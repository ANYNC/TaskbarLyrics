using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Abstractions;

public interface ILyricResolutionCoordinator : IDisposable
{
    Task<ResolvedLyrics?> ResolveAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default);

    async Task<LyricResolutionResult> ResolveWithResultAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default) =>
        LyricResolutionResult.FromLyrics(await ResolveAsync(track, cancellationToken));
}
