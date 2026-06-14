namespace TaskbarLyrics.Core.Models;

public sealed class LyricDocument
{
    public LyricDocument(IEnumerable<LyricLine> lines, int bestScore = 0, bool isPureMusic = false)
    {
        Lines = lines.OrderBy(x => x.Timestamp).ToArray();
        BestScore = bestScore;
        IsPureMusic = isPureMusic || LooksLikePureMusic(Lines);
    }

    public IReadOnlyList<LyricLine> Lines { get; }
    public int BestScore { get; }
    public bool IsPureMusic { get; }

    private static bool LooksLikePureMusic(IReadOnlyList<LyricLine> lines)
    {
        var contentLines = lines
            .Select(line => NormalizeText(line.Text))
            .Where(text => text.Length > 0 && !IsInformationalLine(text))
            .ToList();

        return contentLines.Count == 1 &&
               contentLines[0].Contains("纯音乐", StringComparison.Ordinal);
    }

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text
            .Trim()
            .Trim('[', ']', '【', '】', '(', ')', '（', '）', ' ', '\t');
    }

    private static bool IsInformationalLine(string text)
    {
        if (text.Contains("纯音乐", StringComparison.Ordinal))
        {
            return false;
        }

        return ContainsAny(text,
            "获取", "来源", "提供", "贡献", "上传", "制作", "校对", "翻译",
            "作词", "作曲", "编曲", "歌词", "歌詞",
            "lyric", "lyrics", "provided", "generated", "synced", "composer");
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
