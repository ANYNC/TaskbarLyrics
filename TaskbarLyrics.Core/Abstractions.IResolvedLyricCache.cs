using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Abstractions;

/// <summary>
/// Stores the final lyric resolution for a normalized title-and-artist identity.
/// </summary>
public interface IResolvedLyricCache
{
    bool TryGet(TrackInfo track, out ResolvedLyrics? resolvedLyrics);

    bool Store(TrackInfo track, ResolvedLyrics resolvedLyrics);

    void Clear();
}
