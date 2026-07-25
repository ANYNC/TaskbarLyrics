using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Core.Services;

public abstract class LyricProviderBase : ILyricProvider
{
    // A fuzzy metadata match may be correct at one point in time but wrong after
    // a provider changes its search result. Persist only results tied to the
    // player-reported song ID, so an incorrect fuzzy match cannot become sticky.
    private const int CurrentCacheFormatVersion = 10;

    // --- BetterLyrics 风格的严苛正则 ---

    // 只匹配标准 LRC 时间轴，不进行模糊匹配
    private static readonly Regex LrcTimestampRegex = new(@"\[(\d+)[:：](\d+)(?:[\.\uFF0E:：](\d{1,3}))?\]", RegexOptions.Compiled);

    // 专门移除行内的 QRC 逐字标签（如 <00:12.34>）
    private static readonly Regex InnerTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    // 偏移量解析
    private static readonly Regex OffsetRegex = new(@"\[offset\s*[:：]\s*(?<val>[+-]?\d+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 辅助正则 (用于匹配匹配得分逻辑)
    private static readonly Regex GlobalBracketRegex = new(@"[\[［\(（【][^[\]］\)）】【]*?[\]］\)）】【]", RegexOptions.Compiled);
    private static readonly Regex FeatureSuffixRegex = new(@"\s+(feat\.?|ft\.?|with)\s+.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LeadingCreditRegex = new(
        @"^\s*(?:(?:\u4f5c|\u586b)[\u8bcd\u8a5e]|\u4f5c\u66f2|[\u7f16\u7de8]\u66f2|[\u8bcd\u8a5e]|\u66f2|\u539f\u5531|\u6f14\u5531|\u6b4c\u624b|\u5236\u4f5c\u4eba|[\u76d1\u76e3][\u5236\u88fd]|\u6df7\u97f3|\u6bcd\u5e26|\u51fa\u54c1|[\u5f55\u9304]\u97f3|OP|SP|Composer|Lyricist|Lyrics?|Music|Arranger|Producer|Produced\s+by|Written\s+by|Composed\s+by)\s*[:\uff1a]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly TimeSpan OpeningDuplicateTimestampWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OpeningCreditFilterWindow = TimeSpan.FromSeconds(5);

    private static readonly JsonLyricCacheStore<LyricDocument> CacheStore =
        new JsonLyricCacheStore<LyricDocument>(CacheFilePathStatic);

    protected HttpClient Http { get; }
    private readonly ILyricCacheStore<LyricDocument> _cacheStore;

    static LyricProviderBase()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    protected LyricProviderBase(
        HttpClient httpClient,
        ILyricCacheStore<LyricDocument>? cacheStore = null)
    {
        Http = httpClient;
        _cacheStore = cacheStore ?? CacheStore;
    }

    public abstract string SourceApp { get; }

    public async Task<LyricDocument?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken)
    {
        return (await GetLyricsWithDiagnosticsAsync(track, cancellationToken)).Document;
    }

    public async Task<LyricFetchResult> GetLyricsWithDiagnosticsAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var canUsePersistentCache = HasStableCacheIdentity(track);
        var cacheKey = canUsePersistentCache ? BuildCacheKey(track) : null;
        if (cacheKey is not null &&
            _cacheStore.TryGet(cacheKey, out var cachedDoc, out var acquisition))
        {
            if (HasUsableLines(cachedDoc))
            {
                return new LyricFetchResult(cachedDoc, acquisition, stopwatch.ElapsedMilliseconds);
            }

            _cacheStore.Remove(cacheKey);
            Log.Warn($"Discarded invalid lyric cache entry for '{track.Title}' - '{track.Artist}'.");
        }

        var result = await ResolveRemoteAsync(track, cancellationToken);
        if (result != null)
        {
            result = ProcessDocument(result);
            if (HasUsableLines(result))
            {
                if (cacheKey is not null)
                {
                    _cacheStore.Store(cacheKey, result);
                }
            }
            else
            {
                result = null;
            }
        }
        return new LyricFetchResult(
            result,
            result is null ? LyricAcquisitionKind.NotFound : LyricAcquisitionKind.Remote,
            stopwatch.ElapsedMilliseconds);
    }

    protected abstract Task<LyricDocument?> ResolveRemoteAsync(TrackInfo track, CancellationToken cancellationToken);

