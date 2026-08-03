using System.IO;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class SettingsStoreTests
{
    [Theory]
    [InlineData(0, ForegroundColorMode.Dark)]
    [InlineData(1, ForegroundColorMode.Light)]
    [InlineData(2, ForegroundColorMode.Custom)]
    public void LoadPreservesLegacyForegroundColorModeValues(
        int persistedValue,
        ForegroundColorMode expectedMode)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"settings-store-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(filePath, $"{{\"ForegroundColorMode\":{persistedValue}}}");

            var loaded = new SettingsStore(filePath).Load();

            Assert.Equal(expectedMode, loaded.ForegroundColorMode);
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
    public void SavePersistsSystemForegroundColorModeAsTheNextEnumValue()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"settings-store-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);

        try
        {
            var settings = new AppSettings
            {
                ForegroundColorMode = ForegroundColorMode.System
            };

            var saved = new SettingsStore(filePath).Save(settings);

            Assert.True(saved);
            Assert.Contains("\"ForegroundColorMode\": 3", File.ReadAllText(filePath), StringComparison.Ordinal);
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
                ForegroundColor = "#FF336699",
                ShowCover = false
            };

            var saved = store.Save(settings);

            Assert.True(saved);
            var loaded = store.Load();
            Assert.Equal(28, loaded.FontSize);
            Assert.Equal("#FF336699", loaded.ForegroundColor);
            Assert.False(loaded.ShowCover);
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
    public void SaveWhenTargetCannotBeReplacedReportsFailure()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"settings-store-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(filePath);

        try
        {
            var saved = new SettingsStore(filePath).Save(new AppSettings());

            Assert.False(saved);
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
    public void LoadWithoutLayoutScaleUsesOneHundredPercentAndPreservesLegacyBaseValues()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"settings-store-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(filePath, "{\"FontSize\":14.3,\"CoverSize\":34.3,\"UseSafeFontSizeRange\":false,\"UseSafeCoverSizeRange\":false}");

            var loaded = new SettingsStore(filePath).Load();

            Assert.Equal(14.3, loaded.FontSize);
            Assert.Equal(34.3, loaded.CoverSize);
            Assert.Equal(AppSettings.DefaultLyricsLayoutScalePercent, loaded.LyricsLayoutScalePercent);
            Assert.True(loaded.ShowCover);
            Assert.Equal(ForegroundColorMode.System, loaded.ForegroundColorMode);
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
    public void LoadClampsLayoutSettingsToExtendedHardBoundaries()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"settings-store-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(filePath, "{\"FontSize\":200,\"CoverSize\":500,\"CoverGap\":500,\"CoverCornerRadius\":500,\"LyricsLayoutScalePercent\":500}");

            var loaded = new SettingsStore(filePath).Load();

            Assert.Equal(AppSettings.ExtendedFontSizeMax, loaded.FontSize);
            Assert.Equal(AppSettings.ExtendedCoverSizeMax, loaded.CoverSize);
            Assert.Equal(AppSettings.CoverGapMax, loaded.CoverGap);
            Assert.Equal(AppSettings.ExtendedCoverSizeMax / 2, loaded.CoverCornerRadius);
            Assert.Equal(AppSettings.MaximumLyricsLayoutScalePercent, loaded.LyricsLayoutScalePercent);
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
