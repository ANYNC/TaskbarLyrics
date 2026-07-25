using System.Globalization;
using System.Windows.Markup;
using Media = System.Windows.Media;

namespace TaskbarLyrics.App;

internal sealed class FontCatalogOption
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

internal static class FontCatalogService
{
    public static IReadOnlyList<FontCatalogOption> GetOptions()
    {
        var fonts = Media.Fonts.SystemFontFamilies
            .Select(fontFamily => new FontCatalogOption
            {
                Value = fontFamily.Source,
                Label = GetLocalizedName(fontFamily)
            })
            .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (!fonts.Any(option => string.Equals(
                option.Value,
                AppSettings.BundledFontFamily,
                StringComparison.OrdinalIgnoreCase)))
        {
            fonts.Insert(0, new FontCatalogOption
            {
                Value = AppSettings.BundledFontFamily,
                Label = $"{AppSettings.BundledFontFamily} (内置)"
            });
        }

        return fonts;
    }

    public static string? ResolveInstalledFamily(string? fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            return null;
        }

        var fonts = GetOptions();
        var byValue = fonts.ToDictionary(option => option.Value, option => option.Value, StringComparer.OrdinalIgnoreCase);
        var byLabel = fonts
            .GroupBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in fontFamily.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(candidate, AppSettings.BundledFontFamily, StringComparison.OrdinalIgnoreCase))
            {
                return AppSettings.BundledFontFamily;
            }

            if (byValue.TryGetValue(candidate, out var value) ||
                byLabel.TryGetValue(candidate, out value))
            {
                return value;
            }
        }

        return null;
    }

    private static string GetLocalizedName(Media.FontFamily fontFamily)
    {
        var languages = new[]
        {
            XmlLanguage.GetLanguage("zh-CN"),
            XmlLanguage.GetLanguage("zh-Hans"),
            XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag),
            XmlLanguage.GetLanguage("en-US")
        };

        foreach (var language in languages)
        {
            if (fontFamily.FamilyNames.TryGetValue(language, out var name) &&
                !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return fontFamily.FamilyNames.Values.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
            ?? fontFamily.Source;
    }
}
