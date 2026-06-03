using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Abstractions;

public interface ILyricProviderRegistry
{
    Task<List<LyricResolveResult>> ResolveLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default);
    Task<LyricDocument?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default);
}
