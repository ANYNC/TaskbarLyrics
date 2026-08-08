using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Abstractions;

public interface ILyricSource
{
    LyricProviderId ProviderId { get; }

    Task<IReadOnlyList<SourceTrackCandidate>> SearchAsync(
        LyricSearchPlan plan,
        CancellationToken cancellationToken = default);

    Task<RawLyricPayload?> FetchAsync(
        SourceTrackCandidate candidate,
        CancellationToken cancellationToken = default);
}

public interface ILyricPayloadDecoder
{
    bool CanDecode(LyricPayloadFormat format);

    Task<DecodedLyricPayload> DecodeAsync(
        RawLyricPayload payload,
        CancellationToken cancellationToken = default);
}

public interface ILyricPayloadParser
{
    bool CanParse(LyricPayloadFormat format);

    Task<ParsedLyrics> ParseAsync(
        DecodedLyricPayload payload,
        CancellationToken cancellationToken = default);
}
