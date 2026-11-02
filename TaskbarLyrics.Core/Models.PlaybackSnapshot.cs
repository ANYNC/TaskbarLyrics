namespace TaskbarLyrics.Core.Models;

public sealed record PlaybackSnapshot(
    bool IsPlaying,
    TimeSpan Position,
    TrackInfo? Track,
    byte[]? CoverImageBytes = null,
    TimeSpan? RawPosition = null,
    TimeSpan? ExtrapolatedPosition = null,
    bool IsCoverLoading = false,
    string SourceAppUserModelId = "",
    bool CanSeek = false)
{
    public bool HasKnownPosition { get; init; } = true;
    public MediaControlCapabilities? ControlCapabilities { get; init; }
}

public sealed record MediaControlCapabilities(bool PlayPause, bool Previous, bool Next);
