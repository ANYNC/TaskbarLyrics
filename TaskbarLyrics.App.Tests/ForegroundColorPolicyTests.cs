using System.Windows.Media;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class ForegroundColorPolicyTests
{
    [Theory]
    [InlineData(255, 153)]
    [InlineData(128, 76)]
    public void CreateSecondaryColorPreservesPrimaryRgbAndScalesAlphaByByteTruncated60Percent(
        int primaryAlpha,
        int expectedSecondaryAlpha)
    {
        var primary = Color.FromArgb((byte)primaryAlpha, 12, 34, 56);

        var secondary = ForegroundColorPolicy.CreateSecondaryColor(primary);

        Assert.Equal((byte)12, primary.R);
        Assert.Equal((byte)34, primary.G);
        Assert.Equal((byte)56, primary.B);
        Assert.Equal((byte)primaryAlpha, primary.A);
        Assert.Equal(primary.R, secondary.R);
        Assert.Equal(primary.G, secondary.G);
        Assert.Equal(primary.B, secondary.B);
        Assert.Equal((byte)expectedSecondaryAlpha, secondary.A);
    }

    [Theory]
    [InlineData(255, 178)]
    [InlineData(128, 89)]
    public void CreateTranslationColorPreservesPrimaryRgbAndScalesAlphaByByteTruncated70Percent(
        int primaryAlpha,
        int expectedTranslationAlpha)
    {
        var primary = Color.FromArgb((byte)primaryAlpha, 12, 34, 56);

        var translation = ForegroundColorPolicy.CreateTranslationColor(primary);

        Assert.Equal(primary.R, translation.R);
        Assert.Equal(primary.G, translation.G);
        Assert.Equal(primary.B, translation.B);
        Assert.Equal((byte)expectedTranslationAlpha, translation.A);
    }

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
