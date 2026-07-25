using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricMatcherTests
{
    [Fact]
    public void Score_WhenTitleArtistAndDurationMatch_ReturnsHighConfidence()
    {
        var track = CreateTrack("Midnight City", "M83", 244);

        var score = LyricMatcher.Score(track, "Midnight City", "M83", 244);

        Assert.True(score >= 95);
    }

    [Fact]
    public void Score_WhenOnlyOneTitleHasLiveVersionMarker_RejectsTheMatch()
    {
        var track = CreateTrack("Midnight City", "M83", 244);

        var score = LyricMatcher.Score(track, "Midnight City (Live)", "M83", 244);

        Assert.Equal(0, score);
    }

    [Fact]
    public void Score_WhenNonQqMusicDurationDiffersByTwentySeconds_RejectsTheMatch()
    {
        var track = CreateTrack("Midnight City", "M83", 244);

        var score = LyricMatcher.Score(track, "Midnight City", "M83", 270);

        Assert.Equal(0, score);
    }

    private static TrackInfo CreateTrack(string title, string artist, int durationSeconds) => new(
        "track-id",
        title,
        artist,
        "Album",
        "Spotify",
        TimeSpan.FromSeconds(durationSeconds));
}
