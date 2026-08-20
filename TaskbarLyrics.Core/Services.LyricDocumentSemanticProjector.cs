using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Services;

internal static class LyricDocumentSemanticProjector
{
    public static ParsedLyrics ToParsedLyrics(LyricDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var lines = document.Lines
            .Select((line, index) => ToParsedLyricLine(
                line,
                index + 1 < document.Lines.Count ? document.Lines[index + 1].Timestamp : null))
            .ToArray();

        // LyricDocument retains syllables but not whether a local parser supplied or synthesized them.
        var timingKind = lines.Length == 0
            ? LyricTimingKind.Unsynced
            : lines.Any(line => line.Segments.Count > 0)
                ? LyricTimingKind.Mixed
                : LyricTimingKind.LineTimed;
        return new ParsedLyrics(
            lines,
            timingKind,
            LyricTimingProvenance.Unknown,
            LyricPayloadFormat.Lrc,
            document.IsPureMusic);
    }

    private static ParsedLyricLine ToParsedLyricLine(LyricLine line, TimeSpan? endTime)
    {
        return new ParsedLyricLine(
            line.Timestamp,
            endTime,
            line.Text,
            line.Translation,
            ProjectSyllables(line, endTime));
    }

    private static List<ParsedLyricSegment> ProjectSyllables(LyricLine line, TimeSpan? lineEnd)
    {
        if (line.Syllables is not { Count: > 0 })
        {
            return [];
        }

        var segments = new List<ParsedLyricSegment>();
        foreach (var syllable in line.Syllables)
        {
            var segment = TryProjectSyllable(line.Timestamp, lineEnd, syllable);
            if (segment is null)
            {
                return [];
            }

            segments.Add(segment);
        }

        return segments;
    }

    private static ParsedLyricSegment? TryProjectSyllable(
        TimeSpan lineStart,
        TimeSpan? lineEnd,
        LyricSyllable syllable)
    {
        if (syllable.RelativeOffset < TimeSpan.Zero ||
            syllable.Duration <= TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(syllable.Text))
        {
            return null;
        }

        TimeSpan startTime;
        TimeSpan endTime;
        try
        {
            startTime = lineStart.Add(syllable.RelativeOffset);
            endTime = startTime.Add(syllable.Duration);
        }
        catch (OverflowException)
        {
            return null;
        }

        if (startTime < lineStart ||
            startTime < TimeSpan.Zero ||
            endTime <= startTime ||
            (lineEnd is { } value && endTime > value))
        {
            return null;
        }

        return new ParsedLyricSegment(startTime, endTime, syllable.Text);
    }
}
