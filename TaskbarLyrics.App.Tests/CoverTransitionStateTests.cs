using TaskbarLyrics.Core.Models;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class CoverTransitionStateTests
{
    [Fact]
    public void ArtworkHashUsesSha256AndIncludesAllBytes()
    {
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", CoverContentHash.Compute("abc"u8.ToArray()));
        Assert.NotEqual(CoverContentHash.Compute([1, 2, 3]), CoverContentHash.Compute([1, 2, 4]));
    }

    [Fact]
    public void CorrectedArtworkForTheSameSongReplacesTheFirstImageButEqualContentDoesNot()
    {
        var state = new CoverVisualTransitionState();
        state.Begin("song");
        Assert.True(state.AcceptArtwork("song", [1, 2]));
        Assert.False(state.AcceptArtwork("song", [1, 2]));
        Assert.True(state.AcceptArtwork("song", [3, 4]));
        Assert.False(state.AcceptArtwork("song", [3, 4]));
    }

    [Fact]
    public void SameAlbumArtworkCanBeReusedForANewTrackAndAfterAFallback()
    {
        var state = new CoverVisualTransitionState();
        byte[] cover = [1, 2];
        Assert.True(state.AcceptArtwork("A", cover));
        Assert.True(state.AcceptArtwork("B", cover));
        Assert.True(state.IsVisualFor("B"));
        state.MarkVisual("B");
        Assert.True(state.AcceptArtwork("B", cover));
    }

    [Fact]
    public void FromMetadataPrefersSongIdOverTitleArtistAndAlbum()
    {
        var first = CoverIdentity.FromMetadata(" Netease ", " 123 ", "Title", "Artist", "Album A");
        var second = CoverIdentity.FromMetadata("Netease", "123", "Different", "Other", "Album B");

        Assert.Equal("song|7:Netease|3:123", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void FromMetadataFallsBackToAlbumAwareMetadataIdentity()
    {
        var first = CoverIdentity.FromMetadata("Netease", null, "Title", "Artist", "Album A");
        var second = CoverIdentity.FromMetadata("Netease", null, "Title", "Artist", "Album B");
        var normalized = CoverIdentity.FromMetadata(" Netease ", null, " Title  ", " Artist\t", " Album A ");

        Assert.Equal("metadata|7:Netease|5:Title|6:Artist|7:Album A", first);
        Assert.NotEqual(first, second);
        Assert.Equal(first, normalized);
    }

    [Fact]
    public void NewIdentityRetainsPreviousVisualOnlyBeforeDeadline()
    {
        var now = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
        var state = new CoverVisualTransitionState(
            TimeSpan.FromSeconds(1.5),
            () => now);
        state.MarkVisual("old");

        Assert.True(state.Begin("new"));
        Assert.False(state.Begin("new"));
        Assert.True(state.ShouldRetainPreviousVisual());

        now = now.AddSeconds(1.5);
        Assert.False(state.ShouldRetainPreviousVisual());
    }

    [Fact]
    public void CurrentIdentityTakesOverEvenAfterTransitionDeadline()
    {
        var now = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
        var state = new CoverVisualTransitionState(
            TimeSpan.FromSeconds(1.5),
            () => now);
        state.MarkVisual("old");
        state.Begin("new");
        now = now.AddSeconds(2);

        state.MarkVisual("new");

        Assert.True(state.IsVisualFor("new"));
        Assert.False(state.ShouldRetainPreviousVisual());
    }

    [Fact]
    public void FromTrackUsesStableCoverIdentityWithoutChangingTrackId()
    {
        var track = new TrackInfo("lyrics-id", "Title", "Artist", "Album", "QQMusic", TimeSpan.Zero, "qq-42");

        Assert.Equal("lyrics-id", track.Id);
        Assert.Equal("song|7:QQMusic|5:qq-42", CoverIdentity.FromTrack(track));
    }

    [Fact]
    public void FallbackIsDebouncedWhileTheNewTrackMayStillPublishArtwork()
    {
        var now = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
        var state = new CoverVisualTransitionState(
            TimeSpan.FromMilliseconds(1500),
            TimeSpan.FromMilliseconds(800),
            () => now);
        state.MarkVisual("old");
        state.Begin("new");

        Assert.True(state.ShouldDebounceFallback());

        now = now.AddMilliseconds(800);
        Assert.False(state.ShouldDebounceFallback());
        Assert.True(state.ShouldRetainPreviousVisual());
    }

    [Fact]
    public void FallbackIsNotDebouncedWithoutAPreviousVisual()
    {
        var now = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
        var state = new CoverVisualTransitionState(TimeSpan.FromMilliseconds(1500), () => now);

        state.Begin("first");

        Assert.False(state.ShouldDebounceFallback());
    }

    [Fact]
    public void FallbackIsNotDebouncedOnceTheCurrentIdentityIsVisible()
    {
        var now = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
        var state = new CoverVisualTransitionState(TimeSpan.FromMilliseconds(1500), () => now);
        state.MarkVisual("old");
        state.Begin("new");
        state.MarkVisual("new");

        Assert.False(state.ShouldDebounceFallback());
    }

    [Fact]
    public void RapidTrackChangesRestartTheFallbackSettleDelay()
    {
        var now = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
        var state = new CoverVisualTransitionState(
            TimeSpan.FromMilliseconds(1500),
            TimeSpan.FromMilliseconds(800),
            () => now);
        state.MarkVisual("a");
        state.Begin("b");

        now = now.AddMilliseconds(600);
        state.Begin("c");
        now = now.AddMilliseconds(600);

        Assert.True(state.ShouldDebounceFallback());
    }

    [Fact]
    public void DefaultFallbackSettleDelayStaysShorterThanVisualRetention()
    {
        Assert.True(
            CoverVisualTransitionState.DefaultFallbackSettleDelay <
            CoverVisualTransitionState.DefaultRetention);
    }
}
