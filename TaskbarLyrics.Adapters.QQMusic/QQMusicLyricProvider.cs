using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Adapters.QQMusic;

public sealed class QQMusicLyricProvider : ILyricProvider
{
    private const bool EnableTraditionalToSimplified = false;
    private const int SearchParallelism = 3;
    private const int TitleArtistContainsBonus = 10;
    private const string OfficialSearchEndpoint = "https://c.y.qq.com/soso/fcgi-bin/client_search_cp";
    private const string OfficialLyricEndpoint = "https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg";
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly Regex LrcRegex = new(@"\[(\d{1,2})(?:[:\uFF1A])(\d{2})(?:[\.\uFF0E:\uFF1A](\d{1,3}))?\]([^\r\n]*)", RegexOptions.Compiled);
    private static readonly Regex BracketSuffixRegex = new(@"\s*[\(\[\{（【].*?[\)\]\}）】]\s*", RegexOptions.Compiled);
    private static readonly Regex FeatureSuffixRegex = new(@"\s+(feat\.?|ft\.?|with)\s+.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly ConcurrentDictionary<string, (string? SyncedLyrics, string? PlainLyrics)> MemoryCache = new(StringComparer.Ordinal);
    private static readonly object DiskCacheLock = new();
    private static Dictionary<string, CachedLyrics>? _diskCache;

    public string SourceApp => "QQMusic";

    public async Task<LyricDocument?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(track.SourceApp, SourceApp, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(track.Title, "Unknown Title", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var payload = await FetchLyricsPayloadAsync(track.Title, track.Artist, cancellationToken);
        if (payload is null)
        {
            return null;
        }

        var timed = ParseLrc(payload.Value.SyncedLyrics);
        if (timed.Count > 0)
        {
            return new LyricDocument(timed);
        }

        var plain = ParsePlainLyrics(payload.Value.PlainLyrics);
        if (plain.Count > 0)
        {
            return new LyricDocument(plain);
        }

        return null;
    }

    private static async Task<(string? SyncedLyrics, string? PlainLyrics)?> FetchLyricsPayloadAsync(
        string title,
        string artist,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(title, artist);
        if (TryGetCachedPayload(cacheKey, out var cached) && HasAnyLyrics(cached))
        {
            return cached;
        }

        var official = await FetchOfficialPayloadAsync(title, artist, cancellationToken);
        if (HasAnyLyrics(official))
        {
            StoreCachedPayload(cacheKey, official!.Value);
            return official;
        }

        foreach (var candidate in BuildGetCandidates(title, artist))
        {
            var exact = await FetchExactPayloadAsync(candidate.Title, candidate.Artist, cancellationToken);
            if (!HasAnyLyrics(exact))
            {
                continue;
            }

            StoreCachedPayload(cacheKey, exact.Value);
            return exact;
        }

        var searched = await SearchPayloadAsync(title, artist, cancellationToken);
        if (HasAnyLyrics(searched))
        {
            StoreCachedPayload(cacheKey, searched!.Value);
        }

        return searched;
    }

    private static async Task<(string? SyncedLyrics, string? PlainLyrics)?> FetchOfficialPayloadAsync(
        string title,
        string artist,
        CancellationToken cancellationToken)
    {
        var candidates = await SearchOfficialCandidatesAsync(title, artist, cancellationToken);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.SongMid))
            {
                continue;
            }

            var payload = await FetchOfficialLyricsBySongMidAsync(candidate.SongMid, cancellationToken);
            if (HasAnyLyrics(payload))
            {
                return payload;
            }
        }

