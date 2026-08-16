using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.App;

internal enum PlaybackInputKind
{
    NoPlayback,
    ValidTrack
}

internal static class PlaybackInputPolicy
{
    public static PlaybackInputKind Classify(PlaybackSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Classify(snapshot.Track);
    }

    public static PlaybackInputKind Classify(TrackInfo? track)
    {
        return IsValidTrack(track)
            ? PlaybackInputKind.ValidTrack
            : PlaybackInputKind.NoPlayback;
    }

    public static bool IsValidTrack(TrackInfo? track)
    {
        if (track is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(track.Title) ||
            string.Equals(track.Title.Trim(), "Unknown Title", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !track.Id.Trim().EndsWith("|ProcessFallback", StringComparison.OrdinalIgnoreCase);
    }
}

internal readonly record struct LyricsContentVisibilityTransition(
    bool IsVisible,
    int? CountdownSecondsRemaining,
    bool PresentationChanged);

internal sealed class LyricsContentVisibilityStateMachine
{
    public static readonly TimeSpan CountdownDuration = TimeSpan.FromSeconds(3);

    private bool _autoHideWhenNoPlayback = true;
    private bool _isVisible = true;
    private bool _hasReceivedPlaybackSnapshot;
    private bool? _lastPlaybackInputWasValid;
    private bool _isCountdownActive;
    private DateTimeOffset _countdownDeadlineUtc;
    private int? _countdownSecondsRemaining;

    public bool IsVisible => _isVisible;

    public bool HasReceivedPlaybackSnapshot => _hasReceivedPlaybackSnapshot;

    public bool IsConfirmedNoPlayback =>
        _hasReceivedPlaybackSnapshot && _lastPlaybackInputWasValid == false;

    public LyricsContentVisibilityTransition ApplySettings(
        bool autoHideWhenNoPlayback,
        DateTimeOffset nowUtc)
    {
        var previous = CreatePresentationKey();
        var wasAutoHideEnabled = _autoHideWhenNoPlayback;
        _autoHideWhenNoPlayback = autoHideWhenNoPlayback;

        if (!autoHideWhenNoPlayback)
        {
            RestoreVisibleState();
        }
        else if (_hasReceivedPlaybackSnapshot && _lastPlaybackInputWasValid == false)
        {
            if (!wasAutoHideEnabled)
            {
                RestoreVisibleState();
                StartCountdown(nowUtc);
            }
            else if (_isVisible && !_isCountdownActive)
            {
                StartCountdown(nowUtc);
            }
        }

        return CreateTransition(previous);
    }

    public LyricsContentVisibilityTransition ObservePlaybackInput(
        PlaybackInputKind inputKind,
        DateTimeOffset nowUtc)
    {
        var previous = CreatePresentationKey();
        var isFirstPlaybackSnapshot = !_hasReceivedPlaybackSnapshot;
        _hasReceivedPlaybackSnapshot = true;
        _lastPlaybackInputWasValid = inputKind == PlaybackInputKind.ValidTrack;

        if (inputKind == PlaybackInputKind.ValidTrack)
        {
            RestoreVisibleState();
        }
        else if (!_autoHideWhenNoPlayback)
        {
            RestoreVisibleState();
        }
        else if (_isVisible)
        {
            if (!_isCountdownActive)
            {
                StartCountdown(nowUtc);
            }

            var remaining = GetRemainingSeconds(nowUtc);
            if (remaining <= 0)
            {
                _isVisible = false;
                _isCountdownActive = false;
                _countdownSecondsRemaining = null;
            }
            else
            {
                _countdownSecondsRemaining = remaining;
            }
        }

        return CreateTransition(previous, isFirstPlaybackSnapshot);
    }

    private void StartCountdown(DateTimeOffset nowUtc)
    {
        _countdownDeadlineUtc = nowUtc + CountdownDuration;
        _isCountdownActive = true;
        _countdownSecondsRemaining = GetRemainingSeconds(nowUtc);
    }

    private int GetRemainingSeconds(DateTimeOffset nowUtc)
    {
        var remaining = _countdownDeadlineUtc - nowUtc;
        if (remaining <= TimeSpan.Zero)
        {
            return 0;
        }

        return Math.Clamp((int)Math.Ceiling(remaining.TotalSeconds), 1, (int)CountdownDuration.TotalSeconds);
    }

    private void RestoreVisibleState()
    {
        _isVisible = true;
        _isCountdownActive = false;
        _countdownSecondsRemaining = null;
    }

    private (bool IsVisible, int? CountdownSecondsRemaining) CreatePresentationKey() =>
        (_isVisible, _countdownSecondsRemaining);

    private LyricsContentVisibilityTransition CreateTransition(
        (bool IsVisible, int? CountdownSecondsRemaining) previous,
        bool forcePresentationChange = false)
    {
        var presentationChanged =
            forcePresentationChange ||
            previous.IsVisible != _isVisible ||
            previous.CountdownSecondsRemaining != _countdownSecondsRemaining;
        return new LyricsContentVisibilityTransition(
            _isVisible,
            _countdownSecondsRemaining,
            presentationChanged);
    }
}

