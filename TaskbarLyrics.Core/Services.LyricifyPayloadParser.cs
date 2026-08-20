using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Parsers;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Services;

public sealed class LyricifyPayloadParser : ILyricPayloadParser
{
    public const string InformationLineStartTimesDiagnostic = "informationLineStartTimesMs";

    private static readonly TimeSpan TranslationTolerance = TimeSpan.FromMilliseconds(60);

    public bool CanParse(LyricPayloadFormat format) => format is
        LyricPayloadFormat.Lrc or
        LyricPayloadFormat.Qrc or
        LyricPayloadFormat.Krc or
        LyricPayloadFormat.Yrc or
        LyricPayloadFormat.Ttml or
        LyricPayloadFormat.PlainText;

    public Task<ParsedLyrics> ParseAsync(
        DecodedLyricPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        cancellationToken.ThrowIfCancellationRequested();

        if (payload.IsPureMusic && string.IsNullOrWhiteSpace(payload.OriginalLyrics))
        {
            return Task.FromResult(new ParsedLyrics(
                [new ParsedLyricLine(TimeSpan.Zero, null, "🎶🎶🎶")],
                LyricTimingKind.Unsynced,
                LyricTimingProvenance.Unknown,
                payload.Format,
                isPureMusic: true));
        }

        if (string.IsNullOrWhiteSpace(payload.OriginalLyrics))
        {
            throw new FormatException("Lyric payload is empty.");
        }

        if (payload.Format == LyricPayloadFormat.PlainText)
        {
            return Task.FromResult(ParsePlainText(payload));
        }

        var lines = payload.Format == LyricPayloadFormat.Lrc
            ? ParseLrcLines(payload.OriginalLyrics)
            : ConvertLines(ParseExplicit(payload.Format, payload.OriginalLyrics).Lines);
        if (!string.IsNullOrWhiteSpace(payload.TranslationLyrics))
        {
            lines = ApplyLrcTranslations(lines, payload.TranslationLyrics);
        }

        lines = ApplyInformationLineMarkers(lines, payload.Diagnostics);

        if (lines.Count == 0)
        {
            throw new FormatException($"The {payload.Format} parser returned no usable lyric lines.");
        }

        var timingKind = DetermineTimingKind(lines);
        return Task.FromResult(new ParsedLyrics(
            lines,
            timingKind,
            LyricTimingProvenance.ProviderSupplied,
            payload.Format,
            payload.IsPureMusic));
    }

    private static LyricsData ParseExplicit(LyricPayloadFormat format, string content)
    {
        return format switch
        {
            LyricPayloadFormat.Qrc => QrcParser.Parse(content),
            LyricPayloadFormat.Krc => KrcParser.Parse(content),
            LyricPayloadFormat.Yrc => YrcParser.Parse(content),
            LyricPayloadFormat.Ttml => TtmlParser.Parse(content),
            _ => throw new NotSupportedException($"No explicit parser is registered for {format}.")
        };
    }

    private static ParsedLyrics ParsePlainText(DecodedLyricPayload payload)
    {
        var lines = payload.OriginalLyrics!
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => new ParsedLyricLine(TimeSpan.Zero, null, text))
            .ToArray();
        if (lines.Length == 0)
        {
            throw new FormatException("Plain lyric payload contains no usable text.");
        }

