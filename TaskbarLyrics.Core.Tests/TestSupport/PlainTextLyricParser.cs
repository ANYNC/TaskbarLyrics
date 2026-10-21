using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Tests;

internal sealed class PlainTextLyricParser : ILyricPayloadParser
{
    public bool CanParse(LyricPayloadFormat format) => format == LyricPayloadFormat.PlainText;

    public Task<ParsedLyrics> ParseAsync(
        DecodedLyricPayload payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ParsedLyrics(
            [new ParsedLyricLine(TimeSpan.Zero, null, payload.OriginalLyrics ?? "")],
            LyricTimingKind.LineTimed,
            LyricTimingProvenance.ProviderSupplied,
            payload.Format));
    }
}
