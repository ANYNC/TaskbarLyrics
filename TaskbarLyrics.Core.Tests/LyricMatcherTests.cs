using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
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
    public void ScoreTreatsBracketedVersionMarkerAsEquivalentToDelimitedVersionMarker()
    {
        var track = CreateTrack("Anti-Hero - ILLENIUM Remix", "Taylor Swift", 267) with
        {
            Album = "Anti-Hero (Remixes) [Explicit]"
        };

        var score = LyricMatcher.Score(
            track,
            "Anti-Hero (Illenium Remix)",
            "Taylor Swift / ILLENIUM",
            267,
            "Anti-Hero (Remixes) [Explicit]");

        Assert.Equal(98, score);
    }

    [Fact]
    public void ScoreRejectsDifferentVersionMarkersInsideBrackets()
    {
        var track = CreateTrack("Anti-Hero - ILLENIUM Remix", "Taylor Swift", 267);

        var score = LyricMatcher.Score(
            track,
            "Anti-Hero (Live)",
            "Taylor Swift",
            267);

        Assert.Equal(0, score);
    }

    [Fact]
    public void ScoreStillIgnoresNonVersionBracketedTitleContent()
    {
        var track = CreateTrack("Midnight City", "M83", 244);

        var score = LyricMatcher.Score(track, "Midnight City (Radio Edit)", "M83", 244);

        Assert.Equal(100, score);
    }

    [Fact]
    public void ScoreTreatsBracketedFromQualifierAsEquivalentToDelimitedQualifier()
    {
        var track = CreateTrack("Nobody - from Kaiju No. 8", "OneRepublic", 153) with
        {
            Album = "Nobody (from Kaiju No. 8)"
        };

        var score = LyricMatcher.Score(
            track,
            "Nobody (from Kaiju No. 8)",
            "OneRepublic",
            153,
            "Nobody (from Kaiju No. 8)");

        Assert.Equal(100, score);
    }

    [Fact]
    public void ScoreRejectsFromQualifierWhenOnlyOneTitleContainsIt()
    {
        var track = CreateTrack("Nobody - from Kaiju No. 8", "OneRepublic", 153);

        var score = LyricMatcher.Score(track, "Nobody", "OneRepublic", 153);

        Assert.Equal(0, score);
    }

    [Fact]
    public void ScoreWhenNonQqMusicDurationDiffersByTwentySecondsReducesScoreButDoesNotReject()
    {
        var track = CreateTrack("Midnight City", "M83", 244);

        var score = LyricMatcher.Score(track, "Midnight City", "M83", 270);

        // Duration difference >= 10s zeroes the duration component, but title+artist still score.
        Assert.InRange(score, 1, 100);
        Assert.True(score < 100);
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
