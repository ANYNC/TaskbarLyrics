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
            NormalizeCurrentSettings(settings);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        NormalizeCurrentSettings(settings);
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

        if (!json.Contains("\"AnimationIntensity\"", StringComparison.Ordinal))
        {
            settings.AnimationIntensity = AnimationIntensity.Smooth;
        }

        if (!json.Contains("\"TranslationOpacity\"", StringComparison.Ordinal))
        {
            settings.TranslationOpacity = 1;
        }

        if (settings.CoverStyle == CoverDisplayStyle.Large)
        {
            settings.CoverStyle = CoverDisplayStyle.RoundedSquare;
        }

        NormalizeSongProgressColorSettings(settings);

        EnsurePlayerVisualProfile(settings, "QQMusic");
        EnsurePlayerVisualProfile(settings, "Netease");
        EnsurePlayerVisualProfile(settings, "Kugou");
        EnsurePlayerVisualProfile(settings, "Spotify");
    }

    private static void NormalizeCurrentSettings(AppSettings settings)
    {
        settings.TransitionStyle = AppSettings.NormalizeTransitionStyle(settings.TransitionStyle);
        settings.SongProgressStyle = AppSettings.NormalizeSongProgressStyle(settings.SongProgressStyle);
        if (settings.PlayerVisualProfiles is null)
        {
            return;
        }

        foreach (var profile in settings.PlayerVisualProfiles.Values)
        {
            if (profile is not null)
            {
                profile.SongProgressStyle = AppSettings.NormalizeSongProgressStyle(profile.SongProgressStyle);
            }
        }
    }

    private static void NormalizeSongProgressColorSettings(AppSettings settings)
    {
        switch (settings.SongProgressColorMode)
        {
            case SongProgressColorMode.White:
                settings.SongProgressColor = "#FFFFFFFF";
                settings.SongProgressColorMode = SongProgressColorMode.Custom;
                break;
            case SongProgressColorMode.Blue:
                settings.SongProgressColor = "#FF60A5FA";
                settings.SongProgressColorMode = SongProgressColorMode.Custom;
                break;
            case SongProgressColorMode.Cyan:
                settings.SongProgressColor = "#FF22D3EE";
                settings.SongProgressColorMode = SongProgressColorMode.Custom;
                break;
            case SongProgressColorMode.Green:
                settings.SongProgressColor = "#FF34D399";
                settings.SongProgressColorMode = SongProgressColorMode.Custom;
                break;
            case SongProgressColorMode.Orange:
                settings.SongProgressColor = "#FFFB923C";
                settings.SongProgressColorMode = SongProgressColorMode.Custom;
                break;
            case SongProgressColorMode.Pink:
                settings.SongProgressColor = "#FFF472B6";
                settings.SongProgressColorMode = SongProgressColorMode.Custom;
                break;
            case SongProgressColorMode.Purple:
                settings.SongProgressColor = "#FFA78BFA";
                settings.SongProgressColorMode = SongProgressColorMode.Custom;
                break;
        }

        if (settings.SongProgressColorMode != SongProgressColorMode.Text &&
            settings.SongProgressColorMode != SongProgressColorMode.CoverAccent &&
            settings.SongProgressColorMode != SongProgressColorMode.Custom)
        {
            settings.SongProgressColorMode = SongProgressColorMode.Text;
        }

        if (string.IsNullOrWhiteSpace(settings.SongProgressColor))
        {
            settings.SongProgressColor = "#FFFFFFFF";
        }
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
