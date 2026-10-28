using System.Globalization;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Markup;
using YamlDotNet.Serialization;

namespace TaskbarLyrics.App;

/// <summary>Process-wide UI language, following the launcher's system-language policy.
/// Semantic resource keys are returned when translations are missing.
/// </summary>
public static class Text
{
    private const string LanguageNameKey = "LanguageName";

    private static readonly ConcurrentDictionary<string, byte> MissingKeys = new();
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Catalog = new(() =>
        new DeserializerBuilder().Build().Deserialize<Dictionary<string, Dictionary<string, string>>>(ReadResource("text.yaml")));

    private static string _currentLanguage = DetectSystemLanguage(CultureInfo.CurrentUICulture);

    public static string CurrentLanguage => _currentLanguage;

    /// <summary>UI languages in text.yaml section order, named in each language itself.</summary>
    public static IReadOnlyList<UiLanguage> Languages { get; } = ReadLanguages();

    /// <summary>Applies a persisted interface-language preference; empty or unknown values reuse system detection.</summary>
    public static void ApplyPreference(string? preferredLanguage) =>
        _currentLanguage = ResolveLanguage(preferredLanguage);

    public static string ResolveLanguage(string? preferredLanguage) =>
        NormalizeLanguagePreference(preferredLanguage) is { Length: > 0 } language
            ? language
            : DetectSystemLanguage(CultureInfo.CurrentUICulture);

    public static string NormalizeLanguagePreference(string? preferredLanguage) =>
        preferredLanguage is not null &&
        Languages.Any(language => string.Equals(language.Id, preferredLanguage, StringComparison.Ordinal))
            ? preferredLanguage
            : string.Empty;

    public static string DetectSystemLanguage(CultureInfo culture)
    {
        // Windows reports Traditional Chinese as zh-Hant-HK/MO/TW (or the legacy zh-CHT). They deliberately share
        // the generic zh-Hant catalog instead of per-region resources; Hong Kong and Macau wording differences are
        // accepted for now.
        for (var current = culture; current.Name.Length > 0; current = current.Parent)
        {
            if (IsTraditionalChinese(current.Name))
            {
                return "Zh-Hant";
            }

            if (ReferenceEquals(current, current.Parent))
            {
                break;
            }
        }

        return culture.Name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ? "Zh-CN" : "EN";
    }

    internal static bool IsTraditionalChinese(string name) =>
        name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
        || name.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
        || name.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
        || name.Equals("zh-MO", StringComparison.OrdinalIgnoreCase)
        || name.Equals("zh-CHT", StringComparison.OrdinalIgnoreCase);

    public static string T(string key)
    {
        var value = Translate(key, CurrentLanguage);
        if (value == key && MissingKeys.TryAdd(key, 0))
            TaskbarLyrics.Core.Utilities.Log.Warn($"Missing UI resource: {CurrentLanguage}/{key}");
        return value;
    }

    internal static string Translate(string source, string language) =>
        Catalog.Value.TryGetValue(language, out var strings) && strings.TryGetValue(source, out var value) ? value : source;

    public static string Format(string source, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, T(source), values);

    internal static string CreateWebBootstrap(string? language = null)
    {
        language ??= CurrentLanguage;
        // Default JSON escaping also keeps translations safe inside the inline lyric script.
        var configuration = JsonSerializer.Serialize(new
        {
            language,
            strings = Catalog.Value.TryGetValue(language, out var strings) ? strings : new Dictionary<string, string>()
        });
        return $"window.taskbarLyricsLocale = {configuration};\n" + ReadResource("text.js");
    }

    private static List<UiLanguage> ReadLanguages()
    {
        var catalog = Catalog.Value;
        var languages = new List<UiLanguage>();
        foreach (var line in ReadResource("text.yaml").Split('\n'))
        {
            var match = Regex.Match(line.TrimEnd('\r'), "^([A-Za-z][A-Za-z0-9-]*):\\s*$");
            if (!match.Success ||
                !catalog.TryGetValue(match.Groups[1].Value, out var strings) ||
                !strings.TryGetValue(LanguageNameKey, out var name) ||
                string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            languages.Add(new UiLanguage(match.Groups[1].Value, name));
        }

        return languages;
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(Text).Assembly.GetManifestResourceStream($"TaskbarLyrics.App.{name}")
            ?? throw new InvalidOperationException($"Missing UI localization resource: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>A UI language declared by a text.yaml section, named in its own language.</summary>
public sealed record UiLanguage(string Id, string Name);

[MarkupExtensionReturnType(typeof(string))]
public sealed class TranslateExtension : MarkupExtension
{
    public string Text { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        global::TaskbarLyrics.App.Text.T(Text);
}
