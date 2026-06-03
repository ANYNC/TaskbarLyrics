using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using F23.StringSimilarity;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Utilities;

public static class LyricMatcher
{
    private static readonly Regex BracketSuffixRegex = new(@"\s*[\(\[\{（【].*?[\)\]\}）】]\s*", RegexOptions.Compiled);
    private static readonly Regex FeatureSuffixRegex = new(@"\s+(feat\.?|ft\.?|with)\s+.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string[] ConflictKeywords = { "live", "remix", "acoustic", "demo", "instrumental", "vma", "award", "现场", "演唱会", "颁奖", "典礼" };

    // JaroWinkler 算法，适合短文本匹配
    private static readonly JaroWinkler JaroWinklerAlgo = new();

    public static int Score(TrackInfo target, string resultTitle, string resultArtist, int resultDurationInSeconds = 0)
    {
        if (IsUnknownTitle(target.Title) || IsUnknownTitle(resultTitle)) return 0;

        // 2. 版本冲突检测 (Conflict Detection)
        foreach (var keyword in ConflictKeywords)
        {
            if (HasVersionConflict(target.Title, resultTitle, keyword)) return 0;
        }

        var normalizedTargetTitle = NormalizeForSearch(target.Title);
        var normalizedResultTitle = NormalizeForSearch(resultTitle);
        var normalizedTargetArtist = NormalizeForSearch(target.Artist);
        var normalizedResultArtist = NormalizeForSearch(resultArtist);

        double titleSim = GetStringSimilarity(normalizedTargetTitle, normalizedResultTitle);
        double artistSim = GetStringSimilarity(normalizedTargetArtist, normalizedResultArtist);
        double durationSim = GetDurationSimilarity(target.Duration.TotalSeconds, resultDurationInSeconds);

        if (!IsTitleMatchAcceptable(normalizedTargetTitle, normalizedResultTitle, titleSim))
        {
            Log.Debug($"LyricMatcher rejected title mismatch: '{target.Title}' vs '{resultTitle}' ({titleSim:F2})");
            return 0;
        }

        bool hasTargetArtist = HasUsefulArtist(normalizedTargetArtist);
        bool hasResultArtist = HasUsefulArtist(normalizedResultArtist);
        if (hasTargetArtist &&
            hasResultArtist &&
            artistSim < 0.45 &&
            !HasArtistOverlap(normalizedTargetArtist, normalizedResultArtist))
        {
            Log.Debug($"LyricMatcher rejected artist mismatch: '{target.Artist}' vs '{resultArtist}' ({artistSim:F2})");
            return 0;
        }

        bool hasDuration = target.Duration.TotalSeconds > 0 && resultDurationInSeconds > 0;
        if (hasDuration && Math.Abs(target.Duration.TotalSeconds - resultDurationInSeconds) >= 20)
        {
            Log.Debug($"LyricMatcher rejected duration mismatch: {target.Duration.TotalSeconds:F0}s vs {resultDurationInSeconds}s");
            return 0;
        }

        double totalScore;
        if (hasTargetArtist && hasResultArtist && hasDuration)
        {
            totalScore = (titleSim * 0.50) + (artistSim * 0.30) + (durationSim * 0.20);
        }
        else if (hasTargetArtist && hasResultArtist)
        {
            totalScore = (titleSim * 0.60) + (artistSim * 0.40);
        }
        else if (hasDuration)
        {
            totalScore = (titleSim * 0.75) + (durationSim * 0.25);
        }
        else
        {
            totalScore = titleSim;
        }

        Log.Debug($"LyricMatcher: TitleSim={titleSim:F2}, ArtistSim={artistSim:F2}, DurationSim={durationSim:F2} -> BaseScore={(int)Math.Round(totalScore * 100)}");
        return (int)Math.Round(totalScore * 100);
    }

    private static double GetStringSimilarity(string? s1, string? s2)
    {
        s1 = NormalizeForSearch(s1);
        s2 = NormalizeForSearch(s2);

        if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return 1.0;
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;

        return JaroWinklerAlgo.Similarity(s1, s2);
    }

    private static double GetDurationSimilarity(double localSeconds, double? remoteSeconds)
    {
        if (remoteSeconds == null || remoteSeconds == 0 || localSeconds <= 0) return 0.0;

        double diff = Math.Abs(localSeconds - remoteSeconds.Value);

        // 差距 <= 1 秒：100% 相似
        // 差距 >= 10 秒：0% 相似
        // 中间线性插值
        const double PerfectTolerance = 1.0;
        const double MaxTolerance = 10.0;

        if (diff <= PerfectTolerance) return 1.0;
        if (diff >= MaxTolerance) return 0.0;

        return 1.0 - ((diff - PerfectTolerance) / (MaxTolerance - PerfectTolerance));
    }

    private static bool IsTitleMatchAcceptable(string targetTitle, string resultTitle, double similarity)
    {
        if (string.IsNullOrWhiteSpace(targetTitle) || string.IsNullOrWhiteSpace(resultTitle)) return false;
        if (similarity >= 0.72) return true;

        return targetTitle.Length >= 3 &&
               resultTitle.Length >= 3 &&
               (targetTitle.Contains(resultTitle, StringComparison.Ordinal) ||
                resultTitle.Contains(targetTitle, StringComparison.Ordinal));
    }

    private static bool HasUsefulArtist(string normalizedArtist)
    {
        return !string.IsNullOrWhiteSpace(normalizedArtist) &&
               !string.Equals(normalizedArtist, "unknown artist", StringComparison.Ordinal);
    }

    private static bool IsUnknownTitle(string? title)
    {
        return string.IsNullOrWhiteSpace(title) ||
               string.Equals(title.Trim(), "Unknown Title", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasArtistOverlap(string targetArtist, string resultArtist)
    {
        return targetArtist.Contains(resultArtist, StringComparison.Ordinal) ||
               resultArtist.Contains(targetArtist, StringComparison.Ordinal);
    }

    public static string NormalizeForSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        
        var normalized = ChineseScriptConverter.ToSimplified(value).ToLowerInvariant();
        normalized = RemoveDiacritics(normalized);

        // 移除常见平台噪声标签
        var noNoise = Regex.Replace(normalized, @"\s*[\(\[（【](explicit|deluxe|digital|premium|album|edit|version|special|anniversary|studio|remastered)[\)\]）】]\s*", " ", RegexOptions.IgnoreCase);
        
        // 移除所有括号内容进行纯基准对比（相似度算法对噪声敏感）
        var pureTitle = BracketSuffixRegex.Replace(noNoise, " ");
        
        // 移除歌手后缀
        var noFeatures = FeatureSuffixRegex.Replace(pureTitle, string.Empty);
        
        var sb = new StringBuilder();
        foreach (var ch in noFeatures)
        {
            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)) sb.Append(ch);
            else sb.Append(' ');
        }
        
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static string RemoveDiacritics(string text)
    {
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();
        foreach (var c in normalizedString)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                stringBuilder.Append(c);
        }
        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool HasVersionConflict(string target, string result, string keyword)
    {
        bool targetHas = target.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        bool resultHas = result.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        return targetHas != resultHas;
    }
}
