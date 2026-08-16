using TaskbarLyrics.App;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class LyricsSearchingPresentationPolicyTests
{
    [Fact]
    public void BuildsMetadataLineAndPreservesSearchingPromptAsSecondLine()
    {
        var result = LyricsSearchingPresentationPolicy.Create(CreateTrack("Song", "Artist"));

        Assert.Equal("Song - Artist", result.Current);
        Assert.Equal(LyricSyncService.SearchingText, result.Next);
    }

    [Fact]
    public void OmitsSeparatorWhenArtistIsBlank()
    {
        var result = LyricsSearchingPresentationPolicy.Create(CreateTrack("  Song  ", "  "));

        Assert.Equal("Song", result.Current);
        Assert.Equal(LyricSyncService.SearchingText, result.Next);
    }

    [Fact]
    public void FallsBackToSearchingPromptForMissingMetadata()
    {
        var result = LyricsSearchingPresentationPolicy.Create(CreateTrack("  ", " "));

        Assert.Equal(LyricSyncService.SearchingText, result.Current);
        Assert.Equal(LyricSyncService.SearchingText, result.Next);
    }

    private static TrackInfo CreateTrack(string title, string artist)
    {
        return new TrackInfo(
            "track-id",
            title,
            artist,
            "Album",
            "Netease",
            TimeSpan.FromMinutes(3));
    }
}
