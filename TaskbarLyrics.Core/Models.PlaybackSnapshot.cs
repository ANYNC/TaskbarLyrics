namespace TaskbarLyrics.Core.Models;

public sealed record PlaybackSnapshot(
    bool IsPlaying,
    TimeSpan Position,
    TrackInfo? Track,
    byte[]? CoverImageBytes = null,
    TimeSpan? RawPosition = null,
    TimeSpan? ExtrapolatedPosition = null);
