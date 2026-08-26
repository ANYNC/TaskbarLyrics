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

    [Fact]
    public void ScoreAdmitsCrossScriptArtistAliasWithoutImmediateAcceptance()
    {
        var track = CreateTrack("新宝島", "魚韻", 305) with { Album = "834.194" };

        var score = LyricMatcher.Score(track, "新宝島", "sakanaction", 306, "新宝島");

        Assert.True(score >= LyricMatchingPolicy.MinimumAcceptedMatchScore);
        Assert.True(score < LyricMatchingPolicy.ImmediateAcceptanceScore);
    }

    [Fact]
    public void ScoreTreatsKanaVersusLatinArtistNamesAsNotComparable()
    {
        var track = CreateTrack("新宝島", "サカナクション", 305);

        var score = LyricMatcher.Score(track, "新宝島", "sakanaction", 305);

        Assert.True(score >= LyricMatchingPolicy.MinimumAcceptedMatchScore);
    }

    [Fact]
    public void ScoreAdmitsCrossScriptArtistAliasWithDurationDrift()
    {
        var track = CreateTrack("新宝島", "魚韻", 300);

        var score = LyricMatcher.Score(track, "新宝島", "sakanaction", 305);

        Assert.True(score >= LyricMatchingPolicy.MinimumAcceptedMatchScore);
    }

    [Fact]
    public void ScoreStillPenalizesDifferentArtistsWithinSameScript()
    {
        var track = CreateTrack("Midnight City", "M83", 244);

        var score = LyricMatcher.Score(track, "Midnight City", "Coldplay", 244);

        Assert.True(score < LyricMatchingPolicy.MinimumAcceptedMatchScore);
    }

    [Fact]
    public void ScoreComparesMixedScriptArtistNamesNormally()
    {
        var track = CreateTrack("新宝島", "X玖少年团", 305);

        var score = LyricMatcher.Score(track, "新宝島", "sakanaction", 305);

        Assert.True(score < LyricMatchingPolicy.MinimumAcceptedMatchScore);
    }

    [Fact]
    public void ScoreRanksVerifiedSameScriptArtistAboveCrossScriptCandidate()
    {
        var track = CreateTrack("Love Me Back", "RITUAL / Tove Styrke", 178);

        var verifiedScore = LyricMatcher.Score(
            track,
            "Love Me Back",
            "R I T U A L / Tove Styrke",
            178,
            "Love Me Back");
        var crossScriptScore = LyricMatcher.Score(
            track,
            "Love Me Back (爱我)",
            "倖田來未",
            177,
            "Love Me Back");

        Assert.True(verifiedScore > crossScriptScore);
    }

    [Fact]
    public void ScoreTreatsSpacedLetterArtistStylingAsExactMatch()
    {
        var track = CreateTrack("Love Me Back", "RITUAL / Tove Styrke", 178) with
        {
            Album = "Love Me Back"
        };

        var score = LyricMatcher.Score(
            track,
            "Love Me Back",
            "R I T U A L / Tove Styrke",
            178,
            "Love Me Back");

        Assert.Equal(100, score);
    }

    [Fact]
    public void ScoreKeepsCrossScriptArtistBelowImmediateAcceptance()
    {
        var track = CreateTrack("Love Me Back", "RITUAL / Tove Styrke", 178);

        var score = LyricMatcher.Score(track, "Love Me Back", "倖田來未", 178, "Love Me Back");

        Assert.True(score >= LyricMatchingPolicy.MinimumAcceptedMatchScore);
        Assert.True(score < LyricMatchingPolicy.ImmediateAcceptanceScore);
    }

    private static TrackInfo CreateTrack(string title, string artist, int durationSeconds) => new(
        "track-id",
        title,
        artist,
        "Album",
        "Spotify",
        TimeSpan.FromSeconds(durationSeconds));
}
