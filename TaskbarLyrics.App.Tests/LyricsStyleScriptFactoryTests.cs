using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class LyricsStyleScriptFactoryTests
{
    [Theory]
    [InlineData(1, "1")]
    [InlineData(1.5, "1.5")]
    [InlineData(2, "2")]
    [InlineData(0, "1")]
    [InlineData(double.NaN, "1")]
    public void CreateIncludesHostDpiWithoutApplyingTextScaleToStoredSizes(double dpi, string expected)
    {
        var settings = new AppSettings { FontSize = 14, CoverSize = 34 };
        var script = LyricsStyleScriptFactory.Create(settings, dpi);
        Assert.Contains($"\"hostPixelsPerDip\":{expected}", script);
        Assert.Contains("\"fontSize\":14", script);
        Assert.Equal(14, settings.FontSize);
        Assert.Equal(34, settings.CoverSize);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SearchStatusPreferenceIsSharedAcrossWindowModes(bool floating, bool enabled)
    {
        var settings = new AppSettings { UseFloatingWindow = floating, ShowLyricSearchStatus = enabled };
        var script = LyricsStyleScriptFactory.Create(settings, 1);
        Assert.Contains($"\"showLyricSearchStatus\":{enabled.ToString().ToLowerInvariant()}", script);
    }

    [Theory]
    [InlineData(false, false, "none")]
    [InlineData(true, false, "none")]
    [InlineData(false, true, "0 1px 3px rgba(0, 0, 0, 0.2)")]
    [InlineData(true, true, "0 1px 3px rgba(0, 0, 0, 0.2)")]
    public void CreatePreservesShadowSwitchWithSharedBlur(bool floating, bool enabled, string expected)
    {
        var settings = new AppSettings { UseFloatingWindow = floating, ShowTextShadow = enabled };
        var script = LyricsStyleScriptFactory.Create(settings, 1);
        Assert.Contains($"\"textShadow\":\"{expected}\"", script);
    }

    [Theory]
    [InlineData(false, false, "null")]
    [InlineData(true, false, "40")]
    [InlineData(true, true, "null")]
    public void CreateEmitsIndependentFloatingDoubleLineHeight(bool floating, bool singleLine, string expected)
    {
        var settings = new AppSettings { UseFloatingWindow = floating, SingleLineLyrics = singleLine };
        var script = LyricsStyleScriptFactory.Create(settings, 1);
        Assert.Contains($"\"lyricsPaneHeight\":{expected}", script);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void FloatingInputPassthroughFollowsPlaybackControlWithoutDisablingCoverInteraction(bool floating, bool playback, bool expected)
    {
        var settings = new AppSettings
        {
            UseFloatingWindow = floating,
            EnableFloatingControlPanel = playback
        };
        var script = LyricsStyleScriptFactory.Create(settings, 1);
        Assert.Contains($"\"floatingInputPassthrough\":{expected.ToString().ToLowerInvariant()}", script);
        if (floating) Assert.Contains("\"enableControlPanel\":true", script);
        Assert.Equal(playback, settings.EnableFloatingControlPanel);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    public void CreateEmitsControlPanelAvailabilityAndFloatingMode(bool enabled, bool floating, bool expected)
    {
        var settings = new AppSettings { EnableControlPanel = enabled, UseFloatingWindow = floating };
        var script = LyricsStyleScriptFactory.Create(settings, 1);

        Assert.Contains($"\"enableControlPanel\":{expected.ToString().ToLowerInvariant()}", script);
        Assert.Equal(enabled, settings.EnableControlPanel);
        Assert.Contains($"\"floatingControlPanel\":{floating.ToString().ToLowerInvariant()}", script);
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
        Assert.Contains("\"layoutHorizontalPadding\":0", script);
    }

}
