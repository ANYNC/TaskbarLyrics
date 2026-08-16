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
    public void NewSettingsShowLyricsOnAllDisplaysByDefault()
    {
        var settings = new AppSettings();

        Assert.Equal(LyricsDisplayMode.All, settings.LyricsDisplayMode);
        Assert.Empty(settings.SelectedDisplayIds);
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
        Assert.Equal(AppSettings.MinimumWindowWidth, AppSettings.ClampEffectiveWindowWidth(420, 25, 1920));
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
}
