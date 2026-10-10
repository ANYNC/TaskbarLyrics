using TaskbarLyrics.Core.Models;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class SmtcMusicSessionProviderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingSourceMetadataIsIgnoredWithoutDereferencingIt(string? source)
    {
        using var provider = new SmtcMusicSessionProvider();
        Assert.False(provider.CanUseSource(source));
        Assert.Equal(string.Empty, SmtcMusicSessionProvider.NormalizeSource(source));
    }
    [Fact]
    public void ProcessFallbackDetectionCacheUsesCachedResultWithinRefreshInterval()
    {
        var now = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
        var refreshCount = 0;
        var cache = new ProcessFallbackDetectionCache(() => now);

        string? Detect()
        {
            refreshCount++;
            return refreshCount == 1 ? "Spotify" : "Netease";
        }

        Assert.Equal("Spotify", cache.GetOrRefresh(Detect));

        now = now.AddMilliseconds(ProcessFallbackDetectionCache.RefreshInterval.TotalMilliseconds - 1);

        Assert.Equal("Spotify", cache.GetOrRefresh(Detect));
        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public void ProcessFallbackDetectionCacheRefreshesCachedNullAtInterval()
    {
        var now = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
        var refreshCount = 0;
        var cache = new ProcessFallbackDetectionCache(() => now);

        Assert.Null(cache.GetOrRefresh(() =>
        {
            refreshCount++;
            return null;
        }));

        now = now.AddMilliseconds(ProcessFallbackDetectionCache.RefreshInterval.TotalMilliseconds - 1);

        Assert.Null(cache.GetOrRefresh(() =>
        {
            refreshCount++;
            return "Spotify";
        }));
        Assert.Equal(1, refreshCount);

        now = now.AddMilliseconds(1);

        Assert.Equal("Spotify", cache.GetOrRefresh(() =>
        {
            refreshCount++;
            return "Spotify";
        }));
        Assert.Equal(2, refreshCount);
    }

    [Fact]
    public void ProcessFallbackDetectionCacheInvalidationForcesImmediateRefresh()
    {
        var now = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
        var refreshCount = 0;
        var cache = new ProcessFallbackDetectionCache(() => now);

        Assert.Equal("Spotify", cache.GetOrRefresh(() =>
        {
            refreshCount++;
            return "Spotify";
        }));

        cache.Invalidate();

        Assert.Equal("Netease", cache.GetOrRefresh(() =>
        {
            refreshCount++;
            return "Netease";
        }));
        Assert.Equal(2, refreshCount);
    }

    [Fact]
    public void ApplyLatestPlaybackStateWhenPauseArrivesAfterSnapshotCaptureUsesPausedState()
    {
        var track = new TrackInfo(
            "track-id",
            "Song",
            "Artist",
            "Album",
            "TestPlayer",
            TimeSpan.FromMinutes(3));
        var snapshot = new PlaybackSnapshot(
            IsPlaying: true,
            Position: TimeSpan.FromSeconds(42),
            Track: track,
            RawPosition: TimeSpan.FromSeconds(40),
            ExtrapolatedPosition: TimeSpan.FromSeconds(42));

        var refreshed = SmtcMusicSessionProvider.ApplyLatestPlaybackState(
            snapshot,
            isPlaying: false);

        Assert.False(refreshed.IsPlaying);
        Assert.Equal(snapshot.Position, refreshed.Position);
        Assert.Equal(snapshot.RawPosition, refreshed.RawPosition);
        Assert.Equal(snapshot.ExtrapolatedPosition, refreshed.ExtrapolatedPosition);
        Assert.Same(snapshot.Track, refreshed.Track);
    }

    [Fact]
    public void ApplyLatestPlaybackStateWhenStateIsUnchangedReturnsOriginalSnapshot()
    {
        var snapshot = new PlaybackSnapshot(
            IsPlaying: false,
            Position: TimeSpan.FromSeconds(42),
            Track: null);

        var refreshed = SmtcMusicSessionProvider.ApplyLatestPlaybackState(
            snapshot,
            isPlaying: false);

        Assert.Same(snapshot, refreshed);
    }
}
