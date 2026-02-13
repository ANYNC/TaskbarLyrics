using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Abstractions;

public interface IMusicSessionProvider
{
    Task<PlaybackSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default);
}
