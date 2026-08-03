using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class ForegroundColorPolicyTests
{
    [Theory]
    [InlineData(ForegroundColorMode.Dark, AppSettings.DarkForegroundColor)]
    [InlineData(ForegroundColorMode.Light, AppSettings.LightForegroundColor)]
    public void ApplyStartupKeepsFixedModeAndRestoresItsCanonicalColor(
        ForegroundColorMode mode,
        string expectedColor)
    {
        var settings = new AppSettings
        {
            ForegroundColorMode = mode,
            ForegroundColor = mode == ForegroundColorMode.Dark
                ? AppSettings.LightForegroundColor
                : AppSettings.DarkForegroundColor
        };

        ForegroundColorPolicy.ApplyStartup(settings, systemUsesLightTheme: mode == ForegroundColorMode.Light);

        Assert.Equal(mode, settings.ForegroundColorMode);
        Assert.Equal(expectedColor, settings.ForegroundColor);
    }

    [Fact]
    public void ApplyStartupPreservesCustomColor()
    {
        var settings = new AppSettings
        {
            ForegroundColorMode = ForegroundColorMode.Custom,
            ForegroundColor = "#FF336699"
        };

        ForegroundColorPolicy.ApplyStartup(settings, systemUsesLightTheme: true);

        Assert.Equal(ForegroundColorMode.Custom, settings.ForegroundColorMode);
        Assert.Equal("#FF336699", settings.ForegroundColor);
    }

    [Theory]
    [InlineData(true, AppSettings.DarkForegroundColor)]
    [InlineData(false, AppSettings.LightForegroundColor)]
    public void ApplyStartupResolvesSystemModeWithoutReplacingThePreference(
        bool systemUsesLightTheme,
        string expectedColor)
    {
        var settings = new AppSettings
        {
            ForegroundColorMode = ForegroundColorMode.System,
            ForegroundColor = systemUsesLightTheme
                ? AppSettings.LightForegroundColor
                : AppSettings.DarkForegroundColor
        };

        ForegroundColorPolicy.ApplyStartup(settings, systemUsesLightTheme);

        Assert.Equal(ForegroundColorMode.System, settings.ForegroundColorMode);
        Assert.Equal(expectedColor, settings.ForegroundColor);
    }

    [Theory]
    [InlineData(ForegroundColorMode.Dark, AppSettings.DarkForegroundColor, false)]
    [InlineData(ForegroundColorMode.Light, AppSettings.LightForegroundColor, true)]
    [InlineData(ForegroundColorMode.Custom, "#FF336699", true)]
    public void ApplySystemThemeDoesNotChangeManualModes(
        ForegroundColorMode mode,
        string color,
        bool systemUsesLightTheme)
    {
        var settings = new AppSettings
        {
            ForegroundColorMode = mode,
            ForegroundColor = color
        };

        var changed = ForegroundColorPolicy.ApplySystemTheme(settings, systemUsesLightTheme);

        Assert.False(changed);
        Assert.Equal(mode, settings.ForegroundColorMode);
        Assert.Equal(color, settings.ForegroundColor);
    }

    [Fact]
    public void ApplySystemThemeUpdatesOnlyTheEffectiveColorForSystemMode()
    {
        var settings = new AppSettings
        {
            ForegroundColorMode = ForegroundColorMode.System,
            ForegroundColor = AppSettings.DarkForegroundColor
        };

        var changed = ForegroundColorPolicy.ApplySystemTheme(settings, systemUsesLightTheme: false);

        Assert.True(changed);
        Assert.Equal(ForegroundColorMode.System, settings.ForegroundColorMode);
        Assert.Equal(AppSettings.LightForegroundColor, settings.ForegroundColor);
    }

    [Fact]
    public void ApplyStartupMigratesLegacyCustomColorWithoutReplacingIt()
    {
        var settings = new AppSettings
        {
            ForegroundColorMode = ForegroundColorMode.System,
            ForegroundColor = "#FF336699"
        };

        ForegroundColorPolicy.ApplyStartup(settings, systemUsesLightTheme: true);

        Assert.Equal(ForegroundColorMode.Custom, settings.ForegroundColorMode);
        Assert.Equal("#FF336699", settings.ForegroundColor);
    }

    [Fact]
    public void ApplyStartupNormalizesUnknownModeToSystem()
    {
        var settings = new AppSettings
        {
            ForegroundColorMode = (ForegroundColorMode)99,
            ForegroundColor = AppSettings.LightForegroundColor
        };

        ForegroundColorPolicy.ApplyStartup(settings, systemUsesLightTheme: true);

        Assert.Equal(ForegroundColorMode.System, settings.ForegroundColorMode);
        Assert.Equal(AppSettings.DarkForegroundColor, settings.ForegroundColor);
    }
}
