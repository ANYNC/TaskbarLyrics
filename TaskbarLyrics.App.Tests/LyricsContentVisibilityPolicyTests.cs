using System.Globalization;
using TaskbarLyrics.Core.Models;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class LyricsContentVisibilityPolicyTests
{
    [Fact]
    public void ClassifyTreatsNullAsNoPlayback()
    {
        Assert.Equal(PlaybackInputKind.NoPlayback, PlaybackInputPolicy.Classify((TrackInfo?)null));
    }

    [Theory]
    [InlineData("Netease|ProcessFallback", "Unknown Title")]
    [InlineData("QQMusic|ProcessFallback", "Song")]
    [InlineData("Netease|song", "Unknown Title")]
    public void ClassifyTreatsPlaceholderTracksAsNoPlayback(string id, string title)
    {
        var track = CreateTrack(id, title);

        Assert.Equal(PlaybackInputKind.NoPlayback, PlaybackInputPolicy.Classify(track));
    }

    [Fact]
    public void ClassifyTreatsInferredNeteaseFallbackAndPausedTrackAsValidInput()
    {
        var inferredTrack = CreateTrack("Netease|Song|Artist", "Song");
        var pausedSnapshot = new PlaybackSnapshot(false, TimeSpan.Zero, inferredTrack);

        Assert.Equal(PlaybackInputKind.ValidTrack, PlaybackInputPolicy.Classify(inferredTrack));
        Assert.Equal(PlaybackInputKind.ValidTrack, PlaybackInputPolicy.Classify(pausedSnapshot));
        Assert.True(PlaybackInputPolicy.IsValidTrack(inferredTrack));
    }

    [Fact]
    public void CountdownStartsAtThreeSecondsOnFirstConfirmedNoPlayback()
    {
        var now = DateTimeOffset.Parse("2026-08-16T00:00:00Z", CultureInfo.InvariantCulture);
        var state = new LyricsContentVisibilityStateMachine();

        var transition = state.ObservePlaybackInput(PlaybackInputKind.NoPlayback, now);

        Assert.True(transition.IsVisible);
        Assert.Equal(3, transition.CountdownSecondsRemaining);
        Assert.True(transition.PresentationChanged);
        Assert.True(state.HasReceivedPlaybackSnapshot);
        Assert.True(state.IsConfirmedNoPlayback);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    public void CountdownUsesOneSecondBoundaries(int elapsedSeconds, int expectedRemaining)
    {
        var start = DateTimeOffset.Parse("2026-08-16T00:00:00Z", CultureInfo.InvariantCulture);
        var state = new LyricsContentVisibilityStateMachine();
        state.ObservePlaybackInput(PlaybackInputKind.NoPlayback, start);

        var transition = state.ObservePlaybackInput(
            PlaybackInputKind.NoPlayback,
            start.AddSeconds(elapsedSeconds));

        if (expectedRemaining == 0)
        {
            Assert.False(transition.IsVisible);
            Assert.Null(transition.CountdownSecondsRemaining);
        }
        else
        {
            Assert.True(transition.IsVisible);
            Assert.Equal(expectedRemaining, transition.CountdownSecondsRemaining);
        }
        Assert.True(transition.PresentationChanged);
    }

    [Fact]
    public void RepeatedTickWithinSameCountdownSecondDoesNotChangePresentation()
    {
        var start = DateTimeOffset.Parse("2026-08-16T00:00:00Z", CultureInfo.InvariantCulture);
        var state = new LyricsContentVisibilityStateMachine();
        state.ObservePlaybackInput(PlaybackInputKind.NoPlayback, start);

        var transition = state.ObservePlaybackInput(
            PlaybackInputKind.NoPlayback,
            start.AddMilliseconds(60));

        Assert.Equal(3, transition.CountdownSecondsRemaining);
        Assert.False(transition.PresentationChanged);
    }

    [Fact]
    public void ValidPlaybackCancelsCountdownAndRestoresVisibility()
    {
        var start = DateTimeOffset.Parse("2026-08-16T00:00:00Z", CultureInfo.InvariantCulture);
        var state = new LyricsContentVisibilityStateMachine();
        state.ObservePlaybackInput(PlaybackInputKind.NoPlayback, start);

        var transition = state.ObservePlaybackInput(
            PlaybackInputKind.ValidTrack,
            start.AddSeconds(1));

        Assert.True(transition.IsVisible);
        Assert.Null(transition.CountdownSecondsRemaining);
        Assert.True(transition.PresentationChanged);
    }

    [Fact]
    public void CountdownExpiryKeepsHiddenStateUntilPlaybackReturns()
    {
        var start = DateTimeOffset.Parse("2026-08-16T00:00:00Z", CultureInfo.InvariantCulture);
        var state = new LyricsContentVisibilityStateMachine();
        state.ObservePlaybackInput(PlaybackInputKind.NoPlayback, start);
        state.ObservePlaybackInput(PlaybackInputKind.NoPlayback, start.AddSeconds(3));

        var hiddenTransition = state.ObservePlaybackInput(
            PlaybackInputKind.NoPlayback,
            start.AddSeconds(4));

        Assert.False(hiddenTransition.IsVisible);
        Assert.Null(hiddenTransition.CountdownSecondsRemaining);
        Assert.False(hiddenTransition.PresentationChanged);
    }

    [Fact]
    public void DisablingAutoHideImmediatelyRestoresWaitingState()
    {
        var start = DateTimeOffset.Parse("2026-08-16T00:00:00Z", CultureInfo.InvariantCulture);
        var state = new LyricsContentVisibilityStateMachine();
        state.ObservePlaybackInput(PlaybackInputKind.NoPlayback, start);

        var transition = state.ApplySettings(false, start.AddMilliseconds(60));

        Assert.True(transition.IsVisible);
        Assert.Null(transition.CountdownSecondsRemaining);
        Assert.True(transition.PresentationChanged);
    }

    [Fact]
    public void EnablingAutoHideAfterConfirmedNoPlaybackStartsFreshCountdown()
    {
        var start = DateTimeOffset.Parse("2026-08-16T00:00:00Z", CultureInfo.InvariantCulture);
        var state = new LyricsContentVisibilityStateMachine();
        state.ApplySettings(false, start);
        state.ObservePlaybackInput(PlaybackInputKind.NoPlayback, start.AddSeconds(1));

        var transition = state.ApplySettings(true, start.AddSeconds(2));

        Assert.True(transition.IsVisible);
        Assert.Equal(3, transition.CountdownSecondsRemaining);
        Assert.True(transition.PresentationChanged);
    }

    [Fact]
    public void InitialSettingsApplicationDoesNotHideBeforeFirstSnapshot()
    {
        var state = new LyricsContentVisibilityStateMachine();

        var transition = state.ApplySettings(
            true,
            DateTimeOffset.Parse("2026-08-16T00:00:00Z", CultureInfo.InvariantCulture));

        Assert.True(transition.IsVisible);
        Assert.Null(transition.CountdownSecondsRemaining);
        Assert.False(transition.PresentationChanged);
        Assert.False(state.HasReceivedPlaybackSnapshot);
    }

    [Fact]
    public void AutoHideDisabledKeepsWaitingVisibleWithoutCountdown()
    {
        var start = DateTimeOffset.Parse("2026-08-16T00:00:00Z", CultureInfo.InvariantCulture);
        var state = new LyricsContentVisibilityStateMachine();
        state.ApplySettings(false, start);

        var transition = state.ObservePlaybackInput(PlaybackInputKind.NoPlayback, start);

        Assert.True(transition.IsVisible);
        Assert.Null(transition.CountdownSecondsRemaining);
        Assert.True(transition.PresentationChanged);
    }

    private static TrackInfo CreateTrack(string id, string title)
    {
        return new TrackInfo(
            id,
            title,
            "Artist",
            "Album",
            "Netease",
            TimeSpan.FromMinutes(3));
    }
}
