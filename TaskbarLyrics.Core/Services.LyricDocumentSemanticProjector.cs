using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Services;

internal static class LyricDocumentSemanticProjector
{
    public static ParsedLyrics ToParsedLyrics(LyricDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var lines = document.Lines
            .Select((line, index) => new ParsedLyricLine(
                line.Timestamp,
                index + 1 < document.Lines.Count ? document.Lines[index + 1].Timestamp : null,
                line.Text,
                line.Translation))
            .ToArray();
        return new ParsedLyrics(
            lines,
            lines.Length == 0 ? LyricTimingKind.Unsynced : LyricTimingKind.LineTimed,
            LyricTimingProvenance.Unknown,
            LyricPayloadFormat.Lrc,
            document.IsPureMusic);
    }
}
