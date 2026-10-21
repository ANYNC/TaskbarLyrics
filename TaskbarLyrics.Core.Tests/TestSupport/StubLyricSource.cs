using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Tests;

internal class StubLyricSource(LyricProviderId providerId) : ILyricSource
{
    public LyricProviderId ProviderId { get; } = providerId;

    public Func<LyricSearchPlan, CancellationToken, Task<IReadOnlyList<SourceTrackCandidate>>> SearchHandler { get; set; } =
        (_, _) => Task.FromResult<IReadOnlyList<SourceTrackCandidate>>([]);

    public Func<SourceTrackCandidate, CancellationToken, Task<RawLyricPayload?>> FetchHandler { get; set; } =
        (_, _) => Task.FromResult<RawLyricPayload?>(null);

    public virtual Task<IReadOnlyList<SourceTrackCandidate>> SearchAsync(
        LyricSearchPlan plan,
        CancellationToken cancellationToken = default) =>
        SearchHandler(plan, cancellationToken);

    public virtual Task<RawLyricPayload?> FetchAsync(
        SourceTrackCandidate candidate,
        CancellationToken cancellationToken = default) =>
        FetchHandler(candidate, cancellationToken);
}
