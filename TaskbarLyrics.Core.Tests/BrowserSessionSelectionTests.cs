using TaskbarLyrics.Core.Models;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class BrowserSessionSelectionTests
{
    [Fact]
    public void StandardMetadataReplacesPageTitleFallbackWithinASiteAndEqualQualityRemainsSticky()
    {
        BrowserSiteRule[] rules = [new("music", "Music", "https://music.test")];
        var fallback = Session("background", "https://music.test", true) with
        { Details = new(1, 1, "element", "document", "playing", 2, 8, 1, 0, [], [], []) };
        var standard = Session("music", "https://music.test", true) with
        { Details = fallback.Details with { Source = "media-session", TitleSource = "media-session", DurationSeconds = 200 } };
        Assert.Equal(standard, BrowserSessionSelection.Select([fallback, standard], rules, fallback.Key));
        var other = standard with { Key = "other-music" };
        Assert.Equal(standard, BrowserSessionSelection.Select([other, standard], rules, standard.Key));
    }

    [Theory]
    [InlineData("music.example.com/path?q=secret", "https://music.example.com")]
    [InlineData("https://music.example.com:443/music", "https://music.example.com")]
    [InlineData("http://localhost:8096/web", "http://localhost:8096")]
    [InlineData("https://音乐.test/path", "https://xn--fjq180p.test")]
    public void RulesKeepOnlyExactOrigins(string address, string expected)
    {
        Assert.True(BrowserSiteRule.TryCreate(address, null, out var rule));
        Assert.Equal(expected, rule.Origin);
        Assert.True(rule.Matches(expected + "/another?token=private"));
        Assert.False(rule.Matches("https://other.music.example.com"));
        Assert.False(rule.Matches(expected + ":1234"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://user:password@example.com")]
    [InlineData("file:///C:/music")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://example.com/\npath")]
    public void InvalidRulesAreRejected(string address) => Assert.False(BrowserSiteRule.TryCreate(address, null, out _));

    [Fact]
    public void OnlyEnabledListedSitesParticipateWithPlayingBeforeSiteOrderAndStableTies()
    {
        BrowserSiteRule[] rules = [new("music", "Music", "https://music.test"), new("video", "Video", "https://video.test")];
        var music = Session("music-a", "https://music.test", false);
        var video = Session("video", "https://video.test", true);
        var other = Session("other", "https://other.test", true);
        Assert.Equal(video, BrowserSessionSelection.Select([music, video, other], rules, music.Key));
        music = music with { IsPlaying = true };
        Assert.Equal(music, BrowserSessionSelection.Select([music, video, other], rules, video.Key));
        var second = music with { Key = "music-b" };
        Assert.Equal(second, BrowserSessionSelection.Select([music, second, video], rules, second.Key));
        Assert.Null(BrowserSessionSelection.Select([music, video, other], [], null));
        Assert.Null(BrowserSessionSelection.Select([music], [rules[0] with { Enabled = false }], null));
    }

    [Fact]
    public void NormalizationDropsDuplicateAndInvalidOriginsWithoutEnablingDisabledSites()
    {
        var result = BrowserSiteRule.Normalize([new("one", "Music", "https://music.test/path", false),
            new("two", "Duplicate", "https://music.test"), new("three", "Bad", "file:///music")]);
        var rule = Assert.Single(result);
        Assert.Equal("one", rule.Id);
        Assert.Equal("https://music.test", rule.Origin);
        Assert.False(rule.Enabled);
    }

    [Theory]
    [InlineData(true, false, 20, 0, true)]
    [InlineData(false, true, 0, 20, false)]
    [InlineData(true, true, 20, 0, false)]
    [InlineData(true, true, 0, 20, true)]
    [InlineData(false, false, 20, 0, false)]
    [InlineData(false, null, 20, 0, true)]
    public void OuterSelectionPreservesPlaybackStateBeforePlayerPriority(bool playing, bool? other, int rank, int otherRank, bool expected) =>
        Assert.Equal(expected, BrowserSessionSelection.PreferBrowser(playing, other, rank, otherRank));

    private static BrowserMediaSession Session(string key, string origin, bool playing) =>
        new(key, "client", "Edge", "doc", "media", origin, "Song", "Artist", "Album", playing, 10, 100, true, 1);
}
