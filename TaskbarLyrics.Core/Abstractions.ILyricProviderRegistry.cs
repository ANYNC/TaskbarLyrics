using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Abstractions;

public interface ILyricProviderRegistry
{
    Task<LyricDocument?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default);
}
