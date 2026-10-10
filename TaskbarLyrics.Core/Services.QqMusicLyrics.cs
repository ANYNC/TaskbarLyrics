using System.Globalization;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Core.Services;

/// <summary>
/// QQ 音乐歌词通道：使用歌曲搜索与歌词下载接口，歌词接口只接受数字 songid。
/// </summary>
public sealed class QqMusicLyricSource : ILyricSource, IDisposable
{
    private readonly QqMusicApiClient _api;
    private readonly LyricifyPayloadDecoder _decoder = new();
    private readonly LyricifyPayloadParser _parser = new();

    public QqMusicLyricSource() : this(new QqMusicApiClient())
    {
    }

    internal QqMusicLyricSource(QqMusicApiClient api) =>
        _api = api ?? throw new ArgumentNullException(nameof(api));

    public LyricProviderId ProviderId => KnownLyricProviders.QQMusic;

    public async Task<IReadOnlyList<SourceTrackCandidate>> SearchAsync(
        LyricSearchPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        var track = plan.OriginalTrack;
        if (CanUseDirectSongId(track))
        {
            return [CreateDirectCandidate(track, track.SongId!.Trim(), string.Empty)];
        }

        if (ProviderSongIdPolicy.CanUseDirectSongId(track, ProviderId))
        {
            var song = await TryGetSongAsync(track.SongId!, cancellationToken).ConfigureAwait(false);
            if (song is not null)
            {
                return [CreateDirectCandidate(track, song.SongId, song.Mid)];
            }
        }

        return await LyricSearchStageExecutor.ExecuteAsync(
            plan,
            async (variant, token) =>
            {
                var json = await _api.SearchAsync(BuildQuery(variant), token).ConfigureAwait(false);
                QqMusicResponseMapper.ValidateSearchResponse(json);
                return QqMusicResponseMapper.MapSearchResponse(json)
                    .Select(song => MapSong(song, variant))
                    .ToArray();
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<RawLyricPayload?> FetchAsync(
        SourceTrackCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        Exception? primaryFailure = null;
        try
        {
            var download = await _api.DownloadLyricsAsync(candidate.CandidateId, cancellationToken).ConfigureAwait(false);
            var mapped = QqMusicResponseMapper.MapLyricDownload(candidate, download);
            if (string.IsNullOrWhiteSpace(download) || !download.Contains("<content", StringComparison.Ordinal))
            {
                throw new FormatException("QQ lyric download returned an invalid response.");
            }

            var payload = await PreparePayloadAsync(
                mapped, cancellationToken).ConfigureAwait(false);
            if (payload is not null)
            {
                return payload;
            }
        }
        catch (Exception exception) when (IsRecoverableProviderFailure(exception))
        {
            primaryFailure = exception;
            Log.Warn($"QQ lyric download rejected. Candidate='{candidate.CandidateId}' Error='{exception.Message}'");
        }

        if (!candidate.FetchMetadata.TryGetValue("mid", out var mid) || string.IsNullOrWhiteSpace(mid))
        {
            var song = await GetSongAsync(candidate.CandidateId, cancellationToken).ConfigureAwait(false);
            mid = song?.Mid;
        }

        if (!string.IsNullOrWhiteSpace(mid))
        {
            var legacy = await _api.DownloadLegacyLyricAsync(mid, cancellationToken).ConfigureAwait(false);
            QqMusicResponseMapper.ValidateLegacyResponse(legacy);
            var payload = await PreparePayloadAsync(
                QqMusicResponseMapper.MapLegacyLyric(candidate, legacy), cancellationToken).ConfigureAwait(false);
            if (payload is not null)
            {
                return payload;
            }
        }

        // 不把网络/业务失败降格成“没有歌词”；协调器仍能使用原来的失败分类和重试策略。
        if (primaryFailure is not null)
        {
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        return null;
    }

    private async Task<RawLyricPayload?> PreparePayloadAsync(
        RawLyricPayload? payload, CancellationToken cancellationToken)
    {
        if (payload is null)
        {
            return null;
        }

        var decoded = await _decoder.DecodeAsync(payload, cancellationToken).ConfigureAwait(false);
        await _parser.ParseAsync(decoded, cancellationToken).ConfigureAwait(false);
        // 在 QQ 源内部确认内容有效后再交给管道，确保失败时仍能回退同歌曲的 LRC。
        return new RawLyricPayload(
            payload.ProviderId, payload.CandidateId, payload.Format,
            decoded.OriginalLyrics, decoded.TranslationLyrics, false,
            payload.IsPureMusic, payload.Diagnostics, payload.HasStableIdentity);
    }

    private async Task<QqMusicSearchSong?> TryGetSongAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            return await GetSongAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsRecoverableProviderFailure(exception))
        {
            Log.Warn($"QQ song identity lookup failed. Id='{id}' Error='{exception.Message}'");
            return null;
        }
    }

    private async Task<QqMusicSearchSong?> GetSongAsync(string id, CancellationToken cancellationToken)
    {
        var response = await _api.GetSongAsync(id, cancellationToken).ConfigureAwait(false);
        return QqMusicResponseMapper.MapSongDetail(response, id);
    }

    private static bool IsRecoverableProviderFailure(Exception exception) =>
        exception is HttpRequestException or QqMusicApiException or FormatException;

    private SourceTrackCandidate CreateDirectCandidate(TrackIdentity track, string id, string mid) =>
        new(ProviderId, id, track.Title, track.Artists, track.Album, track.Duration,
            "direct-song-id", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["direct"] = "true",
                ["mid"] = mid
            });

    internal static bool CanUseDirectSongId(TrackIdentity track) =>
        ProviderSongIdPolicy.CanUseDirectSongId(track, KnownLyricProviders.QQMusic) &&
        QqMusicResponseMapper.IsUsableSongId(track.SongId);

    internal static SourceTrackCandidate MapSong(QqMusicSearchSong song, SearchQueryVariant variant) =>
        new(
            KnownLyricProviders.QQMusic, song.SongId, song.Title, song.Artists, song.Album,
            song.Duration, variant.Id, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mid"] = song.Mid
            });

