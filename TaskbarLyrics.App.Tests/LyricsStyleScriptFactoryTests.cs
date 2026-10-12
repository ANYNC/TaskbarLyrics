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
    public void CreateEmitsFloatingWindowSurfaceAndFreeDragForFloatingModeOnly()
    {
        var settings = new AppSettings
        {
            UseFloatingWindow = true,
            ShowBackground = true,
            BackgroundTone = 90,
            ShowBorder = true,
            FloatingWindowBackground = new FloatingWindowBackgroundSettings
            {
                ShowBackground = false,
                ShowBorder = false
            }
        };

        var floatingScript = LyricsStyleScriptFactory.Create(settings, pixelsPerDip: 1);

        Assert.Contains("\"surfaceColor\":\"transparent\"", floatingScript);
        Assert.Contains("\"surfaceShadow\":\"none\"", floatingScript);
        Assert.Contains("\"freeDrag\":true", floatingScript);

        settings.UseFloatingWindow = false;
        var embeddedScript = LyricsStyleScriptFactory.Create(settings, pixelsPerDip: 1);

        Assert.Contains("\"surfaceColor\":\"rgba(0, 0, 0, 0.9)\"", embeddedScript);
        Assert.Contains("\"surfaceShadow\":\"inset 0 0 0 1px rgba(255, 255, 255, 0.16)\"", embeddedScript);
        Assert.Contains("\"freeDrag\":false", embeddedScript);
    }

    [Theory]
    [InlineData(0, "transparent")]
    [InlineData(60, "rgba(0, 0, 0, 0.6)")]
    [InlineData(100, "rgba(0, 0, 0, 1)")]
    [InlineData(-35, "rgba(255, 255, 255, 0.35)")]
    [InlineData(-100, "rgba(255, 255, 255, 1)")]
    public void CreateRendersTheSignedBackgroundTone(double tone, string expected)
    {
        var settings = new AppSettings { ShowBackground = true, BackgroundTone = tone };

        var script = LyricsStyleScriptFactory.Create(settings, pixelsPerDip: 1);

        Assert.Contains($"\"surfaceColor\":\"{expected}\"", script);
    }

    [Fact]
    public void CreateUsesFloatingWindowSurfaceValuesWhileFloating()
    {
        var settings = new AppSettings
        {
            UseFloatingWindow = true,
            FloatingWindowBackground = new FloatingWindowBackgroundSettings
            {
                ShowBackground = true,
                Tone = -25,
                ShowBorder = true
            }
        };

        var script = LyricsStyleScriptFactory.Create(settings, pixelsPerDip: 1);

        Assert.Contains("\"surfaceColor\":\"rgba(255, 255, 255, 0.25)\"", script);
        Assert.Contains("\"freeDrag\":true", script);
    }

    [Fact]
    public void CreateDisablesFreeDragWhileClickThroughIsOn()
    {
        var settings = new AppSettings
        {
            UseFloatingWindow = true,
            FloatingWindowFreeDrag = true,
            FloatingWindowClickThrough = true
        };

        var script = LyricsStyleScriptFactory.Create(settings, pixelsPerDip: 1);

        Assert.Contains("\"freeDrag\":false", script);
    }

    [Theory]
    [InlineData(60, "rgba(255, 255, 255, 0.898)")]
    [InlineData(-60, "rgba(17, 24, 39, 0.898)")]
    public void CreateDerivesFloatingWindowTextColorFromTheSurfaceTone(double tone, string expected)
    {
        var settings = new AppSettings
        {
            UseFloatingWindow = true,
            ForegroundColor = "#FF00FF00",
            FloatingWindowBackground = new FloatingWindowBackgroundSettings
            {
                ShowBackground = true,
                Tone = tone
            }
        };

        var script = LyricsStyleScriptFactory.Create(settings, pixelsPerDip: 1);

        Assert.Contains($"\"primaryColor\":\"{expected}\"", script);
    }

    [Theory]
    [InlineData(true, false, 60)]
    [InlineData(true, true, 0)]
    [InlineData(false, true, 60)]
    public void CreateKeepsTheTextStyleColorWithoutAFloatingSurface(
        bool floating,
        bool showBackground,
        double tone)
    {
        var settings = new AppSettings
        {
            UseFloatingWindow = floating,
            ForegroundColor = "#FF00FF00",
            FloatingWindowBackground = new FloatingWindowBackgroundSettings
            {
                ShowBackground = showBackground,
                Tone = tone
            }
        };

        var script = LyricsStyleScriptFactory.Create(settings, pixelsPerDip: 1);

        Assert.Contains("\"primaryColor\":\"rgba(0, 255, 0, 0.898)\"", script);
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
