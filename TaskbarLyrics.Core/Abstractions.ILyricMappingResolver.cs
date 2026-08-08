using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Abstractions;

public interface ILyricMappingResolver
{
    LyricMapping Resolve(TrackInfo track);
}

public sealed record LyricMapping(
    string Title,
    string Artist,
    string? PreferredProvider,
    bool IsPureMusic,
    string? Album = null)
{
    public static LyricMapping Unchanged(TrackInfo track) =>
        new(track.Title, track.Artist, null, false, track.Album);
}
