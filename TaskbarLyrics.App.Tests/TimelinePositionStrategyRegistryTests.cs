using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class TimelinePositionStrategyRegistryTests
{
    [Fact]
    public void SelectWhenPlaybackPausesWithoutTimelineRefreshKeepsLastSelectedPosition()
    {
        var registry = CreateRegistry();
        var timelineUpdatedAt = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);

        var playing = registry.Select(CreateDiagnostics(
            isPlaying: true,
            rawPosition: TimeSpan.FromSeconds(42),
            timelineUpdatedAt));
        var paused = registry.Select(CreateDiagnostics(
            isPlaying: false,
            rawPosition: TimeSpan.FromSeconds(10),
            timelineUpdatedAt));
        var pausedForward = registry.Select(CreateDiagnostics(
            isPlaying: false,
            rawPosition: TimeSpan.FromSeconds(50),
            timelineUpdatedAt));
        var pausedAgain = registry.Select(CreateDiagnostics(
            isPlaying: false,
            rawPosition: TimeSpan.FromSeconds(45),
            timelineUpdatedAt));

        Assert.Equal(TimeSpan.FromSeconds(42), playing.Position);
        Assert.Equal(TimeSpan.FromSeconds(42), paused.Position);
        Assert.Equal(TimeSpan.FromSeconds(42), pausedForward.Position);
        Assert.Equal(TimeSpan.FromSeconds(42), pausedAgain.Position);
    }

    [Fact]
    public void SelectWhenPausedTimelineRefreshesAcceptsNewPosition()
    {
        var registry = CreateRegistry();
        var timelineUpdatedAt = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);

        registry.Select(CreateDiagnostics(
            isPlaying: true,
            rawPosition: TimeSpan.FromSeconds(42),
            timelineUpdatedAt));
        registry.Select(CreateDiagnostics(
            isPlaying: false,
            rawPosition: TimeSpan.FromSeconds(10),
            timelineUpdatedAt));
        var staleForwardSeek = registry.Select(CreateDiagnostics(
            isPlaying: false,
            rawPosition: TimeSpan.FromSeconds(50),
            timelineUpdatedAt));

        var refreshed = registry.Select(CreateDiagnostics(
            isPlaying: false,
            rawPosition: TimeSpan.FromSeconds(7),
            timelineUpdatedAt.AddSeconds(1)));

        Assert.Equal(TimeSpan.FromSeconds(42), staleForwardSeek.Position);
        Assert.Equal(TimeSpan.FromSeconds(7), refreshed.Position);
    }

    [Fact]
    public void SelectWhenPlaybackPausesWithTimelineRefreshAcceptsPausePosition()
    {
        var registry = CreateRegistry();
        var playingTimelineUpdatedAt = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
        var pausedTimelineUpdatedAt = playingTimelineUpdatedAt.AddSeconds(1);

        registry.Select(CreateDiagnostics(
            isPlaying: true,
            rawPosition: TimeSpan.FromSeconds(42),
            playingTimelineUpdatedAt));

        var paused = registry.Select(CreateDiagnostics(
            isPlaying: false,
            rawPosition: TimeSpan.FromSeconds(50),
            pausedTimelineUpdatedAt));
        var pausedForward = registry.Select(CreateDiagnostics(
            isPlaying: false,
            rawPosition: TimeSpan.FromSeconds(60),
            pausedTimelineUpdatedAt));

        Assert.Equal(TimeSpan.FromSeconds(50), paused.Position);
        Assert.Equal(TimeSpan.FromSeconds(50), pausedForward.Position);
    }

    [Fact]
    public void SelectWhenPausedTimelineRefreshesBySmallAdvanceDefersCorrectionUntilResumeRefresh()
    {
        var registry = CreateRegistry();
        var playingTimelineUpdatedAt = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
        var pausedTimelineUpdatedAt = playingTimelineUpdatedAt.AddSeconds(1);

        var playing = registry.Select(CreateDiagnostics(
            isPlaying: true,
            rawPosition: TimeSpan.FromSeconds(42),
            playingTimelineUpdatedAt));
        var paused = registry.Select(CreateDiagnostics(
            isPlaying: false,
            rawPosition: TimeSpan.FromSeconds(42),
            playingTimelineUpdatedAt));
        var deferredPausedAdvance = registry.Select(CreateDiagnostics(
            isPlaying: false,
            rawPosition: TimeSpan.FromMilliseconds(42250),
            pausedTimelineUpdatedAt));
        var staleResume = registry.Select(CreateDiagnostics(
            isPlaying: true,
            rawPosition: TimeSpan.FromMilliseconds(42250),
            pausedTimelineUpdatedAt));
        var refreshedResume = registry.Select(CreateDiagnostics(
            isPlaying: true,
            rawPosition: TimeSpan.FromMilliseconds(42500),
            pausedTimelineUpdatedAt.AddSeconds(1)));

        Assert.Equal(TimeSpan.FromSeconds(42), playing.Position);
        Assert.Equal(TimeSpan.FromSeconds(42), paused.Position);
        Assert.Equal(TimeSpan.FromSeconds(42), deferredPausedAdvance.Position);
        Assert.Equal(TimeSpan.FromSeconds(42), staleResume.Position);
        Assert.Equal(TimeSpan.FromMilliseconds(42500), refreshedResume.Position);
    }

    [Fact]
    public void SelectWhenPausedTrackChangesDoesNotClampNewTrackPosition()
    {
        var registry = CreateRegistry();
        var timelineUpdatedAt = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);

        registry.Select(CreateDiagnostics(
            isPlaying: true,
            rawPosition: TimeSpan.FromSeconds(42),
            timelineUpdatedAt,
            title: "First song"));
        registry.Select(CreateDiagnostics(
            isPlaying: false,
            rawPosition: TimeSpan.FromSeconds(10),
            timelineUpdatedAt,
            title: "First song"));

        var changedTrack = registry.Select(CreateDiagnostics(
            isPlaying: false,
            rawPosition: TimeSpan.FromSeconds(3),
            timelineUpdatedAt,
            title: "Second song"));

        Assert.Equal(TimeSpan.FromSeconds(3), changedTrack.Position);
    }

    [Fact]
    public void SelectWhenPausedThenPlayingWithStaleTimelineKeepsPausePositionUntilRefresh()
    {
        var registry = CreateRegistry();
        var playingTimelineUpdatedAt = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
        var pausedTimelineUpdatedAt = playingTimelineUpdatedAt.AddSeconds(1);

        var playing = registry.Select(CreateDiagnostics(
            isPlaying: true,
            rawPosition: TimeSpan.FromSeconds(42),
            playingTimelineUpdatedAt,
            extrapolatedPosition: TimeSpan.FromSeconds(45)));
        var paused = registry.Select(CreateDiagnostics(
            isPlaying: false,
            rawPosition: TimeSpan.FromSeconds(50),
            pausedTimelineUpdatedAt,
            extrapolatedPosition: TimeSpan.FromSeconds(50)));
        var stalePlaying = registry.Select(CreateDiagnostics(
            isPlaying: true,
            rawPosition: TimeSpan.FromSeconds(50),
            pausedTimelineUpdatedAt,
            extrapolatedPosition: TimeSpan.FromSeconds(95)));
        var stalePlayingAgain = registry.Select(CreateDiagnostics(
            isPlaying: true,
            rawPosition: TimeSpan.FromSeconds(50),
            pausedTimelineUpdatedAt,
            extrapolatedPosition: TimeSpan.FromSeconds(100)));
        var refreshedPlaying = registry.Select(CreateDiagnostics(
            isPlaying: true,
            rawPosition: TimeSpan.FromSeconds(55),
            pausedTimelineUpdatedAt.AddSeconds(1),
            extrapolatedPosition: TimeSpan.FromSeconds(57)));

        Assert.Equal(TimeSpan.FromSeconds(45), playing.Position);
        Assert.Equal(TimeSpan.FromSeconds(50), paused.Position);
        Assert.Equal(TimeSpan.FromSeconds(50), stalePlaying.Position);
        Assert.Equal(TimeSpan.FromSeconds(50), stalePlayingAgain.Position);
        Assert.Equal(TimeSpan.FromSeconds(57), refreshedPlaying.Position);
    }

    private static TimelinePositionStrategyRegistry CreateRegistry()
    {
        var strategy = new ExtrapolatedPositionStrategy();
        return new TimelinePositionStrategyRegistry(new[] { strategy }, strategy);
    }

    private static SmtcTimelineDiagnostics CreateDiagnostics(
        bool isPlaying,
        TimeSpan rawPosition,
        DateTimeOffset timelineUpdatedAt,
        string source = "TestPlayer",
        string title = "Song",
        string artist = "Artist",
        TimeSpan? extrapolatedPosition = null)
    {
        return new SmtcTimelineDiagnostics(
            CapturedAtUtc: timelineUpdatedAt.AddMilliseconds(10),
            SourceAppUserModelId: source,
            NormalizedSource: source,
            ResolvedSource: source,
            IsPlaying: isPlaying,
            RawPosition: rawPosition,
            LastUpdatedTimeUtc: timelineUpdatedAt,
            LastUpdateAge: TimeSpan.Zero,
            ExtrapolatedPosition: extrapolatedPosition ?? rawPosition,
            SelectedPosition: extrapolatedPosition ?? rawPosition,
            StrategyName: "Raw",
            Title: title,
            Artist: artist,
            IsFallbackSnapshot: false);
    }

    private sealed class ExtrapolatedPositionStrategy : ITimelinePositionStrategy
    {
        public string Name => "Raw";

        public bool CanApply(SmtcTimelineDiagnostics diagnostics) => true;

        public TimeSpan SelectPosition(SmtcTimelineDiagnostics diagnostics) => diagnostics.ExtrapolatedPosition;
    }
}
