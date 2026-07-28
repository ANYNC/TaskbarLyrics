using System.IO;
using System.Text.Json;

using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

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
            using var document = JsonDocument.Parse(json);
            MigrateLegacySpectrumSettings(document.RootElement, settings);
            settings.NormalizePlayerSources();
            settings.NormalizeLyricsLayout();
            return settings;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"Failed to load settings from '{_filePath}': {exception.Message}");
            return new AppSettings();
        }
    }

    public bool Save(AppSettings settings)
    {
        string? temporaryPath = null;
        try
        {
            settings.NormalizePlayerSources();
            settings.NormalizeLyricsLayout();
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
            Log.Error($"Failed to save settings to '{_filePath}': {exception}");
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
                    Log.Warn($"Failed to remove temporary settings file '{temporaryPath}': {exception.Message}");
                }
            }
        }
    }

    private static void MigrateLegacySpectrumSettings(JsonElement root, AppSettings settings)
    {
        if (!root.TryGetProperty("EnableSpectrum", out var enabledElement) ||
            enabledElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            return;
        }

        if (!enabledElement.GetBoolean())
        {
            settings.SpectrumDisplayMode = SpectrumDisplayMode.Disabled;
        }
    }
}
