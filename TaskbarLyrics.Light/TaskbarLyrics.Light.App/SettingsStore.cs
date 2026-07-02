using System.IO;
using System.Text.Json;

namespace TaskbarLyrics.Light.App;

public sealed class SettingsStore
{
    private readonly string _filePath;

    public SettingsStore(string filePath)
    {
        _filePath = filePath;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            ApplyLegacyDefaults(json, settings);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(_filePath, json);
    }

    private static void ApplyLegacyDefaults(string json, AppSettings settings)
    {
        if (!json.Contains("\"StartWithWindows\"", StringComparison.Ordinal))
        {
            settings.StartWithWindows = true;
        }

        if (!json.Contains("\"AutoShowLyricsWhenPlayerOpens\"", StringComparison.Ordinal))
        {
            settings.AutoShowLyricsWhenPlayerOpens = true;
        }

        if (!json.Contains("\"AutoHideLyricsWhenPlayerCloses\"", StringComparison.Ordinal))
        {
            settings.AutoHideLyricsWhenPlayerCloses = true;
        }

        if (!json.Contains("\"FontFamily\"", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(settings.FontFamily))
        {
            settings.FontFamily = AppSettings.DefaultFontFamily;
        }

        if (!json.Contains("\"CoverSize\"", StringComparison.Ordinal))
        {
            settings.CoverSize = settings.CoverStyle == CoverDisplayStyle.Large ? 42 : 34;
        }

        if (settings.CoverStyle == CoverDisplayStyle.Large)
        {
            settings.CoverStyle = CoverDisplayStyle.RoundedSquare;
        }

        EnsurePlayerVisualProfile(settings, "QQMusic");
        EnsurePlayerVisualProfile(settings, "Netease");
        EnsurePlayerVisualProfile(settings, "Kugou");
        EnsurePlayerVisualProfile(settings, "Spotify");
    }

    private static void EnsurePlayerVisualProfile(AppSettings settings, string sourceApp)
    {
        settings.PlayerVisualProfiles ??= new Dictionary<string, PlayerVisualProfile>(StringComparer.OrdinalIgnoreCase);
        if (!settings.PlayerVisualProfiles.ContainsKey(sourceApp))
        {
            settings.PlayerVisualProfiles[sourceApp] = new PlayerVisualProfile();
        }
    }
}
