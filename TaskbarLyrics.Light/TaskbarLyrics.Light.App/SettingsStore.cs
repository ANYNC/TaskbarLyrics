using System.IO;
using System.Text.Json;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Light.App;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"读取设置失败 '{_filePath}': {exception.Message}");
            return new AppSettings();
        }
    }

    public bool Save(AppSettings settings)
    {
        string? temporaryPath = null;
        try
        {
            NormalizeCurrentSettings(settings);
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var settingsDirectory = string.IsNullOrWhiteSpace(directory)
                ? AppContext.BaseDirectory
                : directory;
            temporaryPath = Path.Combine(
                settingsDirectory,
                $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
            var serializedSettings = JsonSerializer.SerializeToUtf8Bytes(settings, SerializerOptions);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(serializedSettings);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
            temporaryPath = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            Log.Error($"保存设置失败 '{_filePath}': {exception}");
            return false;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Log.Warn($"清理设置临时文件失败 '{temporaryPath}': {exception.Message}");
                }
            }
        }
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

        if (!json.Contains("\"TextEffectStyle\"", StringComparison.Ordinal) &&
            json.Contains("\"ShowTextShadow\"", StringComparison.Ordinal))
        {
            settings.TextEffectStyle = settings.ShowTextShadow
                ? TextEffectStyle.Shadow
                : TextEffectStyle.None;
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
        NormalizeTextEffectSettings(settings);

        EnsurePlayerVisualProfile(settings, "QQMusic");
        EnsurePlayerVisualProfile(settings, "Netease");
        EnsurePlayerVisualProfile(settings, "Kugou");
        EnsurePlayerVisualProfile(settings, "Spotify");
    }

    private static void NormalizeCurrentSettings(AppSettings settings)
    {
        settings.TransitionStyle = AppSettings.NormalizeTransitionStyle(settings.TransitionStyle);
        settings.SongProgressStyle = AppSettings.NormalizeSongProgressStyle(settings.SongProgressStyle);
        NormalizeTextEffectSettings(settings);
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

    private static void NormalizeTextEffectSettings(AppSettings settings)
    {
        if (!Enum.IsDefined(settings.TextEffectStyle))
        {
            settings.TextEffectStyle = settings.ShowTextShadow
                ? TextEffectStyle.Glow
                : TextEffectStyle.None;
        }

        settings.ShowTextShadow = settings.TextEffectStyle != TextEffectStyle.None;
        settings.CoverGlowOpacity = NormalizeOpacity(settings.CoverGlowOpacity, 0.5);
        settings.TextGlowOpacity = NormalizeOpacity(settings.TextGlowOpacity, 0.5);
    }

    private static double NormalizeOpacity(double value, double fallback)
    {
        return double.IsFinite(value)
            ? Math.Clamp(value, 0, 1)
            : fallback;
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