        return new ParsedLyrics(
            lines,
            LyricTimingKind.Unsynced,
            LyricTimingProvenance.Unknown,
            LyricPayloadFormat.PlainText,
            payload.IsPureMusic);
    }

    private static List<ParsedLyricLine> ConvertLines(IEnumerable<ILineInfo>? sourceLines)
    {
        var lines = new List<ParsedLyricLine>();
        foreach (var sourceLine in sourceLines ?? [])
        {
            if (string.IsNullOrWhiteSpace(sourceLine.Text))
            {
                continue;
            }

            var startMs = Math.Max(0, sourceLine.StartTime ?? 0);
            var sourceEndMs = sourceLine.EndTime;
            var segments = ConvertSegments(sourceLine, startMs, sourceEndMs);
            var endMs = sourceEndMs is > 0
                ? Math.Max(startMs, sourceEndMs.Value)
                : segments.Length == 0
                    ? (int?)null
                    : segments.Max(segment => (int)segment.EndTime.TotalMilliseconds);
            var translation = sourceLine is IFullLineInfo fullLine
                ? ResolveChineseTranslation(fullLine)
                : null;

            lines.Add(new ParsedLyricLine(
                TimeSpan.FromMilliseconds(startMs),
                endMs is null ? null : TimeSpan.FromMilliseconds(endMs.Value),
                NormalizeLineText(sourceLine.Text),
                translation,
                segments));
        }

        return lines
            .OrderBy(line => line.StartTime)
            .ToList();
    }

    private static ParsedLyricSegment[] ConvertSegments(
        ILineInfo sourceLine,
        int lineStartMs,
        int? lineEndMs)
    {
        if (sourceLine is not SyllableLineInfo { Syllables.Count: > 0 } syllableLine)
        {
            return [];
        }

        return syllableLine.Syllables
            .Where(syllable =>
                !string.IsNullOrEmpty(syllable.Text) &&
                syllable.StartTime >= lineStartMs &&
                syllable.EndTime > syllable.StartTime &&
                (lineEndMs is null || syllable.EndTime <= lineEndMs.Value))
            .Select(syllable => new ParsedLyricSegment(
                TimeSpan.FromMilliseconds(syllable.StartTime),
                TimeSpan.FromMilliseconds(syllable.EndTime),
                syllable.Text))
            .OrderBy(segment => segment.StartTime)
            .ToArray();
    }

    private static string? ResolveChineseTranslation(IFullLineInfo line)
    {
        if (!string.IsNullOrWhiteSpace(line.ChineseTranslation))
        {
            return line.ChineseTranslation;
        }

        return line.Translations.TryGetValue("zh", out var translation) &&
               !string.IsNullOrWhiteSpace(translation)
            ? translation
            : null;
    }

    private static List<ParsedLyricLine> ApplyLrcTranslations(
        IReadOnlyList<ParsedLyricLine> originalLines,
        string translationContent)
    {
        var translationLines = ParseLrcLines(translationContent);
        return originalLines
            .Select(line =>
            {
                var translation = translationLines.FirstOrDefault(candidate =>
                    (candidate.StartTime - line.StartTime).Duration() <= TranslationTolerance);
                return translation is null
                    ? line
                    : new ParsedLyricLine(
                        line.StartTime,
                        line.EndTime,
                        line.Text,
                        translation.Text,
                        line.Segments,
                        line.IsInformationLine);
            })
            .ToList();
    }

    private static LyricTimingKind DetermineTimingKind(List<ParsedLyricLine> lines)
    {
        var segmentedLines = lines.Where(line => line.Segments.Count > 0).ToArray();
        if (segmentedLines.Length == 0)
        {
            return LyricTimingKind.LineTimed;
        }

        if (segmentedLines.Length != lines.Count)
        {
            return LyricTimingKind.Mixed;
        }

        var hasCharacterSegments = segmentedLines
            .SelectMany(line => line.Segments)
            .Any(segment => segment.Text.EnumerateRunes().Count() == 1);
        var hasWordSegments = segmentedLines
            .SelectMany(line => line.Segments)
            .Any(segment => segment.Text.EnumerateRunes().Count() > 1);
        return (hasCharacterSegments, hasWordSegments) switch
        {
            (true, true) => LyricTimingKind.Mixed,
            (true, false) => LyricTimingKind.CharacterTimed,
            _ => LyricTimingKind.WordTimed
        };
    }

    private static List<ParsedLyricLine> ApplyInformationLineMarkers(
        IReadOnlyList<ParsedLyricLine> lines,
        IReadOnlyDictionary<string, string> diagnostics)
    {
        if (!diagnostics.TryGetValue(InformationLineStartTimesDiagnostic, out var serializedStarts))
        {
            return lines.ToList();
        }

        var markedStarts = serializedStarts
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => long.TryParse(
                value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var milliseconds)
                ? milliseconds
                : (long?)null)
            .Where(milliseconds => milliseconds is >= 0)
            .Select(milliseconds => TimeSpan.FromMilliseconds(milliseconds!.Value))
            .ToHashSet();
        if (markedStarts.Count == 0)
        {
            return lines.ToList();
        }

        return lines
            .Select(line => markedStarts.Contains(line.StartTime)
                ? new ParsedLyricLine(
                    line.StartTime,
                    line.EndTime,
                    line.Text,
                    line.Translation,
                    line.Segments,
                    isInformationLine: true)
                : line)
            .ToList();
    }

    private static List<ParsedLyricLine> ParseLrcLines(string content)
    {
        var offsetMilliseconds = 0;
        var lyricLines = new List<string>();
        foreach (var sourceLine in content
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n'))
        {
            var trimmed = sourceLine.Trim();
            if (TryParseOffset(trimmed, out var parsedOffset))
            {
                offsetMilliseconds = parsedOffset;
                continue;
            }

            lyricLines.Add(sourceLine);
        }

        var parsed = LrcParser.Parse(string.Join('\n', lyricLines).AsSpan());
        var lines = ConvertLines(parsed.Lines);
        if (offsetMilliseconds == 0)
        {
            return lines;
        }

        return lines
            .Select(line => ShiftLine(line, offsetMilliseconds))
            .OrderBy(line => line.StartTime)
            .ToList();
    }

    private static bool TryParseOffset(string line, out int offsetMilliseconds)
    {
        const string Prefix = "[offset:";
        if (line.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && line.EndsWith(']'))
        {
            var value = line.AsSpan(Prefix.Length, line.Length - Prefix.Length - 1);
            if (int.TryParse(value, out offsetMilliseconds))
            {
                return true;
            }
        }

        offsetMilliseconds = 0;
        return false;
    }

    private static ParsedLyricLine ShiftLine(ParsedLyricLine line, int offsetMilliseconds)
    {
        var offset = TimeSpan.FromMilliseconds(offsetMilliseconds);
        var start = Max(TimeSpan.Zero, line.StartTime + offset);
        TimeSpan? end = line.EndTime is null
            ? null
            : Max(start, line.EndTime.Value + offset);
        var segments = line.Segments
            .Select(segment => new ParsedLyricSegment(
                Max(start, segment.StartTime + offset),
                Max(start + TimeSpan.FromTicks(1), segment.EndTime + offset),
                segment.Text))
            .ToArray();
        return new ParsedLyricLine(
            start,
            end,
            line.Text,
            line.Translation,
            segments,
            line.IsInformationLine);
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;

    private static string NormalizeLineText(string text)
    {
        return string.Join(' ', text.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
