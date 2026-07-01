using System.IO;
using Media = System.Windows.Media;

namespace TaskbarLyrics.Light.App;

internal static class BundledFontRegistrar
{
    public const string FamilyName = "Source Han Sans SC";

    public static Media.FontFamily BundledFamily { get; private set; } =
        new(AppSettings.DefaultFontFamily);

    public static void Register()
    {
        var fontsDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        var regularPath = Path.Combine(fontsDir, "SourceHanSansSC-Regular.otf");
        var boldPath = Path.Combine(fontsDir, "SourceHanSansSC-Bold.otf");
        if (!File.Exists(regularPath))
        {
            return;
        }

        var baseUri = new Uri(fontsDir + Path.DirectorySeparatorChar, UriKind.Absolute);
        BundledFamily = new Media.FontFamily(baseUri, "./SourceHanSansSC-Regular.otf#" + FamilyName);

        if (File.Exists(boldPath))
        {
            _ = new Media.FontFamily(baseUri, "./SourceHanSansSC-Bold.otf#" + FamilyName);
        }
    }

    public static string ResolveFontFamily(string? configuredFamily)
    {
        if (string.IsNullOrWhiteSpace(configuredFamily))
        {
            return AppSettings.DefaultFontFamily;
        }

        foreach (var candidate in configuredFamily.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (candidate.Contains(FamilyName, StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains("思源黑体", StringComparison.Ordinal))
            {
                return AppSettings.DefaultFontFamily;
            }
        }

        return configuredFamily;
    }
}
