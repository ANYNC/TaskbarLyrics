using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Abstractions;

public interface ILyricResolutionCoordinator : IDisposable
{
    Task<ResolvedLyrics?> ResolveAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default);
}
