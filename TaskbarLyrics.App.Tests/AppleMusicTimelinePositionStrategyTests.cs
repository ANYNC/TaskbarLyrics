using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class AppleMusicTimelinePositionStrategyTests
{
    [Theory]
    [InlineData("AppleMusic", true)]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", true)]
    [InlineData(" appleMUSIC ", true)]
    [InlineData("Unknown.AppleMusic.Player", false)]
    [InlineData("QQMusic", false)]
    public void MatchesOnlyKnownAppleMusicIdentities(string source, bool expected)
    {
        var clock = new TimelineClock();
        var strategy = new AppleMusicTimelinePositionStrategy(clock);
        Assert.Equal(expected, strategy.CanApply(clock.Sample(42, 0, source: source)));
    }

    [Fact]
    public void RepeatedWholeSecondPositionsAndNormalIncrementsKeepTheClockContinuous()
    {
        var clock = new TimelineClock();
        var registry = TimelinePositionStrategyRegistry.CreateDefault(clock);
        // Reproduce the recorded cadence: one raw second with several timestamp-only updates.
        for (var index = 0; index <= 800; index++)
        {
            var elapsed = index * 0.05;
            clock.Set(elapsed);
            var updated = Math.Floor(elapsed / 0.278) * 0.278;
            var raw = 42 + Math.Floor(updated + 0.5);
            var selection = registry.Select(clock.Sample(raw, updated));
            Assert.Equal("AppleMusicContinuous", selection.StrategyName);
            Assert.InRange(Math.Abs(selection.Position.TotalSeconds - (42 + elapsed)), 0, 0.00001);
        }
    }

    [Fact]
    public void RecordedTimestampOnlyRefreshDoesNotRewindTheSelectedPosition()
    {
        var clock = new TimelineClock();
        var registry = TimelinePositionStrategyRegistry.CreateDefault(clock);
        clock.Set(0.1206605);
        registry.Select(clock.Sample(42, 0));
        clock.Set(0.2630259);
        var before = registry.Select(clock.Sample(42, 0));
        clock.Set(0.3248009);
        var after = registry.Select(clock.Sample(42, 0.279233));
        Assert.InRange((after.Position - before.Position).TotalMilliseconds, 61.774, 61.776);
        Assert.True(after.Position > clock.Sample(42, 0.279233).ExtrapolatedPosition);
    }

    [Fact]
    public void DelayedPollingAndWallClockChangesDoNotChangeThePlaybackRate()
    {
        var clock = new TimelineClock();
        var registry = TimelinePositionStrategyRegistry.CreateDefault(clock);
        registry.Select(clock.Sample(42, 0));
        clock.Set(5);
        Assert.Equal(TimeSpan.FromSeconds(47), registry.Select(clock.Sample(47, 4.9)).Position);
        clock.WallClockShift = TimeSpan.FromHours(-1);
        clock.Set(5.1);
        Assert.Equal(TimeSpan.FromSeconds(47.1), registry.Select(clock.Sample(47, 4.9)).Position);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(20)]
    public void FreshExternalSeeksReanchorInEitherDirection(double target)
    {
        var clock = new TimelineClock();
        var registry = TimelinePositionStrategyRegistry.CreateDefault(clock);
        registry.Select(clock.Sample(42, 0));
        clock.Set(0.25);
        var selection = registry.Select(clock.Sample(target, 0.23));
        Assert.InRange(Math.Abs(selection.Position.TotalSeconds - (target + 0.02)), 0, 0.000001);
    }

    [Fact]
    public void OutOfOrderTimelineUpdatesDoNotLookLikeBackwardSeeks()
    {
        var clock = new TimelineClock();
        var registry = TimelinePositionStrategyRegistry.CreateDefault(clock);
        registry.Select(clock.Sample(42, 0));
        clock.Set(1);
        registry.Select(clock.Sample(43, 0.9));
        clock.Set(1.1);
        Assert.Equal(TimeSpan.FromSeconds(43.1), registry.Select(clock.Sample(1, 0.5)).Position);
    }

    [Fact]
    public void PauseAndStaleResumeStayFrozenUntilTheTimelineRefreshes()
    {
        var clock = new TimelineClock();
        var registry = TimelinePositionStrategyRegistry.CreateDefault(clock);
        registry.Select(clock.Sample(42, 0));
        clock.Set(0.25);
        var frozen = registry.Select(clock.Sample(42, 0)).Position;
        clock.Set(0.5);
        Assert.Equal(frozen, registry.Select(clock.Sample(42, 0, playing: false)).Position);
        clock.Set(10);
        Assert.Equal(frozen, registry.Select(clock.Sample(42, 0, playing: false)).Position);
        clock.Set(10.1);
        Assert.Equal(frozen, registry.Select(clock.Sample(42, 0)).Position);
        clock.Set(10.3);
        Assert.Equal(frozen, registry.Select(clock.Sample(42, 10.28)).Position);
        clock.Set(10.4);
        Assert.InRange((registry.Select(clock.Sample(42, 10.28)).Position - frozen).TotalMilliseconds, 99.999, 100.001);
    }

    [Fact]
    public void ABackwardSeekWhilePausedIsNotClampedToThePreviousPosition()
    {
        var clock = new TimelineClock();
        var registry = TimelinePositionStrategyRegistry.CreateDefault(clock);
        registry.Select(clock.Sample(42, 0));
        clock.Set(0.5);
        Assert.Equal(TimeSpan.FromSeconds(10), registry.Select(clock.Sample(10, 0.49, playing: false)).Position);
        clock.Set(5);
        Assert.Equal(TimeSpan.FromSeconds(10), registry.Select(clock.Sample(10, 0.49, playing: false)).Position);
    }

    [Theory]
    [InlineData(42.4)]
    [InlineData(41.8)]
    public void AcknowledgedSmallSeeksApplyImmediatelyAndIgnoreOldSamples(double target)
    {
        var clock = new TimelineClock();
        var registry = TimelinePositionStrategyRegistry.CreateDefault(clock);
        registry.Select(clock.Sample(42, 0));
        clock.Set(0.2);
        registry.NotifySeek("AppleMusic", TimeSpan.FromSeconds(target), TimelineClock.Start);
        clock.Set(0.3);
        Assert.InRange(Math.Abs(registry.Select(clock.Sample(42, 0)).Position.TotalSeconds - target - 0.1), 0, 0.000001);
        clock.Set(0.5);
        var confirmed = registry.Select(clock.Sample(Math.Floor(target), 0.45));
        Assert.InRange(Math.Abs(confirmed.Position.TotalSeconds - target - 0.3), 0, 0.000001);
    }

    [Fact]
    public void AcknowledgedPausedSeekStaysFrozenAndResumesWithoutIncludingPausedTime()
    {
        var clock = new TimelineClock();
        var registry = TimelinePositionStrategyRegistry.CreateDefault(clock);
        registry.Select(clock.Sample(42, 0, playing: false));
        registry.NotifySeek("AppleMusic", TimeSpan.FromSeconds(42.5), TimelineClock.Start);
        clock.Set(1);
        Assert.Equal(TimeSpan.FromSeconds(42.5), registry.Select(clock.Sample(42, 0, playing: false)).Position);
        clock.Set(1.2);
        Assert.Equal(TimeSpan.FromSeconds(42.5), registry.Select(clock.Sample(42, 1.1, playing: false)).Position);
        clock.Set(2.2);
        Assert.Equal(TimeSpan.FromSeconds(42.5), registry.Select(clock.Sample(42, 1.1)).Position);
        clock.Set(2.4);
        Assert.Equal(TimeSpan.FromSeconds(42.5), registry.Select(clock.Sample(42, 2.3)).Position);
        clock.Set(2.5);
        Assert.Equal(TimeSpan.FromSeconds(42.6), registry.Select(clock.Sample(42, 2.3)).Position);
    }

    [Fact]
    public void UnconfirmedSeekEventuallyReconcilesWithTheActualTimeline()
    {
        var clock = new TimelineClock();
        var registry = TimelinePositionStrategyRegistry.CreateDefault(clock);
        registry.Select(clock.Sample(42, 0));
        registry.NotifySeek("AppleMusic", TimeSpan.FromSeconds(100), TimelineClock.Start);
        clock.Set(0.2);
        Assert.Equal(TimeSpan.FromSeconds(100.2), registry.Select(clock.Sample(42, 0)).Position);
        clock.Set(2.1);
        Assert.InRange(registry.Select(clock.Sample(44, 2.05)).Position.TotalSeconds, 44.04999, 44.05001);
    }

    [Fact]
    public void SeeksSentToAnotherPlayerDoNotMoveAppleMusic()
    {
        var clock = new TimelineClock();
        var registry = TimelinePositionStrategyRegistry.CreateDefault(clock);
        registry.Select(clock.Sample(42, 0));
        registry.NotifySeek("QQMusic", TimeSpan.FromSeconds(100), TimelineClock.Start);
        clock.Set(0.1);
        Assert.Equal(TimeSpan.FromSeconds(42.1), registry.Select(clock.Sample(42, 0)).Position);
    }

    [Theory]
    [InlineData("track")]
    [InlineData("player")]
    [InlineData("session")]
    public void NewTracksPlayersAndSessionsDiscardThePreviousAnchor(string change)
    {
        var clock = new TimelineClock();
        var registry = TimelinePositionStrategyRegistry.CreateDefault(clock);
        registry.Select(clock.Sample(42, 0));
        if (change == "player") registry.Select(clock.Sample(42, 0, source: "QQMusic"));
        if (change == "session") registry.Reset();
        clock.Set(20);
        var next = registry.Select(clock.Sample(3, 19.99, title: change == "track" ? "Other Song" : "Song"));
        Assert.InRange(next.Position.TotalSeconds, 3.009999, 3.010001);
    }

    [Theory]
    [InlineData("QQMusic", "CommonExtrapolated")]
    [InlineData("Netease", "CommonExtrapolated")]
    [InlineData("Spotify", "CommonExtrapolated")]
    [InlineData("Kugou", "CommonExtrapolated")]
    [InlineData("SodaMusic", "DefaultExtrapolated")]
    [InlineData("Foobar2000", "DefaultExtrapolated")]
    public void OtherPlayersKeepTheirExistingStrategies(string source, string expectedStrategy)
    {
        var clock = new TimelineClock();
        var registry = TimelinePositionStrategyRegistry.CreateDefault(clock);
        clock.Set(0.2);
        var selected = registry.Select(clock.Sample(42, 0, source: source));
        Assert.Equal(expectedStrategy, selected.StrategyName);
        Assert.Equal(TimeSpan.FromSeconds(42.2), selected.Position);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void PersistentDriftAdjustsTheRateGraduallyWithoutPositionJumps(int direction)
    {
        var clock = new TimelineClock();
        var registry = TimelinePositionStrategyRegistry.CreateDefault(clock);
        registry.Select(clock.Sample(42, 0));
        var start = direction > 0 ? 0.5 : 3.5;
        for (var index = 0; index < 3; index++)
        {
            clock.Set(start + index);
            var raw = 44 + index;
            Assert.Equal(TimeSpan.FromSeconds(42 + start + index), registry.Select(clock.Sample(raw, start + index)).Position);
        }
        clock.Set(start + 2.1);
        var last = registry.Select(clock.Sample(46, start + 2));
        Assert.InRange((last.Position - TimeSpan.FromSeconds(42 + start + 2)).TotalSeconds, 0.094999, 0.105001);
        Assert.Equal(direction, Math.Sign(last.Position.TotalSeconds - (42 + start + 2.1)));
    }

    private sealed class TimelineClock : TimeProvider
    {
        internal static readonly DateTimeOffset Start = new(2026, 10, 21, 13, 27, 30, TimeSpan.Zero);
        private long _ticks;
        internal TimeSpan WallClockShift { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => Start.AddTicks(_ticks) + WallClockShift;
        internal void Set(double seconds) => _ticks = TimeSpan.FromSeconds(seconds).Ticks;

        internal SmtcTimelineDiagnostics Sample(double raw, double updated, bool playing = true,
            string source = "AppleMusic", string title = "Song")
        {
            var position = TimeSpan.FromSeconds(raw);
            var timestamp = Start.AddSeconds(updated);
            var age = GetUtcNow() - timestamp;
            if (age < TimeSpan.Zero) age = TimeSpan.Zero;
            var extrapolated = position + (playing ? age : TimeSpan.Zero);
            return new(GetUtcNow(), source, source, source, playing, position, timestamp, age,
                extrapolated, position, "", title, "Artist", false);
        }
    }
}