    // ========================================================
    // ✅ 第一核心：ProcessDocument (后处理)
    // ========================================================
    private static LyricDocument ProcessDocument(LyricDocument doc)
    {
        var lines = doc.Lines.Select(l => l with
        {
            Text = BetterLyrics_Sanitize(l.Text),
            Translation = l.Translation != null ? BetterLyrics_Sanitize(l.Translation) : null
        })
        .Where(l => !string.IsNullOrWhiteSpace(l.Text) && l.Text != "//")
        .ToList();

        lines = NormalizeOpeningLines(lines);
        lines = LyricLineNormalizer.MergeStandaloneSpeakerLabels(lines);

        return new LyricDocument(EnsureSyllables(lines), doc.BestScore, doc.IsPureMusic);
    }

    // ========================================================
    // ✅ 第二核心：BetterLyrics 风格净化逻辑
    // ========================================================
    private static string BetterLyrics_Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        // 1. HTML 解码
        var text = WebUtility.HtmlDecode(input);

        // 2. 移除逐字标签
        text = InnerTagRegex.Replace(text, string.Empty);

        // 3. 严格字符过滤 (白名单模式)
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // 彻底封杀导致框叉的 \uFFFC 和 \uFFFD 以及 PUA 区
            if ((int)c >= 0xFFF0 || ((int)c >= 0xE000 && (int)c <= 0xF8FF)) continue;

            // 过滤控制字符
            if (char.IsControl(c)) continue;

            // 只允许安全分类
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            bool isSafe = cat switch
            {
                UnicodeCategory.UppercaseLetter or
                UnicodeCategory.LowercaseLetter or
                UnicodeCategory.OtherLetter or      // 汉字/中日韩
                UnicodeCategory.DecimalDigitNumber or
                UnicodeCategory.ConnectorPunctuation or
                UnicodeCategory.DashPunctuation or
                UnicodeCategory.OpenPunctuation or
                UnicodeCategory.ClosePunctuation or
                UnicodeCategory.InitialQuotePunctuation or
                UnicodeCategory.FinalQuotePunctuation or
                UnicodeCategory.OtherPunctuation or
                UnicodeCategory.SpaceSeparator or
                UnicodeCategory.MathSymbol or
                UnicodeCategory.CurrencySymbol or
                UnicodeCategory.ModifierSymbol or
                UnicodeCategory.OtherSymbol => true,
                _ => false
            };

