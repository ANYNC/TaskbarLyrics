using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void ForegroundColorModesKeepTheirPersistedNumericValues()
    {
        Assert.Equal(0, (int)ForegroundColorMode.Dark);
        Assert.Equal(1, (int)ForegroundColorMode.Light);
        Assert.Equal(2, (int)ForegroundColorMode.Custom);
        Assert.Equal(3, (int)ForegroundColorMode.System);
    }

    [Fact]
    public void NewSettingsFollowTheSystemForegroundByDefault()
    {
        Assert.Equal(ForegroundColorMode.System, new AppSettings().ForegroundColorMode);
    }

    [Fact]
    public void NewSettingsDisableSpectrumAndDoNotGrantAudioAccess()
    {
        var settings = new AppSettings();

        Assert.Equal(SpectrumDisplayMode.Disabled, settings.SpectrumDisplayMode);
        Assert.False(settings.SpectrumAudioAccessGranted);
    }

    [Fact]
    public void NewSettingsUseTheDefaultSpectrumFrequencyRange()
    {
        var tuning = new AppSettings().SpectrumTuning;

        Assert.Equal(28, tuning.MinFrequency);
        Assert.Equal(4000, tuning.MaxFrequency);
    }

    [Fact]
    public void NewSettingsShowLyricsOnAllDisplaysByDefault()
    {
        var settings = new AppSettings();

        Assert.Equal(LyricsDisplayMode.All, settings.LyricsDisplayMode);
        Assert.Empty(settings.SelectedDisplayIds);
    }

    [Fact]
    public void NewSettingsUseTaskbarEmbeddingByDefault()
    {
        Assert.False(new AppSettings().UseFloatingWindow);
    }

    [Fact]
    public void NewSettingsUseLeftLyricsTextAlignmentByDefault()
    {
        Assert.Equal(LyricsTextAlignment.Left, new AppSettings().LyricsTextAlignment);
    }

    [Fact]
    public void NormalizeLyricsTextAlignmentFallsBackToLeftForUndefinedValues()
    {
        var settings = new AppSettings
        {
            LyricsTextAlignment = (LyricsTextAlignment)999
        };

        settings.NormalizeLyricsTextAlignment();

        Assert.Equal(LyricsTextAlignment.Left, settings.LyricsTextAlignment);
    }

    [Fact]
    public void NewSettingsAutoHideWhenNoPlaybackByDefault()
    {
        Assert.True(new AppSettings().AutoHideWhenNoPlayback);
    }

    [Fact]
    public void NormalizeDisplaySelectionRemovesBlankAndDuplicateIds()
    {
        var settings = new AppSettings
        {
            SelectedDisplayIds = [" display-a ", "DISPLAY-A", "", "display-b"]
        };

        settings.NormalizeDisplaySelection();

        Assert.Equal(["display-a", "display-b"], settings.SelectedDisplayIds);
    }

    [Fact]
    public void NormalizeDisplaySelectionRestoresUnknownModeToAllDisplays()
    {
        var settings = new AppSettings
        {
            LyricsDisplayMode = (LyricsDisplayMode)999
        };

        settings.NormalizeDisplaySelection();

        Assert.Equal(LyricsDisplayMode.All, settings.LyricsDisplayMode);
    }

    [Fact]
    public void NewSettingsKeepFreeDragOnAndClickThroughOffByDefault()
    {
        var settings = new AppSettings();

        Assert.True(settings.FloatingWindowFreeDrag);
        Assert.False(settings.FloatingWindowClickThrough);
        Assert.False(settings.FallbackToFloatingWhenTaskbarHidden);
    }

    [Fact]
    public void EnablingFreeDragClearsClickThrough()
    {
        var settings = new AppSettings
        {
            FloatingWindowFreeDrag = false,
            FloatingWindowClickThrough = true
        };

        settings.SetFloatingWindowFreeDrag(true);

        Assert.True(settings.FloatingWindowFreeDrag);
        Assert.False(settings.FloatingWindowClickThrough);
    }

    [Fact]
    public void EnablingClickThroughClearsFreeDrag()
    {
        var settings = new AppSettings
        {
            FloatingWindowFreeDrag = true,
            FloatingWindowClickThrough = false
        };

        settings.SetFloatingWindowClickThrough(true);

        Assert.True(settings.FloatingWindowClickThrough);
        Assert.False(settings.FloatingWindowFreeDrag);
    }

    [Fact]
    public void DisablingAnInputModeLeavesTheOtherModeAlone()
    {
        var dragOff = new AppSettings
        {
            FloatingWindowFreeDrag = true,
            FloatingWindowClickThrough = false
        };

        dragOff.SetFloatingWindowFreeDrag(false);
        Assert.False(dragOff.FloatingWindowFreeDrag);
        Assert.False(dragOff.FloatingWindowClickThrough);

        var clickThroughOff = new AppSettings
        {
            FloatingWindowFreeDrag = false,
            FloatingWindowClickThrough = true
        };

        clickThroughOff.SetFloatingWindowClickThrough(false);
        Assert.False(clickThroughOff.FloatingWindowClickThrough);
        Assert.False(clickThroughOff.FloatingWindowFreeDrag);
    }

    [Fact]
    public void NewSettingsStartWithAnEmptyFloatingWindowSurface()
    {
        var background = new AppSettings().FloatingWindowBackground;

        Assert.False(background.ShowBackground);
        Assert.False(background.ShowBorder);
        Assert.Equal(AppSettings.DefaultBackgroundTone, background.Tone);
        Assert.Empty(new AppSettings().FloatingWindowPlacement.Displays);
    }

    [Fact]
    public void CloneKeepsFloatingWindowSurfaceAndPlacementIndependentFromSource()
    {
        var source = new AppSettings();
        source.FloatingWindowBackground.ShowBackground = true;
        source.FloatingWindowBackground.Tone = -25;
        source.FloatingWindowPlacement.Set("display-a", 120, -40);

        var clone = source.Clone();
        clone.FloatingWindowBackground.Tone = 90;
        clone.FloatingWindowPlacement.Set("display-a", 0, 0);

        Assert.True(source.FloatingWindowBackground.ShowBackground);
        Assert.Equal(-25, source.FloatingWindowBackground.Tone);
        Assert.True(source.FloatingWindowPlacement.TryGet("display-a", out var placement));
        Assert.Equal(120, placement.OffsetX);
        Assert.Equal(-40, placement.OffsetY);
    }

    [Fact]
    public void NormalizeWindowLayoutClampsFloatingWindowSurfaceAndPlacement()
    {
        var settings = new AppSettings
        {
            FloatingWindowBackground = new FloatingWindowBackgroundSettings { Tone = 400 }
        };
        settings.FloatingWindowPlacement.Set("display-a", double.PositiveInfinity, -1e9);

        settings.NormalizeWindowLayout();

        Assert.Equal(AppSettings.MaximumBackgroundTone, settings.FloatingWindowBackground.Tone);
        Assert.True(settings.FloatingWindowPlacement.TryGet("DISPLAY-A", out var placement));
        Assert.Equal(0, placement.OffsetX);
        Assert.Equal(AppSettings.MinimumFloatingWindowOffset, placement.OffsetY);
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(55, 55)]
    [InlineData(-40, -40)]
    [InlineData(-500, -100)]
    [InlineData(double.NaN, AppSettings.DefaultBackgroundTone)]
    public void NormalizeLyricsLayoutKeepsBackgroundToneInsideItsRange(double tone, double expected)
    {
        var settings = new AppSettings { BackgroundTone = tone };

        settings.NormalizeLyricsLayout();

        Assert.Equal(expected, settings.BackgroundTone);
    }

    [Theory]
    [InlineData(false, false, 60, "#FF102030")]
    [InlineData(false, true, 60, "#FF102030")]
    [InlineData(true, true, -60, AppSettings.DarkForegroundColor)]
    [InlineData(true, true, -1, AppSettings.DarkForegroundColor)]
    [InlineData(true, true, 1, AppSettings.LightForegroundColor)]
    [InlineData(true, true, 60, AppSettings.LightForegroundColor)]
    [InlineData(true, true, 0, "#FF102030")]
    [InlineData(true, false, 60, "#FF102030")]
    public void ResolveEffectiveForegroundColorFollowsTheFloatingWindowSurface(
        bool floating,
        bool showBackground,
        double tone,
        string expected)
    {
        var settings = new AppSettings
        {
            UseFloatingWindow = floating,
            ForegroundColor = "#FF102030",
            FloatingWindowBackground = new FloatingWindowBackgroundSettings
            {
                ShowBackground = showBackground,
                Tone = tone
            }
        };

        Assert.Equal(expected, settings.ResolveEffectiveForegroundColor());
    }

    [Fact]
    public void CloneKeepsDisplaySelectionIndependentFromSource()
    {
        var source = new AppSettings
        {
            LyricsDisplayMode = LyricsDisplayMode.Selected,
            SelectedDisplayIds = ["display-a"]
        };

        var clone = source.Clone();
        clone.SelectedDisplayIds.Add("display-b");

        Assert.Equal(LyricsDisplayMode.Selected, clone.LyricsDisplayMode);
        Assert.Equal(["display-a"], source.SelectedDisplayIds);
    }

    [Fact]
    public void ClampEffectiveWindowWidthReturnsBaseWidthAtFullScale()
    {
        Assert.Equal(420, AppSettings.ClampEffectiveWindowWidth(420, 100, 1920));
    }

    [Fact]
    public void ClampEffectiveWindowWidthScalesProportionallyAboveOneHundred()
    {
        Assert.Equal(840, AppSettings.ClampEffectiveWindowWidth(420, 200, 1920));
    }

    [Fact]
    public void ClampEffectiveWindowWidthClampsToMinimumWhenScaledBelowIt()
    {
        Assert.Equal(100, AppSettings.ClampEffectiveWindowWidth(100, 25, 1920));
    }

    [Fact]
    public void ClampEffectiveWindowWidthClampsToMaxWidthWhenScaleExceedsIt()
    {
        Assert.Equal(1000, AppSettings.ClampEffectiveWindowWidth(1400, 300, 1000));
    }

    [Fact]
    public void ClampEffectiveWindowWidthClampsBaseWidthToBounds()
    {
        Assert.Equal(AppSettings.MaximumWindowWidth, AppSettings.ClampEffectiveWindowWidth(5000, 100, 1920));
    }

    [Fact]
    public void ClampEffectiveWindowWidthClampsScalePercentToBounds()
    {
        Assert.Equal(1260, AppSettings.ClampEffectiveWindowWidth(420, 500, 1920));
    }

    [Fact]
    public void CalculateEffectiveWindowWidthUsesSharedBaseWidthAndScale()
    {
        Assert.Equal(630, AppSettings.CalculateEffectiveWindowWidth(420, 150));
    }

    [Fact]
    public void NormalizeWindowLayoutClampsSharedLayoutValues()
    {
        var settings = new AppSettings
        {
            WindowWidth = 5000,
            XOffset = -5000,
            YOffset = 5000
        };

        settings.NormalizeWindowLayout();

        Assert.Equal(AppSettings.MaximumWindowWidth, settings.WindowWidth);
        Assert.Equal(AppSettings.MinimumWindowOffset, settings.XOffset);
        Assert.Equal(AppSettings.MaximumWindowOffset, settings.YOffset);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(99, 100)]
    public void NormalizeWindowLayoutAllowsOneHundredPixelWidth(double input, double expected)
    {
        var settings = new AppSettings { WindowWidth = input };

        settings.NormalizeWindowLayout();

        Assert.Equal(expected, settings.WindowWidth);
    }
    [Fact]
    public void CoverPositionDefaultsToLeftAndNormalizesUndefinedValues()
    {
        Assert.Equal(CoverPosition.Left, new AppSettings().CoverPosition);
        var settings = new AppSettings { CoverPosition = (CoverPosition)999 };
        settings.NormalizeLyricsLayout();
        Assert.Equal(CoverPosition.Left, settings.CoverPosition);
    }

}
