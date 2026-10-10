using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Services;

/// <summary>
/// QQ 音乐歌词通道：使用歌曲搜索与歌词下载接口，歌词接口只接受数字 songid。
/// </summary>
public sealed class QqMusicLyricSource : ILyricSource
{
    public LyricProviderId ProviderId => KnownLyricProviders.QQMusic;

    public async Task<IReadOnlyList<SourceTrackCandidate>> SearchAsync(
        LyricSearchPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (CanUseDirectSongId(plan.OriginalTrack))
        {
            return
            [
                new SourceTrackCandidate(
                    ProviderId,
                    plan.OriginalTrack.SongId!,
                    plan.OriginalTrack.Title,
                    plan.OriginalTrack.Artists,
                    plan.OriginalTrack.Album,
                    plan.OriginalTrack.Duration,
                    "direct-song-id",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["direct"] = "true"
                    })
            ];
        }

        return await LyricSearchStageExecutor.ExecuteAsync(
            plan,
            async (variant, token) =>
            {
                var json = await QqMusicApiClient.SearchAsync(BuildQuery(variant), token);
                return QqMusicResponseMapper
                    .MapSearchResponse(json)
                    .Select(song => MapSong(song, variant))
                    .ToArray();
            },
            cancellationToken);
    }

    public async Task<RawLyricPayload?> FetchAsync(
        SourceTrackCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var download = await QqMusicApiClient.DownloadLyricsAsync(candidate.CandidateId, cancellationToken);
        var payload = QqMusicResponseMapper.MapLyricDownload(candidate, download);
        if (payload is not null)
        {
            return payload;
        }

        if (!candidate.FetchMetadata.TryGetValue("mid", out var mid) || string.IsNullOrWhiteSpace(mid))
        {
            return null;
        }

        var legacy = await QqMusicApiClient.DownloadLegacyLyricAsync(mid, cancellationToken);
        return QqMusicResponseMapper.MapLegacyLyric(candidate, legacy);
    }

    /// <summary>
    /// SMTC 的 QQ- 值可能是字母 songmid，而歌词接口只接受数字 songid，此时回退到关键词搜索。
    /// </summary>
    internal static bool CanUseDirectSongId(TrackIdentity track) =>
        ProviderSongIdPolicy.CanUseDirectSongId(track, KnownLyricProviders.QQMusic) &&
        QqMusicResponseMapper.IsUsableSongId(track.SongId);

    internal static SourceTrackCandidate MapSong(QqMusicSearchSong song, SearchQueryVariant variant) =>
        new(
            KnownLyricProviders.QQMusic,
            song.SongId,
            song.Title,
            song.Artists,
            song.Album,
            song.Duration,
            variant.Id,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mid"] = song.Mid
            });

    private static string BuildQuery(SearchQueryVariant variant) =>
        string.Join(' ', new[] { variant.Title }.Concat(variant.Artists));
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
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("song", out var song) ||
                !song.TryGetProperty("list", out var list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

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
        if (string.IsNullOrEmpty(decoded) ||
            !decoded.Contains("LyricContent", StringComparison.Ordinal))
        {
            return decoded ?? string.Empty;
        }

        var match = Lyric1ContentRegex().Match(decoded);
        if (!match.Success)
        {
            match = AnyLyricContentRegex().Match(decoded);
        }

        return match.Success ? UnescapeXmlEntities(match.Groups["value"].Value) : decoded;
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
        if (!item.TryGetProperty("singer", out var singers) || singers.ValueKind != JsonValueKind.Array)
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
        if (!element.TryGetProperty(property, out var value))
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
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var seconds))
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(Math.Max(0, seconds));
    }

    private static string UnescapeXmlEntities(string value) =>
        value
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&quot;", "\"", StringComparison.Ordinal)
            .Replace("&apos;", "'", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal);

    [GeneratedRegex(@"<(?<tag>[A-Za-z_][\w\-]*)(?:\s[^>]*)?>\s*<!\[CDATA\[(?<value>.*?)\]\]>", RegexOptions.Singleline)]
    private static partial Regex CDataRegex();

    [GeneratedRegex(@"Lyric_1[^>]*LyricContent=""(?<value>.*?)""(?=\s*(?:/>|>))", RegexOptions.Singleline)]
    private static partial Regex Lyric1ContentRegex();

    [GeneratedRegex(@"LyricContent=""(?<value>.*?)""(?=\s*(?:/>|>))", RegexOptions.Singleline)]
    private static partial Regex AnyLyricContentRegex();
}
