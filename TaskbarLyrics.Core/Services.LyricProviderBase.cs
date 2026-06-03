using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Core.Services;

public abstract class LyricProviderBase : ILyricProvider
{
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

    // 缓存系统
    private static readonly ConcurrentDictionary<string, LyricDocument?> MemoryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object DiskCacheLock = new();
    private static Dictionary<string, LyricDocument>? _diskCache;

    protected HttpClient Http { get; }

    static LyricProviderBase()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    protected LyricProviderBase(HttpClient httpClient) => Http = httpClient;

    public abstract string SourceApp { get; }

    public async Task<LyricDocument?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(track);
        if (MemoryCache.TryGetValue(cacheKey, out var cachedDoc)) return cachedDoc;

        lock (DiskCacheLock)
        {
            EnsureDiskCacheLoaded();
            if (_diskCache!.TryGetValue(cacheKey, out var diskDoc))
            {
                MemoryCache[cacheKey] = diskDoc;
                return diskDoc;
            }
        }

        var result = await ResolveRemoteAsync(track, cancellationToken);
        if (result != null)
        {
            result = ProcessDocument(result);
            MemoryCache[cacheKey] = result;
            lock (DiskCacheLock) { EnsureDiskCacheLoaded(); _diskCache![cacheKey] = result; SaveDiskCache(); }
        }
        return result;
    }

    protected abstract Task<LyricDocument?> ResolveRemoteAsync(TrackInfo track, CancellationToken cancellationToken);

    // ========================================================
    // ✅ 第一核心：ProcessDocument (后处理)
    // ========================================================
    private LyricDocument ProcessDocument(LyricDocument doc)
    {
        var lines = doc.Lines.Select(l => l with 
        { 
            Text = BetterLyrics_Sanitize(l.Text),
            Translation = l.Translation != null ? BetterLyrics_Sanitize(l.Translation) : null
        })
        .Where(l => !string.IsNullOrWhiteSpace(l.Text) && l.Text != "//")
        .ToList();

        return new LyricDocument(EnsureSyllables(lines), doc.BestScore);
    }

    // ========================================================
    // ✅ 第二核心：BetterLyrics 风格净化逻辑
    // ========================================================
    private string BetterLyrics_Sanitize(string input)
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

    private bool ContainsAnyMeaningfulChar(string s)
    {
        return s.Any(c => char.IsLetterOrDigit(c) || (int)c > 0x4E00);
    }

    // ========================================================
    // ✅ 第三核心：ParseLrc (逐行精准提取)
    // ========================================================
    protected List<LyricLine> ParseLrc(string? lrc)
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
                int min = int.Parse(match.Groups[1].Value);
                int sec = int.Parse(match.Groups[2].Value);
                int ms = ParseMillisecond(match.Groups[3].Value);
                var timestamp = new TimeSpan(0, 0, min, sec, ms).Add(TimeSpan.FromMilliseconds(offsetMs));
                resultList.Add(new LyricLine(ClampTimestamp(timestamp), cleanedContent));
            }
        }

        return AlignBilingualLyrics(resultList);
    }

    private List<LyricLine> AlignBilingualLyrics(List<LyricLine> rawLines)
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

    private List<LyricLine> EnsureSyllables(List<LyricLine> lines)
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

    protected string DecodeBytesToString(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) {
            try { return Encoding.GetEncoding(936).GetString(bytes); }
            catch { return Encoding.UTF8.GetString(bytes); }
        }
    }

    private int ParseMillisecond(string fractionRaw) { 
        if (string.IsNullOrWhiteSpace(fractionRaw)) return 0; 
        return fractionRaw.Length switch { 1 => int.Parse(fractionRaw) * 100, 2 => int.Parse(fractionRaw) * 10, _ => int.Parse(fractionRaw[..3]) }; 
    }

    private static TimeSpan ClampTimestamp(TimeSpan timestamp)
    {
        return timestamp < TimeSpan.Zero ? TimeSpan.Zero : timestamp;
    }

    // ========================================================
    // ✅ 匹配与得分逻辑 (此前被误删)
    // ========================================================
    protected string BuildCacheKey(TrackInfo track)
    {
        return $"{SourceApp}|{NormalizeForCache(track.Title)}|{NormalizeForCache(track.Artist)}|{NormalizeDurationForCache(track.Duration)}";
    }

    private string NormalizeForCache(string s) 
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

    protected string NormalizeForSearch(string? value)
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

    protected int ScoreMatch(TrackInfo target, string resultTitle, string resultArtist, int? resultDurationInSeconds = null)
    {
        return LyricMatcher.Score(target, resultTitle, resultArtist, resultDurationInSeconds ?? 0);
    }

    private static double CalculateSimilarity(string s, string t) => LyricMatcher.NormalizeForSearch(s) == LyricMatcher.NormalizeForSearch(t) ? 1.0 : 0.0;

    private void EnsureDiskCacheLoaded() 
    { 
        if (_diskCache != null) return; 
        try { 
            if (!File.Exists(CacheFilePathStatic)) { _diskCache = new Dictionary<string, LyricDocument>(StringComparer.OrdinalIgnoreCase); return; } 
            _diskCache = JsonSerializer.Deserialize<Dictionary<string, LyricDocument>>(File.ReadAllText(CacheFilePathStatic)) ?? new(); 
        } catch { _diskCache = new(); } 
    }

    private void SaveDiskCache() 
    { 
        try { 
            var dir = Path.GetDirectoryName(CacheFilePathStatic); 
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir); 
            File.WriteAllText(CacheFilePathStatic, JsonSerializer.Serialize(_diskCache)); 
        } catch { } 
    }

    public static void ClearCache()
    {
        lock (DiskCacheLock)
        {
            _diskCache = new Dictionary<string, LyricDocument>(StringComparer.OrdinalIgnoreCase);
            MemoryCache.Clear();
            try
            {
                if (File.Exists(CacheFilePathStatic))
                {
                    File.Delete(CacheFilePathStatic);
                }
            }
            catch
            {
                // Ignore delete errors
            }
        }
    }

    private static string CacheFilePathStatic => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarLyrics", "cache", "unified-lyrics-v5.json");
}
