using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Core.Services;

public abstract class LrcLibSmtcLyricProviderBase : ILyricProvider
{
    private const bool EnableTraditionalToSimplified = false;
    private const int SearchParallelism = 3;
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly Regex LrcRegex = new(@"\[(\d{1,2})(?:[:\uFF1A])(\d{2})(?:[\.\uFF0E:\uFF1A](\d{1,3}))?\]([^\r\n]*)", RegexOptions.Compiled);
    private static readonly Regex BracketSuffixRegex = new(@"\s*[\(\[\{（【].*?[\)\]\}）】]\s*", RegexOptions.Compiled);
    private static readonly Regex FeatureSuffixRegex = new(@"\s+(feat\.?|ft\.?|with)\s+.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly ConcurrentDictionary<string, ProviderCacheState> ProviderCaches = new(StringComparer.OrdinalIgnoreCase);

    protected LrcLibSmtcLyricProviderBase(string sourceApp, string cacheFileName, bool strictSourceMatch = true)
    {
        SourceApp = sourceApp;
        CacheFileName = cacheFileName;
        StrictSourceMatch = strictSourceMatch;
    }

    public string SourceApp { get; }

    protected string CacheFileName { get; }

    protected bool StrictSourceMatch { get; }

    public async Task<LyricDocument?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        if (!CanHandleTrack(track))
        {
            return null;
        }

        if (string.Equals(track.Title, "Unknown Title", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var payload = await FetchLyricsPayloadAsync(track.SourceApp, track.Title, track.Artist, cancellationToken);
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

    protected static void ClearCacheFile(string cacheFileName)
    {
        var state = ProviderCaches.GetOrAdd(cacheFileName, _ => new ProviderCacheState());
        state.MemoryCache.Clear();

        lock (state.DiskCacheLock)
        {
            state.DiskCache = new Dictionary<string, CachedLyrics>(StringComparer.Ordinal);
            try
            {
                var cacheFilePath = GetCacheFilePath(cacheFileName);
                if (File.Exists(cacheFilePath))
                {
                    File.Delete(cacheFilePath);
                }
            }
            catch
            {
                // Ignore cache delete failures.
            }
        }
    }

    private bool CanHandleTrack(TrackInfo track)
    {
        if (!StrictSourceMatch || string.Equals(SourceApp, "*", StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(track.SourceApp, SourceApp, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(string? SyncedLyrics, string? PlainLyrics)?> FetchLyricsPayloadAsync(
        string sourceApp,
        string title,
        string artist,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(sourceApp, title, artist);
        if (TryGetCachedPayload(cacheKey, out var cached) && HasAnyLyrics(cached))
        {
            return cached;
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

    private static string BuildCacheKey(string sourceApp, string title, string artist)
    {
        var sourceKey = NormalizeForMatch(sourceApp);
        var titleKey = NormalizeForMatch(title);
        var artistKey = NormalizeForMatch(artist);
        return $"{sourceKey}|{titleKey}|{artistKey}";
    }

    private bool TryGetCachedPayload(string cacheKey, out (string? SyncedLyrics, string? PlainLyrics) payload)
    {
        var cacheState = GetOrCreateCacheState();
        if (cacheState.MemoryCache.TryGetValue(cacheKey, out payload))
        {
            return true;
        }

        lock (cacheState.DiskCacheLock)
        {
            EnsureDiskCacheLoaded(cacheState);
            if (cacheState.DiskCache is not null && cacheState.DiskCache.TryGetValue(cacheKey, out var cached))
            {
                payload = (cached.SyncedLyrics, cached.PlainLyrics);
                cacheState.MemoryCache[cacheKey] = payload;
                return true;
            }
        }

        payload = default;
        return false;
    }

    private void StoreCachedPayload(string cacheKey, (string? SyncedLyrics, string? PlainLyrics) payload)
    {
        var cacheState = GetOrCreateCacheState();
        cacheState.MemoryCache[cacheKey] = payload;

        lock (cacheState.DiskCacheLock)
        {
            EnsureDiskCacheLoaded(cacheState);
            cacheState.DiskCache ??= new Dictionary<string, CachedLyrics>(StringComparer.Ordinal);
            cacheState.DiskCache[cacheKey] = new CachedLyrics
            {
                SyncedLyrics = payload.SyncedLyrics,
                PlainLyrics = payload.PlainLyrics
            };

            try
            {
                var path = GetCacheFilePath(CacheFileName);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(cacheState.DiskCache);
                File.WriteAllText(path, json);
            }
            catch
            {
                // Ignore disk cache write failures.
            }
        }
    }

    private void EnsureDiskCacheLoaded(ProviderCacheState cacheState)
    {
        if (cacheState.DiskCache is not null)
        {
            return;
        }

        try
        {
            var path = GetCacheFilePath(CacheFileName);
            if (!File.Exists(path))
            {
                cacheState.DiskCache = new Dictionary<string, CachedLyrics>(StringComparer.Ordinal);
                return;
            }

            var json = File.ReadAllText(path);
            cacheState.DiskCache = JsonSerializer.Deserialize<Dictionary<string, CachedLyrics>>(json)
                ?? new Dictionary<string, CachedLyrics>(StringComparer.Ordinal);
        }
        catch
        {
            cacheState.DiskCache = new Dictionary<string, CachedLyrics>(StringComparer.Ordinal);
        }
    }

    private static string GetCacheFilePath(string cacheFileName)
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskbarLyrics",
            "cache",
            cacheFileName);
    }

    private ProviderCacheState GetOrCreateCacheState()
    {
        return ProviderCaches.GetOrAdd(CacheFileName, _ => new ProviderCacheState());
    }

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
        var normalized = text
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
        return client;
    }

    private sealed record SearchResult(int Score, (string? SyncedLyrics, string? PlainLyrics) Payload);

    private sealed class CachedLyrics
    {
        public string? SyncedLyrics { get; set; }

        public string? PlainLyrics { get; set; }
    }

    private sealed class ProviderCacheState
    {
        public ConcurrentDictionary<string, (string? SyncedLyrics, string? PlainLyrics)> MemoryCache { get; } =
            new(StringComparer.Ordinal);

        public object DiskCacheLock { get; } = new();

        public Dictionary<string, CachedLyrics>? DiskCache { get; set; }
    }
}
