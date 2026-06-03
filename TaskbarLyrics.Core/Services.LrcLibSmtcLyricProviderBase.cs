using System.Collections.Concurrent;
using System.Diagnostics;
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
    private const int MinimumSearchScore = 70;
    private const int ImmediateStructuredSearchScore = 95;
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly Regex LrcTimestampRegex = new(@"\[(\d{1,2})(?:[:\uFF1A])(\d{2})(?:[\.\uFF0E:\uFF1A](\d{1,3}))?\]", RegexOptions.Compiled);
    private static readonly Regex OffsetRegex = new(@"\[offset\s*[:\uFF1A]\s*(?<value>[+-]?\d+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
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

        var payload = await FetchLyricsPayloadAsync(
            track.SourceApp,
            track.Title,
            track.Artist,
            track.Duration,
            cancellationToken);
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
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(sourceApp, title, artist, duration);
        if (TryGetCachedPayload(cacheKey, out var cached) && HasAnyLyrics(cached))
        {
            return cached;
        }

        var structured = await SearchStructuredCandidatesAsync(
            title,
            artist,
            duration,
            cancellationToken);
        if (structured is not null)
        {
            StoreCachedPayload(cacheKey, structured.Payload);
            return structured.Payload;
        }

        var searched = await SearchPayloadAsync(title, artist, duration, cancellationToken);
        if (searched is not null)
        {
            StoreCachedPayload(cacheKey, searched.Payload);
        }

        return searched?.Payload;
    }

    private static async Task<SearchResult?> SearchStructuredCandidatesAsync(
        string title,
        string artist,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var candidates = BuildStructuredSearchCandidates(title, artist).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var firstCandidate = candidates[0];
        var best = await SearchStructuredSingleCandidateAsync(
            firstCandidate.Title,
            firstCandidate.Artist,
            title,
            artist,
            duration,
            cancellationToken);
        if (best?.Score >= ImmediateStructuredSearchScore || candidates.Count == 1)
        {
            return best;
        }

        var relaxedResults = await RunSearchesAsync(
            candidates
                .Skip(1)
                .Select(candidate => new Func<Task<SearchResult?>>(() =>
                    SearchStructuredSingleCandidateAsync(
                        candidate.Title,
                        candidate.Artist,
                        title,
                        artist,
                        duration,
                        cancellationToken))),
            cancellationToken);
        return SelectBest(relaxedResults.Append(best));
    }

    private static async Task<SearchResult?> SearchStructuredSingleCandidateAsync(
        string queryTitle,
        string queryArtist,
        string targetTitle,
        string targetArtist,
        TimeSpan targetDuration,
        CancellationToken cancellationToken)
    {
        var trackName = Uri.EscapeDataString(queryTitle ?? string.Empty);
        var artistName = Uri.EscapeDataString(queryArtist ?? string.Empty);
        var url = $"https://lrclib.net/api/search?track_name={trackName}&artist_name={artistName}";

        try
        {
            using var response = await Http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return FindBestSearchResult(json.RootElement, targetTitle, targetArtist, targetDuration);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<SearchResult?> SearchPayloadAsync(
        string title,
        string artist,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var queries = BuildSearchQueries(title, artist).ToList();
        if (queries.Count == 0)
        {
            return null;
        }

        var results = await RunSearchesAsync(
            queries.Select(query => new Func<Task<SearchResult?>>(() =>
                SearchSingleQueryAsync(query, title, artist, duration, cancellationToken))),
            cancellationToken);
        return SelectBest(results);
    }

    private static async Task<IReadOnlyList<SearchResult?>> RunSearchesAsync(
        IEnumerable<Func<Task<SearchResult?>>> searches,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResult?>();
        foreach (var batch in searches.Chunk(SearchParallelism))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchResults = await Task.WhenAll(batch.Select(search => search()));
            results.AddRange(batchResults);
            if (batchResults.Any(result => result?.Score >= ImmediateStructuredSearchScore))
            {
                break;
            }
        }

        return results;
    }

    private static SearchResult? SelectBest(IEnumerable<SearchResult?> results)
    {
        SearchResult? best = null;
        foreach (var result in results)
        {
            if (result is not null && (best is null || result.Score > best.Score))
            {
                best = result;
            }
        }

        return best;
    }

    private static async Task<SearchResult?> SearchSingleQueryAsync(
        string query,
        string targetTitle,
        string targetArtist,
        TimeSpan targetDuration,
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

            return FindBestSearchResult(json.RootElement, targetTitle, targetArtist, targetDuration);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LrcLib] Search error: {ex.Message}");
            return null;
        }
    }

    private static SearchResult? FindBestSearchResult(
        JsonElement root,
        string targetTitle,
        string targetArtist,
        TimeSpan targetDuration)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        SearchResult? best = null;
        foreach (var item in root.EnumerateArray())
        {
            var payload = ExtractPayload(item);
            if (!HasAnyLyrics(payload))
            {
                continue;
            }

            var itemTitle = GetStringProperty(item, "trackName", "track_name", "name", "title");
            var itemArtist = GetStringProperty(item, "artistName", "artist_name", "artist");

            var itemDuration = 0;
            if (item.TryGetProperty("duration", out var durationProperty) &&
                durationProperty.ValueKind == JsonValueKind.Number)
            {
                itemDuration = (int)durationProperty.GetDouble();
            }

            var score = ScoreSearchResult(
                targetTitle,
                targetArtist,
                targetDuration,
                itemTitle,
                itemArtist,
                itemDuration);
            if (score < MinimumSearchScore)
            {
                continue;
            }

            var candidate = new SearchResult(score, payload!.Value);
            if (best is null || candidate.Score > best.Score)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static IEnumerable<(string Title, string Artist)> BuildStructuredSearchCandidates(string title, string artist)
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

    private static int ScoreSearchResult(
        string targetTitle,
        string targetArtist,
        TimeSpan targetDuration,
        string? resultTitle,
        string? resultArtist,
        int resultDurationInSeconds = 0)
    {
        if (string.IsNullOrWhiteSpace(resultTitle)) return 0;

        var target = new TrackInfo(
            "lrclib-search",
            targetTitle,
            targetArtist,
            "LrcLib",
            targetDuration);
        return LyricMatcher.Score(target, resultTitle, resultArtist ?? string.Empty, resultDurationInSeconds);
    }

    private static int ScoreField(string target, string result, int exact, int contains, int overlap)
    {
        // Deprecated helper but keeping for now if used elsewhere
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(result)) return 0;
        if (target == result) return exact;
        if (target.Contains(result, StringComparison.Ordinal) || result.Contains(target, StringComparison.Ordinal)) return contains;
        var commonPrefix = 0;
        var max = Math.Min(target.Length, result.Length);
        for (var i = 0; i < max; i++) { if (target[i] != result[i]) break; commonPrefix++; }
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

    private static string BuildCacheKey(string sourceApp, string title, string artist, TimeSpan duration)
    {
        var sourceKey = NormalizeForMatch(sourceApp);
        var titleKey = NormalizeForMatch(title);
        var artistKey = NormalizeForMatch(artist);
        var durationKey = duration > TimeSpan.Zero
            ? (int)Math.Round(duration.TotalSeconds / 2, MidpointRounding.AwayFromZero) * 2
            : 0;
        return $"{sourceKey}|{titleKey}|{artistKey}|{durationKey}";
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
        var offsetMilliseconds = 0;
        var offsetMatch = OffsetRegex.Match(lrc);
        if (offsetMatch.Success &&
            int.TryParse(offsetMatch.Groups["value"].Value, out var parsedOffset))
        {
            offsetMilliseconds = parsedOffset;
        }
        var lines = lrc.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var rawLine in lines)
        {
            var matches = LrcTimestampRegex.Matches(rawLine);
            if (matches.Count == 0)
            {
                continue;
            }

            var textStart = matches[^1].Index + matches[^1].Length;
            var text = NormalizeLyricText(textStart < rawLine.Length ? rawLine[textStart..] : string.Empty);
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

                var timestamp = new TimeSpan(0, 0, minute, second, millisecond)
                    .Add(TimeSpan.FromMilliseconds(offsetMilliseconds));
                result.Add(new LyricLine(ClampTimestamp(timestamp), text));
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

    private static TimeSpan ClampTimestamp(TimeSpan timestamp)
    {
        return timestamp < TimeSpan.Zero ? TimeSpan.Zero : timestamp;
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