            if (isSafe)
            {
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        sb.Append(c);
                        sb.Append(text[++i]);
                    }
                }
                else sb.Append(c);
            }
        }

        var result = sb.ToString().Trim();

        // 关键：如果净化后不包含任何字母、数字或汉字，直接返回空，这能彻底干掉 [00:41.30] 后的乱码
        if (!ContainsAnyMeaningfulChar(result)) return string.Empty;

        return ChineseScriptConverter.ToSimplified(result).Trim();
    }

    private static bool ContainsAnyMeaningfulChar(string s)
    {
        return s.Any(c => char.IsLetterOrDigit(c) || (int)c > 0x4E00);
    }

    // ========================================================
    // ✅ 第三核心：ParseLrc (逐行精准提取)
    // ========================================================
    protected static List<LyricLine> ParseLrc(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc)) return new List<LyricLine>();

        // 1. 预解码
        lrc = WebUtility.HtmlDecode(lrc);

        // 2. 偏移量解析
        int offsetMs = 0;
        var offsetMatch = OffsetRegex.Match(lrc);
        if (offsetMatch.Success && int.TryParse(offsetMatch.Groups["val"].Value, out var parsedOffset))
            offsetMs = parsedOffset;

        var resultList = new List<LyricLine>();

        // 3. 逐行读取
        var lines = lrc.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            var matches = LrcTimestampRegex.Matches(trimmedLine);
            if (matches.Count == 0) continue;

            // 提取内容：多时间戳行共用最后一个时间戳之后的歌词文本。
            var textStart = matches[^1].Index + matches[^1].Length;
            string rawContent = textStart < trimmedLine.Length ? trimmedLine[textStart..] : string.Empty;

            // 精准净化
            string cleanedContent = BetterLyrics_Sanitize(rawContent);

            if (string.IsNullOrWhiteSpace(cleanedContent)) continue;

            foreach (Match match in matches)
            {
                int min = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                int sec = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                int ms = ParseMillisecond(match.Groups[3].Value);
                var timestamp = new TimeSpan(0, 0, min, sec, ms).Add(TimeSpan.FromMilliseconds(offsetMs));
                resultList.Add(new LyricLine(ClampTimestamp(timestamp), cleanedContent));
            }
        }

        return AlignBilingualLyrics(NormalizeOpeningLines(resultList));
    }

    private static List<LyricLine> NormalizeOpeningLines(List<LyricLine> lines)
    {
        if (lines.Count == 0)
        {
            return lines;
        }

        var sortedLines = lines
            .Select((line, index) => new { Line = line, Index = index })
            .OrderBy(x => x.Line.Timestamp)
            .ThenBy(x => x.Index)
            .Select(x => x.Line)
            .ToList();

        var filteredLines = sortedLines
            .Where(line => line.Timestamp > OpeningCreditFilterWindow || !IsLeadingCreditLine(line.Text))
            .ToList();

        if (filteredLines.Count == 0)
        {
            filteredLines = sortedLines;
        }

        if (filteredLines.Count < 2)
        {
            return filteredLines;
        }

        var normalized = new List<LyricLine>(filteredLines.Count);
        for (var i = 0; i < filteredLines.Count;)
        {
            var timestamp = filteredLines[i].Timestamp;
            var group = new List<LyricLine>();
            do
            {
                group.Add(filteredLines[i]);
                i++;
            }
            while (i < filteredLines.Count && filteredLines[i].Timestamp == timestamp);

            if (timestamp <= OpeningDuplicateTimestampWindow && group.Count > 1)
            {
                var nonCreditLines = group
                    .Where(line => !IsLeadingCreditLine(line.Text))
                    .ToList();

                if (nonCreditLines.Count == group.Count)
                {
                    normalized.AddRange(group);
                    continue;
                }

                if (nonCreditLines.Count > 0)
                {
                    normalized.AddRange(nonCreditLines);
                    continue;
                }

                normalized.Add(group[^1]);
                continue;
            }

            normalized.AddRange(group);
        }

        return normalized;
    }

    private static bool IsLeadingCreditLine(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && LeadingCreditRegex.IsMatch(text);
    }

    private static List<LyricLine> AlignBilingualLyrics(List<LyricLine> rawLines)
    {
        if (rawLines.Count == 0) return rawLines;
        var sorted = rawLines.OrderBy(l => l.Timestamp).ToList();
        var mainTrack = new List<LyricLine>();
        var secondaryTracks = new List<LyricLine>();
        var processedTimestamps = new HashSet<double>();
        foreach (var line in sorted)
        {
            if (processedTimestamps.Add(line.Timestamp.TotalMilliseconds))
                mainTrack.Add(line);
            else
                secondaryTracks.Add(line);
        }
        const double epsilon = 60.0;
        foreach (var secLine in secondaryTracks)
        {
            var match = mainTrack.FirstOrDefault(m => Math.Abs(m.Timestamp.TotalMilliseconds - secLine.Timestamp.TotalMilliseconds) <= epsilon);
            if (match != null)
            {
                int idx = mainTrack.IndexOf(match);
                mainTrack[idx] = mainTrack[idx] with { Translation = secLine.Text };
            }
            else mainTrack.Add(secLine);
        }
        return mainTrack.OrderBy(l => l.Timestamp).ToList();
    }

    private static List<LyricLine> EnsureSyllables(List<LyricLine> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var nextTs = (i + 1 < lines.Count) ? lines[i + 1].Timestamp : line.Timestamp + TimeSpan.FromSeconds(5);
            var duration = Math.Clamp((nextTs - line.Timestamp).TotalMilliseconds, 500, 10000);
            if (line.Text.Length == 0) continue;
            double msPerChar = duration / line.Text.Length;
            lines[i] = line with { Syllables = line.Text.Select((c, idx) => new LyricSyllable(TimeSpan.FromMilliseconds(idx * msPerChar), TimeSpan.FromMilliseconds(msPerChar), c.ToString())).ToList() };
        }
        return lines;
    }

    protected static string DecodeBytesToString(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException)
        {
            try { return Encoding.GetEncoding(936).GetString(bytes); }
            catch { return Encoding.UTF8.GetString(bytes); }
        }
    }

    private static int ParseMillisecond(string fractionRaw)
    {
        if (string.IsNullOrWhiteSpace(fractionRaw)) return 0;
        return fractionRaw.Length switch
        {
            1 => int.Parse(fractionRaw, CultureInfo.InvariantCulture) * 100,
            2 => int.Parse(fractionRaw, CultureInfo.InvariantCulture) * 10,
            _ => int.Parse(fractionRaw[..3], CultureInfo.InvariantCulture)
        };
    }

    private static TimeSpan ClampTimestamp(TimeSpan timestamp)
    {
        return timestamp < TimeSpan.Zero ? TimeSpan.Zero : timestamp;
    }

    // ========================================================
    // ========================================================
    protected string BuildCacheKey(TrackInfo track)
    {
        var provider = NormalizeForCache(SourceApp);
        var source = NormalizeForCache(track.SourceApp);
        var songId = NormalizeForCache(track.SongId ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(songId))
        {
            return $"v{CurrentCacheFormatVersion}|{provider}|{source}|song:{songId}";
        }

        return $"v{CurrentCacheFormatVersion}|{provider}|{source}|metadata:{NormalizeForCache(track.Title)}|{NormalizeForCache(track.Artist)}|{NormalizeForCache(track.Album)}|{NormalizeDurationForCache(track.Duration)}";
    }

    private static bool HasStableCacheIdentity(TrackInfo track)
    {
        return !string.IsNullOrWhiteSpace(track.SongId);
    }

    private static string NormalizeForCache(string s)
    {
        var n = ChineseScriptConverter.ToSimplified(s).ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (var ch in n) if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    private static int NormalizeDurationForCache(TimeSpan duration)
    {
        return duration > TimeSpan.Zero
            ? (int)Math.Round(duration.TotalSeconds / 2, MidpointRounding.AwayFromZero) * 2
            : 0;
    }

    protected static string NormalizeForSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        // 1. 转繁为简并小写
        var normalized = ChineseScriptConverter.ToSimplified(value).ToLowerInvariant();

        // 2. 移除音标 (á -> a)
        normalized = RemoveDiacritics(normalized);

        // 3. 移除常见平台噪声标签
        var noNoise = Regex.Replace(normalized, @"\s*[\(\[（【](explicit|deluxe|digital|premium|album|edit|version|special|anniversary|studio)[\)\]）】]\s*", " ", RegexOptions.IgnoreCase);

        // 4. 分离歌手后缀 (feat. ft. with)
        var noFeatures = FeatureSuffixRegex.Replace(noNoise, string.Empty);

        // 5. 移除非字母数字字符，但保留空格以便分词
        var sb = new StringBuilder();
        foreach (var ch in noFeatures)
        {
            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)) sb.Append(ch);
            else sb.Append(' ');
        }

        // 6. 合并多余空格并 Trim
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static string RemoveDiacritics(string text)
    {
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }

    protected static int ScoreMatch(TrackInfo target, string resultTitle, string resultArtist, int? resultDurationInSeconds = null)
    {
        return LyricMatcher.Score(target, resultTitle, resultArtist, resultDurationInSeconds ?? 0);
    }

    private static double CalculateSimilarity(string s, string t) => LyricMatcher.NormalizeForSearch(s) == LyricMatcher.NormalizeForSearch(t) ? 1.0 : 0.0;

    private static bool HasUsableLines(LyricDocument? document)
    {
        return document?.Lines.Any(line => !string.IsNullOrWhiteSpace(line.Text)) == true;
    }

    public static void ClearCache()
    {
        CacheStore.Clear();
        DeleteLegacyCacheFiles();
    }

    private static void DeleteLegacyCacheFiles()
    {
        foreach (var legacyCacheFilePath in LegacyCacheFilePaths)
        {
            try
            {
                if (File.Exists(legacyCacheFilePath))
                {
                    File.Delete(legacyCacheFilePath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Failed to clear legacy lyric cache '{legacyCacheFilePath}': {exception.Message}");
            }
        }
    }

    private static readonly string[] LegacyCacheFilePaths =
    [
        GetCacheFilePath(8),
        GetCacheFilePath(9)
    ];

    private static string CacheFilePathStatic => GetCacheFilePath(CurrentCacheFormatVersion);

    private static string GetCacheFilePath(int cacheFormatVersion)
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskbarLyrics",
            "cache",
            $"unified-lyrics-v{cacheFormatVersion}.json");
    }
}
