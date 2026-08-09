namespace TaskbarLyrics.App;

internal static class ForegroundColorPolicy
{
    private const double SecondaryTextOpacityRatio = 0.60;
    private const double TranslationTextOpacityRatio = 0.70;

    public static bool ApplyStartup(AppSettings settings, bool systemUsesLightTheme)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (IsLegacyCustomForeground(settings.ForegroundColor))
        {
            settings.ForegroundColorMode = ForegroundColorMode.Custom;
            return false;
        }

        if (!Enum.IsDefined(settings.ForegroundColorMode))
        {
            settings.ForegroundColorMode = ForegroundColorMode.System;
        }

        return ApplySelectedMode(settings, systemUsesLightTheme);
    }

    public static bool ApplySystemTheme(AppSettings settings, bool systemUsesLightTheme)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.ForegroundColorMode == ForegroundColorMode.System &&
            ApplySelectedMode(settings, systemUsesLightTheme);
    }

    public static bool ApplySelectedMode(AppSettings settings, bool systemUsesLightTheme)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var nextColor = settings.ForegroundColorMode switch
        {
            ForegroundColorMode.Dark => AppSettings.DarkForegroundColor,
            ForegroundColorMode.Light => AppSettings.LightForegroundColor,
            ForegroundColorMode.System => systemUsesLightTheme
                ? AppSettings.DarkForegroundColor
                : AppSettings.LightForegroundColor,
            _ => null
        };
        if (nextColor is null ||
            string.Equals(settings.ForegroundColor, nextColor, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        settings.ForegroundColor = nextColor;
        return true;
    }

    public static System.Windows.Media.Color CreateSecondaryColor(System.Windows.Media.Color primaryColor)
    {
        return CreateAlphaScaledColor(primaryColor, SecondaryTextOpacityRatio);
    }

    public static System.Windows.Media.Color CreateTranslationColor(System.Windows.Media.Color primaryColor)
    {
        return CreateAlphaScaledColor(primaryColor, TranslationTextOpacityRatio);
    }

    private static System.Windows.Media.Color CreateAlphaScaledColor(
        System.Windows.Media.Color primaryColor,
        double opacityRatio)
    {
        return System.Windows.Media.Color.FromArgb(
            (byte)Math.Clamp((int)(primaryColor.A * opacityRatio), 0, 255),
            primaryColor.R,
            primaryColor.G,
            primaryColor.B);
    }

    private static bool IsLegacyCustomForeground(string? color)
    {
        var normalized = NormalizeColor(color);
        return !string.Equals(normalized, AppSettings.DarkForegroundColor, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalized, AppSettings.LightForegroundColor, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return AppSettings.LightForegroundColor;
        }

        var trimmed = color.Trim();
        return trimmed.Length == 7 && trimmed.StartsWith('#')
            ? $"#FF{trimmed[1..]}"
            : trimmed;
    }
}
