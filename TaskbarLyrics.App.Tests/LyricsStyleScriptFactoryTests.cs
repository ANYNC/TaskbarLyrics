using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class LyricsStyleScriptFactoryTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    public void CreateEmitsControlPanelAvailabilityForEmbeddedModeOnly(bool enabled, bool floating, bool expected)
    {
        var settings = new AppSettings { EnableControlPanel = enabled, UseFloatingWindow = floating };
        var script = LyricsStyleScriptFactory.Create(settings, 1);

        Assert.Contains($"\"enableControlPanel\":{expected.ToString().ToLowerInvariant()}", script);
        Assert.Equal(enabled, settings.EnableControlPanel);
    }

    [Fact]
    public void CreateAppliesLyricsOpacityHierarchyWithoutChangingStoredForegroundColor()
    {
        var settings = new AppSettings { ForegroundColor = "#FF123456" };

        var script = LyricsStyleScriptFactory.Create(settings, pixelsPerDip: 1);

        Assert.Contains("\"primaryColor\":\"rgba(18, 52, 86, 0.898)\"", script);
        Assert.Contains("\"secondaryColor\":\"rgba(18, 52, 86, 0.6)\"", script);
        Assert.Contains("\"translationColor\":\"rgba(18, 52, 86, 0.698)\"", script);
        Assert.Contains("\"wordScanOverlayColor\":\"rgba(18, 52, 86, 0.745)\"", script);
        Assert.Equal("#FF123456", settings.ForegroundColor);
    }

    [Theory]
    [InlineData(LyricsTextAlignment.Left, "Left")]
    [InlineData(LyricsTextAlignment.Center, "Center")]
    [InlineData(LyricsTextAlignment.Right, "Right")]
    public void CreateEmitsTheSelectedLyricsTextAlignment(LyricsTextAlignment alignment, string expected)
    {
        var script = LyricsStyleScriptFactory.Create(
            new AppSettings { LyricsTextAlignment = alignment },
            pixelsPerDip: 1);

        Assert.Contains($"\"textAlignment\":\"{expected}\"", script);
    }

    [Fact]
    public void CreateNormalizesUndefinedLyricsTextAlignmentToLeft()
    {
        var settings = new AppSettings
        {
            LyricsTextAlignment = (LyricsTextAlignment)999
        };

        var script = LyricsStyleScriptFactory.Create(settings, pixelsPerDip: 1);

        Assert.Contains("\"textAlignment\":\"Left\"", script);
    }
    [Theory]
    [InlineData(CoverPosition.Left, "Left")]
    [InlineData(CoverPosition.Right, "Right")]
    [InlineData((CoverPosition)999, "Left")]
    public void CreateEmitsNormalizedCoverPosition(CoverPosition position, string expected)
    {
        var script = LyricsStyleScriptFactory.Create(new AppSettings { CoverPosition = position }, 1);
        Assert.Contains($"\"coverPosition\":\"{expected}\"", script);
    }

}
