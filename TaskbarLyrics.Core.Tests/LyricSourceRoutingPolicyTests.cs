using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricSourceRoutingPolicyTests
{
    [Theory]
    [InlineData("QQMusic.exe", "QQMusic")]
    [InlineData("CloudMusic", "Netease")]
    [InlineData("music.163.com", "Netease")]
    [InlineData("KuGou Music", "Kugou")]
    [InlineData("Spotify", "")]
    public void TryGetOfficialProvider_MapsKnownPlayersAndRejectsOthers(string sourceApp, string expectedProvider)
    {
        var found = LyricSourceRoutingPolicy.TryGetOfficialProvider(sourceApp, out var provider);

        Assert.Equal(!string.IsNullOrEmpty(expectedProvider), found);
        Assert.Equal(expectedProvider, provider);
    }

    [Fact]
    public void BuildFallbackBatches_ForOfficialPlayer_ExcludesItsOwnProvider()
    {
        var batches = LyricSourceRoutingPolicy.BuildFallbackBatches(CreateTrack("QQMusic"));

        var batch = Assert.Single(batches);
        Assert.Equal(["Netease", "Kugou", "LRCLIB"], batch);
    }

    [Fact]
    public void BuildFallbackBatches_ForOtherPlayer_KeepsConfiguredTwoPhaseFallback()
    {
        var batches = LyricSourceRoutingPolicy.BuildFallbackBatches(CreateTrack("Spotify"));

        Assert.Collection(
            batches,
            first => Assert.Equal(["QQMusic", "Netease", "LRCLIB"], first),
            second => Assert.Equal(["Kugou"], second));
    }

    private static TrackInfo CreateTrack(string sourceApp) => new(
        "track-id",
        "Midnight City",
        "M83",
        "Hurry Up, We're Dreaming",
        sourceApp,
        TimeSpan.FromSeconds(244));
}
