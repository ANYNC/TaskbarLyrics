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

    [Theory]
    [InlineData(420, 1920, 420)]
    [InlineData(100, 1920, 100)]
    [InlineData(1400, 1000, 1000)]
    [InlineData(5000, 1920, 1400)]
    [InlineData(420, 50, 50)]
    [InlineData(420, 0, 420)]
    [InlineData(double.NaN, 1920, AppSettings.DefaultWindowWidth)]
    public void ClampEffectiveWindowWidthUsesTheIndependentWidthAndAvailableSpace(double width, double available, double expected)
    {
        Assert.Equal(expected, AppSettings.ClampEffectiveWindowWidth(width, available));
    }

    [Theory]
    [InlineData(false, 25)]
    [InlineData(false, 150)]
    [InlineData(false, 300)]
    [InlineData(true, 25)]
    [InlineData(true, 150)]
    [InlineData(true, 300)]
    public void BothPresentationModesKeepTheSameWidthAtEveryLayoutScale(bool floating, double scale)
    {
        var settings = new AppSettings { UseFloatingWindow = floating, WindowWidth = 420, LyricsLayoutScalePercent = scale };
        Assert.Equal(420, AppSettings.CalculateEffectiveWindowWidth(settings.WindowWidth));
        Assert.Equal(420, AppSettings.ClampEffectiveWindowWidth(settings.WindowWidth, 1920));
        Assert.Equal(scale, LyricsLayoutMetrics.Create(settings).ScalePercent);
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
