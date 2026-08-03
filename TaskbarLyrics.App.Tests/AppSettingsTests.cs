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