        return null;
    }

    private static async Task<List<OfficialSongCandidate>> SearchOfficialCandidatesAsync(
        string title,
        string artist,
        CancellationToken cancellationToken)
    {
        var queries = BuildSearchQueries(title, artist).Take(4).ToList();
        var merged = new List<OfficialSongCandidate>();

        foreach (var query in queries)
        {
            var batch = await SearchOfficialSingleQueryAsync(query, title, artist, cancellationToken);
            if (batch.Count == 0)
            {
                continue;
            }

            merged.AddRange(batch);
        }

        return merged
            .GroupBy(x => x.SongMid, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Score).First())
            .OrderByDescending(x => x.Score)
            .Take(8)
            .ToList();
    }

    private static async Task<List<OfficialSongCandidate>> SearchOfficialSingleQueryAsync(
        string query,
        string targetTitle,
        string targetArtist,
        CancellationToken cancellationToken)
    {
        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"{OfficialSearchEndpoint}?w={encodedQuery}&n=20&p=1&format=json";

        try
        {
            using var response = await Http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new List<OfficialSongCandidate>();
            }

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            var jsonText = ParsePossiblyJsonp(raw);
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                return new List<OfficialSongCandidate>();
            }

            using var json = JsonDocument.Parse(jsonText);
            if (!TryGetOfficialSongList(json.RootElement, out var songs))
            {
                return new List<OfficialSongCandidate>();
            }

            var result = new List<OfficialSongCandidate>();
            foreach (var song in songs.EnumerateArray())
            {
                var songMid = GetStringProperty(song, "songmid", "songMid", "mid");
                if (string.IsNullOrWhiteSpace(songMid))
                {
                    continue;
                }

                var songName = GetStringProperty(song, "songname", "songName", "title", "name");
                var artistName = ExtractOfficialArtistName(song);
                var score = ScoreSearchResult(targetTitle, targetArtist, songName, artistName);
                result.Add(new OfficialSongCandidate(songMid, score));
            }

            return result;
        }
        catch
        {
            return new List<OfficialSongCandidate>();
        }
    }

    private static async Task<(string? SyncedLyrics, string? PlainLyrics)?> FetchOfficialLyricsBySongMidAsync(
        string songMid,
        CancellationToken cancellationToken)
    {
        var encodedMid = Uri.EscapeDataString(songMid);
        var url = $"{OfficialLyricEndpoint}?songmid={encodedMid}&format=json&nobase64=1";

        try
        {
            using var response = await Http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            var jsonText = ParsePossiblyJsonp(raw);
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                return null;
            }

            using var json = JsonDocument.Parse(jsonText);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var lyricRaw = GetStringProperty(root, "lyric", "lrc", "lyricContent");
            var transRaw = GetStringProperty(root, "trans", "transLyric", "trans_lyric");

            var lyricText = DecodeIfBase64(lyricRaw);
            var transText = DecodeIfBase64(transRaw);

            var synced = LooksLikeTimedLyric(lyricText) ? lyricText : null;
            var plain = synced is null ? lyricText : transText;

            return (synced, plain);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetOfficialSongList(JsonElement root, out JsonElement listElement)
    {
        listElement = default;

        if (root.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("song", out var songObj) &&
            songObj.ValueKind == JsonValueKind.Object &&
            songObj.TryGetProperty("list", out var nestedList) &&
            nestedList.ValueKind == JsonValueKind.Array)
        {
            listElement = nestedList;
            return true;
        }

        if (root.TryGetProperty("song", out var song) &&
            song.ValueKind == JsonValueKind.Object &&
            song.TryGetProperty("list", out var list) &&
            list.ValueKind == JsonValueKind.Array)
        {
            listElement = list;
            return true;
        }

        return false;
    }

    private static string ExtractOfficialArtistName(JsonElement songElement)
    {
        if (!songElement.TryGetProperty("singer", out var singerElement) ||
            singerElement.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var names = new List<string>();
        foreach (var singer in singerElement.EnumerateArray())
        {
            var name = GetStringProperty(singer, "name", "singerName");
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name.Trim());
            }
        }

        return string.Join(" / ", names);
    }

    private static string ParsePossiblyJsonp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var open = trimmed.IndexOf('(');
        var close = trimmed.LastIndexOf(')');
        if (open >= 0 && close > open)
        {
            return trimmed[(open + 1)..close].Trim();
        }

        return string.Empty;
    }

    private static string? DecodeIfBase64(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        if (LooksLikeTimedLyric(raw))
        {
            return raw;
        }

        try
        {
            var bytes = Convert.FromBase64String(raw);
            var decoded = Encoding.UTF8.GetString(bytes);
            return string.IsNullOrWhiteSpace(decoded) ? raw : decoded;
        }
        catch
        {
            return raw;
        }
    }

    private static bool LooksLikeTimedLyric(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains('[', StringComparison.Ordinal) &&
               value.Contains(':', StringComparison.Ordinal);
    }

    private static async Task<(string? SyncedLyrics, string? PlainLyrics)?> FetchExactPayloadAsync(
        string title,
        string artist,
        CancellationToken cancellationToken)
    {
        var trackName = Uri.EscapeDataString(title ?? string.Empty);
        var artistName = Uri.EscapeDataString(artist ?? string.Empty);
        var url = $"https://lrclib.net/api/get?track_name={trackName}&artist_name={artistName}";

        try
        {
            using var response = await Http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return ExtractPayload(json.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<(string? SyncedLyrics, string? PlainLyrics)?> SearchPayloadAsync(
        string title,
        string artist,
        CancellationToken cancellationToken)
    {
        var queries = BuildSearchQueries(title, artist).ToList();
        if (queries.Count == 0)
        {
            return null;
        }

        using var semaphore = new SemaphoreSlim(SearchParallelism, SearchParallelism);
        var tasks = queries.Select(async query =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await SearchSingleQueryAsync(query, title, artist, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        var results = await Task.WhenAll(tasks);

        SearchResult? best = null;
        foreach (var result in results)
        {
            if (result is null)
            {
                continue;
            }

            if (best is null || result.Score > best.Score)
            {
                best = result;
            }
        }

        return best?.Payload;
    }

    private static async Task<SearchResult?> SearchSingleQueryAsync(
        string query,
        string targetTitle,
        string targetArtist,
        CancellationToken cancellationToken)
    {
        var encoded = Uri.EscapeDataString(query);
        var url = $"https://lrclib.net/api/search?q={encoded}";

        try
        {
            using var response = await Http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (json.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            SearchResult? best = null;
            foreach (var item in json.RootElement.EnumerateArray())
            {
                var payload = ExtractPayload(item);
                if (!HasAnyLyrics(payload))
                {
                    continue;
                }

                var itemTitle = GetStringProperty(item, "trackName", "track_name", "name", "title");
                var itemArtist = GetStringProperty(item, "artistName", "artist_name", "artist");
                var score = ScoreSearchResult(targetTitle, targetArtist, itemTitle, itemArtist);

                var candidate = new SearchResult(score, payload!.Value);
                if (best is null || candidate.Score > best.Score)
                {
                    best = candidate;
                }
            }

            return best;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<(string Title, string Artist)> BuildGetCandidates(string title, string artist)
    {
        var list = new List<(string Title, string Artist)>();

        void Add(string t, string a)
        {
            var key = $"{t}\u001f{a}";
            if (!list.Any(x => string.Equals($"{x.Title}\u001f{x.Artist}", key, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add((t, a));
            }
        }

        var normalizedTitle = NormalizeTitleForQuery(title);
        var primaryArtist = GetPrimaryArtist(artist);

        Add(title, artist);
        Add(normalizedTitle, artist);
        Add(title, primaryArtist);
        Add(normalizedTitle, primaryArtist);

        foreach (var segment in SplitByDash(title ?? string.Empty))
        {
            Add(segment, artist);
            Add(segment, primaryArtist);
        }

        return list.Where(x => !string.IsNullOrWhiteSpace(x.Title));
    }

    private static IEnumerable<string> BuildSearchQueries(string title, string artist)
    {
        var queries = new List<string>();

        void Add(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var trimmed = value.Trim();
            if (!queries.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                queries.Add(trimmed);
            }
        }

        var normalizedTitle = NormalizeTitleForQuery(title);
        var normalizedArtist = NormalizeArtistForQuery(artist);

        Add($"{title} {artist}".Trim());
        Add($"{normalizedTitle} {normalizedArtist}".Trim());
        Add(title ?? string.Empty);
        Add(normalizedTitle);

        if (!string.IsNullOrWhiteSpace(artist))
        {
            Add($"{title} {GetPrimaryArtist(artist)}".Trim());
            Add($"{normalizedTitle} {GetPrimaryArtist(artist)}".Trim());
        }

        foreach (var segment in SplitByDash(title ?? string.Empty))
        {
            Add($"{segment} {artist}".Trim());
            Add(segment);
        }

        return queries;
    }

    private static string NormalizeTitleForQuery(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var value = BracketSuffixRegex.Replace(title, " ");
        value = FeatureSuffixRegex.Replace(value, string.Empty);
        return CollapseWhitespace(value);
    }

    private static string NormalizeArtistForQuery(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
        {
            return string.Empty;
        }

        return GetPrimaryArtist(artist);
    }

    private static string GetPrimaryArtist(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
        {
            return string.Empty;
        }

        var separators = new[] { "、", "/", ",", "，", "&", " x ", " X ", " feat. ", " feat ", " ft. ", " ft " };
        foreach (var separator in separators)
        {
            var index = artist.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                return CollapseWhitespace(artist[..index]);
            }
        }

        return CollapseWhitespace(artist);
    }

    private static IEnumerable<string> SplitByDash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var separators = new[] { " - ", " – ", " — ", "-", "–", "—" };
        foreach (var separator in separators)
        {
            var parts = value.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 1)
            {
                foreach (var part in parts)
                {
                    if (!string.IsNullOrWhiteSpace(part))
                    {
                        yield return part;
                    }
                }

                yield break;
            }
        }
    }

    private static int ScoreSearchResult(string targetTitle, string targetArtist, string? resultTitle, string? resultArtist)
    {
        var titleTarget = NormalizeForMatch(targetTitle);
        var artistTarget = NormalizeForMatch(targetArtist);
        var titleResult = NormalizeForMatch(resultTitle);
        var artistResult = NormalizeForMatch(resultArtist);

        var score = 0;

        score += ScoreField(titleTarget, titleResult, 100, 60, 30);
        score += ScoreField(artistTarget, artistResult, 60, 35, 15);

        if (IsContainsOrExact(titleTarget, titleResult) &&
            IsContainsOrExact(artistTarget, artistResult) &&
            !(titleTarget == titleResult && artistTarget == artistResult))
        {
            // Prefer candidates where title + artist are both semantically aligned.
            score += TitleArtistContainsBonus;
        }

        if (!string.IsNullOrWhiteSpace(titleTarget) && !string.IsNullOrWhiteSpace(titleResult) &&
            !string.IsNullOrWhiteSpace(artistTarget) && !string.IsNullOrWhiteSpace(artistResult) &&
            titleTarget == titleResult && artistTarget == artistResult)
        {
            score += 80;
        }

        return score;
    }

    private static int ScoreField(string target, string result, int exact, int contains, int overlap)
    {
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(result))
        {
            return 0;
        }

        if (target == result)
        {
            return exact;
        }

        if (target.Contains(result, StringComparison.Ordinal) || result.Contains(target, StringComparison.Ordinal))
        {
            return contains;
        }

        var commonPrefix = 0;
        var max = Math.Min(target.Length, result.Length);
        for (var i = 0; i < max; i++)
        {
            if (target[i] != result[i])
            {
                break;
            }

            commonPrefix++;
        }

        return commonPrefix >= 2 ? overlap : 0;
    }

    private static bool IsContainsOrExact(string target, string result)
    {
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(result))
        {
            return false;
        }

        return target == result ||
               target.Contains(result, StringComparison.Ordinal) ||
               result.Contains(target, StringComparison.Ordinal);
    }

    private static string NormalizeForMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var buffer = new char[value.Length];
        var idx = 0;

        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[idx++] = ch;
            }
        }

        return idx == 0 ? string.Empty : new string(buffer, 0, idx);
    }

    private static string BuildCacheKey(string title, string artist)
    {
        var titleKey = NormalizeForMatch(title);
        var artistKey = NormalizeForMatch(artist);
        return $"{titleKey}|{artistKey}";
    }

    private static bool TryGetCachedPayload(string cacheKey, out (string? SyncedLyrics, string? PlainLyrics) payload)
    {
        if (MemoryCache.TryGetValue(cacheKey, out payload))
        {
            return true;
        }

        lock (DiskCacheLock)
        {
            EnsureDiskCacheLoaded();
            if (_diskCache is not null && _diskCache.TryGetValue(cacheKey, out var cached))
            {
                payload = (cached.SyncedLyrics, cached.PlainLyrics);
                MemoryCache[cacheKey] = payload;
                return true;
            }
        }

        payload = default;
        return false;
    }

    private static void StoreCachedPayload(string cacheKey, (string? SyncedLyrics, string? PlainLyrics) payload)
    {
        MemoryCache[cacheKey] = payload;

        lock (DiskCacheLock)
        {
            EnsureDiskCacheLoaded();
            _diskCache ??= new Dictionary<string, CachedLyrics>(StringComparer.Ordinal);
            _diskCache[cacheKey] = new CachedLyrics
            {
                SyncedLyrics = payload.SyncedLyrics,
                PlainLyrics = payload.PlainLyrics
            };

            try
            {
                var dir = Path.GetDirectoryName(CacheFilePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(_diskCache);
                File.WriteAllText(CacheFilePath, json);
            }
            catch
            {
                // Ignore disk cache write failures.
            }
        }
    }

    private static void EnsureDiskCacheLoaded()
    {
        if (_diskCache is not null)
        {
            return;
        }

        try
        {
            if (!File.Exists(CacheFilePath))
            {
                _diskCache = new Dictionary<string, CachedLyrics>(StringComparer.Ordinal);
                return;
            }

            var json = File.ReadAllText(CacheFilePath);
            _diskCache = JsonSerializer.Deserialize<Dictionary<string, CachedLyrics>>(json)
                ?? new Dictionary<string, CachedLyrics>(StringComparer.Ordinal);
        }
        catch
        {
            _diskCache = new Dictionary<string, CachedLyrics>(StringComparer.Ordinal);
        }
    }

    private static string CacheFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TaskbarLyrics",
        "cache",
        "qqmusic-lyrics.json");

    private static string CollapseWhitespace(string value)
    {
        return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string? GetStringProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }

        return null;
    }

    private static (string? SyncedLyrics, string? PlainLyrics)? ExtractPayload(JsonElement element)
    {
        string? synced = null;
        string? plain = null;

        if (element.TryGetProperty("syncedLyrics", out var syncedElement) &&
            syncedElement.ValueKind == JsonValueKind.String)
        {
            synced = syncedElement.GetString();
        }

        if (element.TryGetProperty("plainLyrics", out var plainElement) &&
            plainElement.ValueKind == JsonValueKind.String)
        {
            plain = plainElement.GetString();
        }

        return (synced, plain);
    }

    private static bool HasAnyLyrics((string? SyncedLyrics, string? PlainLyrics)? payload)
    {
        return payload is not null &&
               (!string.IsNullOrWhiteSpace(payload.Value.SyncedLyrics) ||
                !string.IsNullOrWhiteSpace(payload.Value.PlainLyrics));
    }

    private static List<LyricLine> ParseLrc(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return new List<LyricLine>();
        }

        var result = new List<LyricLine>();
        var lines = lrc.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var rawLine in lines)
        {
            var matches = LrcRegex.Matches(rawLine);
            if (matches.Count == 0)
            {
                continue;
            }

            var text = NormalizeLyricText(matches[^1].Groups[4].Value);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (Match match in matches)
            {
                var minute = int.Parse(match.Groups[1].Value);
                var second = int.Parse(match.Groups[2].Value);
                var fractionRaw = match.Groups[3].Value;
                var millisecond = ParseMillisecond(fractionRaw);

                result.Add(new LyricLine(new TimeSpan(0, 0, minute, second, millisecond), text));
            }
        }

        return result.OrderBy(x => x.Timestamp).ToList();
    }

    private static List<LyricLine> ParsePlainLyrics(string? plainLyrics)
    {
        if (string.IsNullOrWhiteSpace(plainLyrics))
        {
            return new List<LyricLine>();
        }

        var result = new List<LyricLine>();
        var lines = plainLyrics.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var index = 0;
        foreach (var rawLine in lines)
        {
            var text = NormalizeLyricText(rawLine);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            result.Add(new LyricLine(TimeSpan.FromSeconds(index * 3), text));
            index++;
        }

        return result;
    }

    private static string NormalizeLyricText(string text)
    {
        var normalized = WebUtility.HtmlDecode(text)
            .Replace("\uFEFF", string.Empty)
            .Replace("\u200B", string.Empty)
            .Trim();

        return EnableTraditionalToSimplified
            ? ChineseScriptConverter.ToSimplified(normalized)
            : normalized;
    }

    private static int ParseMillisecond(string fractionRaw)
    {
        if (string.IsNullOrWhiteSpace(fractionRaw))
        {
            return 0;
        }

        return fractionRaw.Length switch
        {
            1 => int.Parse(fractionRaw) * 100,
            2 => int.Parse(fractionRaw) * 10,
            _ => int.Parse(fractionRaw[..3])
        };
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TaskbarLyrics/1.0");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://y.qq.com/");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://y.qq.com");
        return client;
    }

    public static void ClearCache()
    {
        MemoryCache.Clear();

        lock (DiskCacheLock)
        {
            _diskCache = new Dictionary<string, CachedLyrics>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(CacheFilePath))
                {
                    File.Delete(CacheFilePath);
                }
            }
            catch
            {
                // Ignore cache delete failures.
            }
        }
    }

    private sealed record SearchResult(int Score, (string? SyncedLyrics, string? PlainLyrics) Payload);
    private sealed record OfficialSongCandidate(string SongMid, int Score);

    private sealed class CachedLyrics
    {
        public string? SyncedLyrics { get; set; }

        public string? PlainLyrics { get; set; }
    }
}

