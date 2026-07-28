using System.Text.RegularExpressions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Utilities;

public static class LyricLineNormalizer
{
    private static readonly TimeSpan MaxSpeakerLabelLead = TimeSpan.FromMilliseconds(1500);
    private static readonly Regex SingerSlotLabelRegex = new(
        @"^(?:歌手\s*)?[A-Za-z0-9Ａ-Ｚａ-ｚ０-９一二三四五六七八九十甲乙丙丁]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] CommonSpeakerLabels =
    [
        "男", "女", "合", "齐", "男声", "女声", "合唱", "齐唱", "独白", "旁白",
        "主唱", "和声", "说唱", "rap", "rapper", "vocal", "vocals", "singer"
    ];

    private static readonly string[] SectionLabels =
    [
        "intro", "outro", "verse", "chorus", "pre-chorus", "prechorus", "bridge",
        "hook", "refrain", "interlude"
    ];

    public static List<LyricLine> MergeStandaloneSpeakerLabels(IEnumerable<LyricLine> lines)
    {
        var sorted = lines
            .Select((line, index) => new { Line = line, Index = index })
            .OrderBy(x => x.Line.Timestamp)
            .ThenBy(x => x.Index)
            .Select(x => x.Line)
            .ToList();

        if (sorted.Count < 2)
        {
            return sorted;
        }

        var merged = new List<LyricLine>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
        {
            var current = sorted[i];
            if (!TryGetStandaloneSpeakerLabel(current.Text, out var label) ||
                !TryFindMergeTarget(sorted, i, out var targetIndex))
            {
                merged.Add(current);
                continue;
            }

            var target = sorted[targetIndex];
            sorted[targetIndex] = target with
            {
                Text = label + target.Text.TrimStart(),
                Syllables = null
            };
        }

        return merged;
    }

    private static bool TryFindMergeTarget(List<LyricLine> lines, int speakerIndex, out int targetIndex)
    {
        var speaker = lines[speakerIndex];
        for (var i = speakerIndex + 1; i < lines.Count; i++)
        {
            var candidate = lines[i];
            var lead = candidate.Timestamp - speaker.Timestamp;
            if (lead < TimeSpan.Zero || lead > MaxSpeakerLabelLead)
            {
                break;
            }

            if (TryGetStandaloneSpeakerLabel(candidate.Text, out _))
            {
                continue;
            }

            targetIndex = i;
            return true;
        }

        targetIndex = -1;
        return false;
    }

    private static bool TryGetStandaloneSpeakerLabel(string? text, out string label)
    {
        label = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (!trimmed.EndsWith(':') && !trimmed.EndsWith('：'))
        {
            return false;
        }

        var name = trimmed[..^1]
            .Trim()
            .Trim('[', ']', '(', ')', '（', '）', '【', '】', '「', '」');
        if (name.Length is < 1 or > 16)
        {
            return false;
        }

        if (IsSectionLabel(name))
        {
            return false;
        }

        if (CommonSpeakerLabels.Any(x => string.Equals(name, x, StringComparison.OrdinalIgnoreCase)) ||
            (name.Length <= 8 && ContainsAny(name, "男", "女", "合", "唱")) ||
            SingerSlotLabelRegex.IsMatch(name))
        {
            label = name + "：";
            return true;
        }

        return false;
    }

    private static bool IsSectionLabel(string value)
    {
        return SectionLabels.Any(x => string.Equals(value, x, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsAny(string value, params string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            if (value.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
