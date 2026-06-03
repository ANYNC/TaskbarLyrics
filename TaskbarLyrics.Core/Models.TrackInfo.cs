namespace TaskbarLyrics.Core.Models;

public sealed record TrackInfo(
    string Id,
    string Title,
    string Artist,
    string SourceApp,
    TimeSpan Duration,
    string? SongId = null);
