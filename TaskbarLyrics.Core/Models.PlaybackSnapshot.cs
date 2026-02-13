namespace TaskbarLyrics.Core.Models;

public sealed record PlaybackSnapshot(
    bool IsPlaying,
    TimeSpan Position,
    TrackInfo? Track);
