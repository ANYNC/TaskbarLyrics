using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Abstractions;

public interface ILyricProvider
{
    string SourceApp { get; }

    Task<LyricDocument?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default);
}
