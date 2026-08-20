using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Services;

public static class ResolvedLyricsCompatibilityProjector
{
    public static LyricDocument ToLyricDocument(
        ResolvedLyrics resolved,
        bool includeInformationLines = true)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        var lines = resolved.Content.Lines
            .Where(line => includeInformationLines || !line.IsInformationLine)
            .Select(ToLyricLine)
            .ToArray();
        return new LyricDocument(lines, isPureMusic: resolved.Content.IsPureMusic);
    }

    private static LyricLine ToLyricLine(ParsedLyricLine line)
    {
        var syllables = line.Segments.Count == 0
            ? null
            : line.Segments
                .Select(segment => new LyricSyllable(
                    segment.StartTime - line.StartTime,
                    segment.EndTime - segment.StartTime,
                    segment.Text))
                .ToList();

        return new LyricLine(
            line.StartTime,
            line.Text,
            line.Translation,
            syllables);
    }
}
