using TaskbarLyrics.Core.Models;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class SmtcMusicSessionProviderTests
{
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
