using System.IO;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void SaveReplacesTheSettingsFileWithoutLeavingTemporaryFiles()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"settings-store-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);

        try
        {
            var store = new SettingsStore(filePath);
            var settings = new AppSettings
            {
                FontSize = 28,
                ForegroundColor = "#FF336699"
            };

            store.Save(settings);

            var loaded = store.Load();
            Assert.Equal(28, loaded.FontSize);
            Assert.Equal("#FF336699", loaded.ForegroundColor);
            Assert.Empty(Directory.EnumerateFiles(directory, ".settings.json.*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadWhenSettingsJsonIsInvalidReturnsDefaultSettings()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"settings-store-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(filePath, "{ invalid json }");

            var loaded = new SettingsStore(filePath).Load();

            Assert.Equal(new AppSettings().FontSize, loaded.FontSize);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadMigratesLegacySpectrumSwitchAndSaveDropsLegacyFields()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"settings-store-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(filePath, "{\"EnableSpectrum\":false,\"EnablePureMusicSpectrum\":true,\"ShowSpectrumWhenLyricsNotFound\":false}");
            var store = new SettingsStore(filePath);

            var loaded = store.Load();
            store.Save(loaded);

            Assert.Equal(SpectrumDisplayMode.Disabled, loaded.SpectrumDisplayMode);
            var saved = File.ReadAllText(filePath);
            Assert.DoesNotContain("EnableSpectrum", saved, StringComparison.Ordinal);
            Assert.DoesNotContain("EnablePureMusicSpectrum", saved, StringComparison.Ordinal);
            Assert.DoesNotContain("ShowSpectrumWhenLyricsNotFound", saved, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void SaveWhenCalledConcurrentlyKeepsAValidSettingsFile()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"settings-store-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "settings.json");
        var fontSizes = Enumerable.Range(12, 16).Select(value => (double)value).ToArray();
        Directory.CreateDirectory(directory);

        try
        {
            Parallel.ForEach(fontSizes, fontSize =>
            {
                new SettingsStore(filePath).Save(new AppSettings { FontSize = fontSize });
            });

            var loaded = new SettingsStore(filePath).Load();
            Assert.Contains(loaded.FontSize, fontSizes);
            Assert.Empty(Directory.EnumerateFiles(directory, ".settings.json.*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