    private static string BuildQuery(SearchQueryVariant variant) =>
        string.Join(' ', new[] { variant.Title }.Concat(variant.Artists));

    public void Dispose() => _api.Dispose();
}

internal sealed class QqMusicApiException(int code) : Exception($"QQ Music API returned code {code}.")
{
    // 2001 是本次复现的搜索拒绝；未知业务码不自动重试。
    public bool IsTransient => code == 2001;
}

internal sealed record QqMusicSearchSong(
    string SongId,
    string Mid,
    string Title,
    IReadOnlyList<string> Artists,
    string Album,
    TimeSpan Duration);

/// <summary>
/// QQ 音乐响应到领域模型的纯映射逻辑，不涉及网络，便于回归测试。
/// </summary>
internal static partial class QqMusicResponseMapper
{
    private static readonly IReadOnlyDictionary<string, string> NoDiagnostics =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly string[] EmptyArtists = [];

    public static IReadOnlyList<QqMusicSearchSong> MapSearchResponse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }

        using (document)
        {
            if (!TryGetProperty(document.RootElement, "data", out var data) ||
                !TryGetProperty(data, "song", out var song) ||
                !TryGetProperty(song, "list", out var list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            EnsureSuccess(document.RootElement);
            var songs = new List<QqMusicSearchSong>();
            foreach (var item in list.EnumerateArray())
            {
                var songId = ReadText(item, "songid");
                if (!IsUsableSongId(songId))
                {
                    continue;
                }

                songs.Add(new QqMusicSearchSong(
                    songId,
                    ReadText(item, "songmid"),
                    ReadText(item, "songname"),
                    ReadArtists(item),
                    ReadText(item, "albumname"),
                    ReadSeconds(item, "interval")));
            }

            return songs;
        }
    }

    public static RawLyricPayload? MapLyricDownload(SourceTrackCandidate candidate, string? response)
    {
        var result = DownloadResultRegex().Match(response ?? string.Empty);
        if (result.Success && int.TryParse(result.Groups["code"].Value, out var code) && code != 0)
        {
            throw new QqMusicApiException(code);
        }

        var content = ExtractCData(response, "content");
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var translation = ExtractCData(response, "contentts");
        var encrypted = IsHexPayload(content);
        return new RawLyricPayload(
            KnownLyricProviders.QQMusic,
            candidate.CandidateId,
            encrypted ? LyricPayloadFormat.Qrc : LyricPayloadFormat.Lrc,
            content,
            string.IsNullOrWhiteSpace(translation) ? null : translation,
            encrypted,
            false,
            NoDiagnostics);
    }

    public static RawLyricPayload? MapLegacyLyric(SourceTrackCandidate candidate, string? response)
    {
        var (lyric, translation) = DecodeLegacyJsonp(response);
        return string.IsNullOrWhiteSpace(lyric)
            ? null
            : new RawLyricPayload(
                KnownLyricProviders.QQMusic,
                candidate.CandidateId,
                LyricPayloadFormat.Lrc,
                lyric,
                string.IsNullOrWhiteSpace(translation) ? null : translation,
                false,
                false,
                NoDiagnostics);
    }

    /// <summary>
    /// 旧接口返回 JSONP，lyric/trans 字段为 base64 编码的 UTF-8 文本。
    /// </summary>
    public static (string? Lyric, string? Translation) DecodeLegacyJsonp(string? response)
    {
        var json = TrimJsonpWrapper(response);
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            EnsureSuccess(document.RootElement);
            return (DecodeBase64(document.RootElement, "lyric"), DecodeBase64(document.RootElement, "trans"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// 歌词接口把 CDATA 里的密文解压后包成 QrcInfos XML，真正的 QRC 文本在 Lyric_1 的 LyricContent 属性里。
    /// </summary>
    public static string UnwrapQrcContent(string? decoded)
    {
        if (string.IsNullOrEmpty(decoded) || !decoded.TrimStart().StartsWith('<'))
        {
            return decoded ?? string.Empty;
        }

        try
        {
            // XML 会把属性内的字面换行归一为空格，先转成字符实体以保留 QRC 的行边界。
            var xml = LyricContentAttributeRegex().Replace(decoded, match =>
                match.Groups["prefix"].Value + match.Groups["quote"].Value +
                match.Groups["value"].Value.Replace("\r", "&#13;", StringComparison.Ordinal)
                    .Replace("\n", "&#10;", StringComparison.Ordinal)
                    .Replace("\t", "&#9;", StringComparison.Ordinal) +
                match.Groups["quote"].Value);
            using var reader = XmlReader.Create(new StringReader(xml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = new XmlDocument { XmlResolver = null };
            document.Load(reader);
            var node = document.SelectSingleNode("//Lyric_1[@LyricContent]") ??
                       document.SelectSingleNode("//*[@LyricContent]");
            return node?.Attributes?["LyricContent"]?.Value ??
                   throw new FormatException("QQ QRC XML has no LyricContent.");
        }
        catch (XmlException exception)
        {
            throw new FormatException("QQ QRC XML is invalid.", exception);
        }
    }

    public static void ValidateSearchResponse(string? json)
    {
        using var document = ParseResponse(json);
        EnsureSuccess(document.RootElement);
        if (!TryGetProperty(document.RootElement, "data", out var data) ||
            !TryGetProperty(data, "song", out var song) ||
            !TryGetProperty(song, "list", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("QQ search response has no song list.");
        }
    }

    public static QqMusicSearchSong? MapSongDetail(string? response, string requestedId)
    {
        using var document = ParseResponse(TrimJsonpWrapper(response));
        EnsureSuccess(document.RootElement);
        if (!TryGetProperty(document.RootElement, "data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("QQ song detail response has no song list.");
        }

        foreach (var item in data.EnumerateArray())
        {
            var id = ReadText(item, "id");
            var mid = ReadText(item, "mid");
            if (string.IsNullOrEmpty(id))
            {
                id = ReadText(item, "songid");
            }

            if (string.IsNullOrEmpty(mid))
            {
                mid = ReadText(item, "songmid");
            }
            if (IsUsableSongId(id) &&
                (IsUsableSongId(requestedId) ? id == requestedId.Trim() : mid == requestedId.Trim()))
            {
                return new QqMusicSearchSong(id, mid, ReadText(item, "songname"), ReadArtists(item),
                    ReadText(item, "albumname"), ReadSeconds(item, "interval"));
            }
        }

        return null;
    }

    public static void ValidateLegacyResponse(string? response)
    {
        using var document = ParseResponse(TrimJsonpWrapper(response));
        EnsureSuccess(document.RootElement);
        if (!TryGetProperty(document.RootElement, "lyric", out var lyric) ||
            lyric.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            throw new FormatException("QQ legacy lyric response has no lyric field.");
        }

        if (lyric.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(lyric.GetString()))
        {
            try
            {
                Convert.FromBase64String(lyric.GetString()!);
            }
            catch (FormatException exception)
            {
                throw new FormatException("QQ legacy lyric content is invalid base64.", exception);
            }
        }
    }

    private static JsonDocument ParseResponse(string? response)
    {
        try
        {
            return JsonDocument.Parse(response ?? string.Empty);
        }
        catch (JsonException exception)
        {
            throw new FormatException("QQ Music returned invalid JSON.", exception);
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value);
    }

    private static void EnsureSuccess(JsonElement root)
    {
        foreach (var name in new[] { "code", "retcode" })
        {
            if (!TryGetProperty(root, name, out var value))
            {
                continue;
            }

            var text = ReadText(root, name);
            if (value.ValueKind is not (JsonValueKind.Number or JsonValueKind.String) ||
                !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            {
                throw new FormatException("QQ Music returned an invalid status code.");
            }

            if (code != 0)
            {
                throw new QqMusicApiException(code);
            }
        }
    }

    public static bool IsNumericSongId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().All(char.IsAsciiDigit);

    /// <summary>
    /// 歌词接口需要正的数字 songid；纯 0 不会命中任何歌曲。
    /// </summary>
    public static bool IsUsableSongId(string? value) =>
        IsNumericSongId(value) && value!.Trim().Any(character => character != '0');

    public static bool IsHexPayload(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length % 2 != 0)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    public static string? ExtractCData(string? pseudoXml, string tag)
    {
        if (string.IsNullOrEmpty(pseudoXml))
        {
            return null;
        }

        foreach (Match match in CDataRegex().Matches(pseudoXml))
        {
            if (string.Equals(match.Groups["tag"].Value, tag, StringComparison.Ordinal))
            {
                return match.Groups["value"].Value.Trim();
            }
        }

        return null;
    }

    private static string? TrimJsonpWrapper(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        var text = response.Trim();
        var start = text.IndexOf('(');
        var end = text.LastIndexOf(')');
        return start >= 0 && end > start ? text[(start + 1)..end] : text;
    }

    private static string? DecodeBase64(JsonElement element, string property)
    {
        var encoded = ReadText(element, property);
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadArtists(JsonElement item)
    {
        if (!TryGetProperty(item, "singer", out var singers) || singers.ValueKind != JsonValueKind.Array)
        {
            return EmptyArtists;
        }

        var artists = new List<string>();
        foreach (var singer in singers.EnumerateArray())
        {
            var name = ReadText(singer, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                artists.Add(name);
            }
        }

        return artists;
    }

    private static string ReadText(JsonElement element, string property)
    {
        if (!TryGetProperty(element, property, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
    }

    private static TimeSpan ReadSeconds(JsonElement element, string property)
    {
        if (!TryGetProperty(element, property, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var seconds) ||
            !double.IsFinite(seconds) || seconds >= TimeSpan.MaxValue.TotalSeconds)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(Math.Max(0, seconds));
    }

    [GeneratedRegex(@"<(?<tag>[A-Za-z_][\w\-]*)(?:\s[^>]*)?>\s*<!\[CDATA\[(?<value>.*?)\]\]>", RegexOptions.Singleline)]
    private static partial Regex CDataRegex();

    [GeneratedRegex(@"(?<prefix>\bLyricContent\s*=\s*)(?<quote>[""'])(?<value>.*?)\k<quote>", RegexOptions.Singleline)]
    private static partial Regex LyricContentAttributeRegex();

    [GeneratedRegex(@"<result>\s*(?<code>-?\d+)\s*</result>")]
    private static partial Regex DownloadResultRegex();
}
