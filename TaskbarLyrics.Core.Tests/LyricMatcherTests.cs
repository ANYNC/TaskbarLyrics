using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricMatcherTests
{
    [Fact]
    public void ScoreWhenTitleArtistAndDurationMatchReturnsHighConfidence()
    {
        var track = CreateTrack("Midnight City", "M83", 244);

        var score = LyricMatcher.Score(track, "Midnight City", "M83", 244);

        Assert.True(score >= 95);
    }

    [Fact]
    public void ScoreWhenOnlyOneTitleHasLiveVersionMarkerRejectsTheMatch()
    {
        var track = CreateTrack("Midnight City", "M83", 244);

        var score = LyricMatcher.Score(track, "Midnight City (Live)", "M83", 244);

        Assert.Equal(0, score);
    }

    [Fact]
    public void ScoreWhenNonQqMusicDurationDiffersByTwentySecondsRejectsTheMatch()
    {
        var track = CreateTrack("Midnight City", "M83", 244);

        var score = LyricMatcher.Score(track, "Midnight City", "M83", 270);

        Assert.Equal(0, score);
    }

    [Fact]
    public void ScoreUsesAlbumAsLowWeightEvidenceWithoutHardRejection()
    {
        var track = CreateTrack("Midnight City", "M83", 244);

        var matchingAlbum = LyricMatcher.Score(
            track,
            "Midnight City",
            "M83",
            244,
            "Album");
        var differentAlbum = LyricMatcher.Score(
            track,
            "Midnight City",
            "M83",
            244,
            "Compilation");

        Assert.Equal(100, matchingAlbum);
        Assert.InRange(differentAlbum, 90, 99);
    }

    private static TrackInfo CreateTrack(string title, string artist, int durationSeconds) => new(
        "track-id",
        title,
        artist,
        "Album",
        "Spotify",
        TimeSpan.FromSeconds(durationSeconds));
}
