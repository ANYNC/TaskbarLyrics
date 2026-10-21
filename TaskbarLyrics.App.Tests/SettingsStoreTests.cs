using System.IO;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class SettingsStoreTests
{
    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"ShowLyricSearchStatus\":false}", false)]
    [InlineData("{\"ShowLyricSearchStatus\":true}", true)]
    public void SearchStatusPreferenceDefaultsOnAndRoundTrips(string json, bool expected)
    {
        using var files = new TestDirectory();
        var path = Path.Combine(files.Path, "settings.json");
        File.WriteAllText(path, json);
        var store = new SettingsStore(path);
        var loaded = store.Load();
        Assert.Equal(expected, loaded.ShowLyricSearchStatus);
        Assert.Equal(expected, loaded.Clone().ShowLyricSearchStatus);
        Assert.True(store.Save(loaded));
        Assert.Equal(expected, store.Load().ShowLyricSearchStatus);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PresentationWidthsMigrateOnceAndRoundTripIndependently(bool floating, bool hasFloatingWidth)
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, $"{{\"UseFloatingWindow\":{floating.ToString().ToLowerInvariant()},\"WindowWidth\":620" +
            (hasFloatingWidth ? ",\"FloatingWindowWidth\":860}" : "}"));
        var store = new SettingsStore(path);
        var loaded = store.Load();
        Assert.Equal(620, loaded.WindowWidth);
        Assert.Equal(hasFloatingWidth ? 860 : 620, loaded.FloatingWindowWidth);
        Assert.Equal(loaded.FloatingWindowWidth, loaded.Clone().FloatingWindowWidth);
        loaded.WindowWidth = 360;
        Assert.True(store.Save(loaded));
        var roundTripped = store.Load();
        Assert.Equal(360, roundTripped.WindowWidth);
        Assert.Equal(hasFloatingWidth ? 860 : 620, roundTripped.FloatingWindowWidth);
        roundTripped.UseFloatingWindow = true;
        Assert.Equal(roundTripped.FloatingWindowWidth, roundTripped.GetWindowWidth());
        roundTripped.UseFloatingWindow = false;
        Assert.Equal(360, roundTripped.GetWindowWidth());
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(5000, 1400)]
    [InlineData(double.NaN, AppSettings.DefaultWindowWidth)]
    public void FloatingWidthNormalizesWithoutChangingEmbeddedWidth(double input, double expected)
    {
        var settings = new AppSettings { WindowWidth = 620, FloatingWindowWidth = input };
        settings.NormalizeWindowLayout();
        Assert.Equal(620, settings.WindowWidth);
        Assert.Equal(expected, settings.FloatingWindowWidth);
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"EnableControlPanel\":false}", false)]
    [InlineData("{\"EnableControlPanel\":true}", true)]
    public void ControlPanelSwitchPreservesDefaultAndRoundTrips(string json, bool expected)
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        File.WriteAllText(filePath, json);
        var store = new SettingsStore(filePath);
        var loaded = store.Load();
        Assert.Equal(expected, loaded.EnableControlPanel);
        Assert.Equal(expected, loaded.Clone().EnableControlPanel);
        Assert.True(store.Save(loaded));
        Assert.Equal(expected, store.Load().EnableControlPanel);
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"EnableWordScanning\":false}", false)]
    [InlineData("{\"EnableWordScanning\":true}", true)]
    public void LoadPreservesWordScanningDefaultAndExplicitValue(
        string json,
        bool expectedEnabled)
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        File.WriteAllText(filePath, json);

        var loaded = new SettingsStore(filePath).Load();

        Assert.Equal(expectedEnabled, loaded.EnableWordScanning);
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"AutoHideWhenNoPlayback\":false}", false)]
    [InlineData("{\"AutoHideWhenNoPlayback\":true}", true)]
    public void LoadPreservesAutoHideDefaultAndExplicitValue(
        string json,
        bool expectedEnabled)
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        File.WriteAllText(filePath, json);

        var loaded = new SettingsStore(filePath).Load();

        Assert.Equal(expectedEnabled, loaded.AutoHideWhenNoPlayback);
    }

    [Theory]
    [InlineData(0, ForegroundColorMode.Dark)]
    [InlineData(1, ForegroundColorMode.Light)]
    [InlineData(2, ForegroundColorMode.Custom)]
    public void LoadPreservesLegacyForegroundColorModeValues(
        int persistedValue,
        ForegroundColorMode expectedMode)
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        File.WriteAllText(filePath, $"{{\"ForegroundColorMode\":{persistedValue}}}");

        var loaded = new SettingsStore(filePath).Load();

        Assert.Equal(expectedMode, loaded.ForegroundColorMode);
    }

    [Fact]
    public void SavePersistsSystemForegroundColorModeAsTheNextEnumValue()
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        var settings = new AppSettings
        {
            ForegroundColorMode = ForegroundColorMode.System
        };

        var saved = new SettingsStore(filePath).Save(settings);

        Assert.True(saved);
        Assert.Contains("\"ForegroundColorMode\": 3", File.ReadAllText(filePath), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveReplacesTheSettingsFileWithoutLeavingTemporaryFiles()
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
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

    [Fact]
    public void SaveWhenTargetCannotBeReplacedReportsFailure()
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(filePath);
        var saved = new SettingsStore(filePath).Save(new AppSettings());

        Assert.False(saved);
        Assert.Empty(Directory.EnumerateFiles(directory, ".settings.json.*.tmp"));
    }

    [Fact]
    public void LoadWhenSettingsJsonIsInvalidReturnsDefaultSettings()
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        File.WriteAllText(filePath, "{ invalid json }");

        var loaded = new SettingsStore(filePath).Load();

        Assert.Equal(new AppSettings().FontSize, loaded.FontSize);
    }

    [Fact]
    public void LoadWithoutLayoutScaleUsesOneHundredPercentAndPreservesLegacyBaseValues()
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        File.WriteAllText(filePath, "{\"FontSize\":14.3,\"CoverSize\":34.3,\"UseSafeFontSizeRange\":false,\"UseSafeCoverSizeRange\":false}");

        var loaded = new SettingsStore(filePath).Load();

        Assert.Equal(14.3, loaded.FontSize);
        Assert.Equal(34.3, loaded.CoverSize);
        Assert.Equal(AppSettings.DefaultLyricsLayoutScalePercent, loaded.LyricsLayoutScalePercent);
        Assert.True(loaded.ShowCover);
        Assert.Equal(ForegroundColorMode.System, loaded.ForegroundColorMode);
    }

    [Fact]
    public void LoadUndefinedLyricsTextAlignmentFallsBackToLeft()
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        File.WriteAllText(filePath, "{\"LyricsTextAlignment\":999}");

        var store = new SettingsStore(filePath);
        var loaded = store.Load();

        Assert.Equal(LyricsTextAlignment.Left, loaded.LyricsTextAlignment);
        Assert.True(store.Save(loaded));
        Assert.Contains("\"LyricsTextAlignment\": 0", File.ReadAllText(filePath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LyricsTextAlignment.Left)]
    [InlineData(LyricsTextAlignment.Center)]
    [InlineData(LyricsTextAlignment.Right)]
    public void SaveAndLoadRoundTripsLyricsTextAlignment(LyricsTextAlignment alignment)
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        var store = new SettingsStore(filePath);
        Assert.True(store.Save(new AppSettings { LyricsTextAlignment = alignment }));

        Assert.Equal(alignment, store.Load().LyricsTextAlignment);
    }

    [Fact]
    public void LoadClampsLayoutSettingsToExtendedHardBoundaries()
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        File.WriteAllText(filePath, "{\"FontSize\":200,\"CoverSize\":500,\"CoverGap\":500,\"CoverCornerRadius\":500,\"LyricsLayoutScalePercent\":500}");

        var loaded = new SettingsStore(filePath).Load();

        Assert.Equal(AppSettings.ExtendedFontSizeMax, loaded.FontSize);
        Assert.Equal(AppSettings.ExtendedCoverSizeMax, loaded.CoverSize);
        Assert.Equal(AppSettings.CoverGapMax, loaded.CoverGap);
        Assert.Equal(AppSettings.ExtendedCoverSizeMax / 2, loaded.CoverCornerRadius);
        Assert.Equal(AppSettings.MaximumLyricsLayoutScalePercent, loaded.LyricsLayoutScalePercent);
    }

    [Fact]
    public void LoadMigratesLegacySpectrumSwitchAndSaveDropsLegacyFields()
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
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

    [Theory]
    [InlineData("{\"SpectrumDisplayMode\":2}", SpectrumDisplayMode.Disabled, false)]
    [InlineData("{\"SpectrumDisplayMode\":2,\"SpectrumAudioAccessGranted\":true}", SpectrumDisplayMode.Always, true)]
    public void LoadMigratesSpectrumAudioAccessAndPreservesAuthorizedMode(
        string json,
        SpectrumDisplayMode expectedMode,
        bool expectedAccess)
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        File.WriteAllText(filePath, json);

        var loaded = new SettingsStore(filePath).Load();

        Assert.Equal(expectedMode, loaded.SpectrumDisplayMode);
        Assert.Equal(expectedAccess, loaded.SpectrumAudioAccessGranted);
    }

    [Fact]
    public void SaveWhenCalledConcurrentlyKeepsAValidSettingsFile()
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        var fontSizes = Enumerable.Range(12, 16).Select(value => (double)value).ToArray();
        Parallel.ForEach(fontSizes, fontSize =>
        {
            new SettingsStore(filePath).Save(new AppSettings { FontSize = fontSize });
        });

        var loaded = new SettingsStore(filePath).Load();
        Assert.Contains(loaded.FontSize, fontSizes);
        Assert.Empty(Directory.EnumerateFiles(directory, ".settings.json.*.tmp"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"SelectedDisplayIds\":null}")]
    public void LoadOldSettingsDefaultsToAllDisplays(string json)
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        File.WriteAllText(filePath, json);

        var loaded = new SettingsStore(filePath).Load();

        Assert.Equal(LyricsDisplayMode.All, loaded.LyricsDisplayMode);
        Assert.Empty(loaded.SelectedDisplayIds);
    }

    [Fact]
    public void SaveAndLoadRoundTripsSelectedDisplayIds()
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        var store = new SettingsStore(filePath);
        var settings = new AppSettings
        {
            LyricsDisplayMode = LyricsDisplayMode.Selected,
            SelectedDisplayIds = [" display-a ", "DISPLAY-A", "display-b"]
        };

        Assert.True(store.Save(settings));
        var loaded = store.Load();

        Assert.Equal(LyricsDisplayMode.Selected, loaded.LyricsDisplayMode);
        Assert.Equal(["display-a", "display-b"], loaded.SelectedDisplayIds);
    }

    [Fact]
    public void LoadMigratesLegacyEmbeddedPresentationToSharedWindowLayout()
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        File.WriteAllText(
            filePath,
            "{\"TaskbarEmbeddingEnabled\":true,\"EmbeddedTaskbarWidth\":500,\"EmbeddedTaskbarHorizontalOffset\":12,\"EmbeddedTaskbarVerticalOffset\":-4,\"LyricsLayoutScalePercent\":125}");

        var store = new SettingsStore(filePath);
        var loaded = store.Load();

        Assert.False(loaded.UseFloatingWindow);
        Assert.Equal(400, loaded.WindowWidth);
        Assert.Equal(400, loaded.FloatingWindowWidth);
        Assert.Equal(12, loaded.XOffset);
        Assert.Equal(-4, loaded.YOffset);

        Assert.True(store.Save(loaded));
        var saved = File.ReadAllText(filePath);
        Assert.Contains("\"UseFloatingWindow\": false", saved, StringComparison.Ordinal);
        Assert.Contains("\"WindowWidth\": 400", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("TaskbarEmbeddingEnabled", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("EmbeddedTaskbarWidth", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("EmbeddedTaskbarHorizontalOffset", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("EmbeddedTaskbarVerticalOffset", saved, StringComparison.Ordinal);

        var roundTripped = store.Load();
        Assert.False(roundTripped.UseFloatingWindow);
        Assert.Equal(loaded.WindowWidth, roundTripped.WindowWidth);
        Assert.Equal(loaded.XOffset, roundTripped.XOffset);
        Assert.Equal(loaded.YOffset, roundTripped.YOffset);
    }

    [Fact]
    public void LoadMigratesLegacyFloatingPresentationAndPreservesSharedLayout()
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        File.WriteAllText(
            filePath,
            "{\"TaskbarEmbeddingEnabled\":false,\"WindowWidth\":100,\"XOffset\":-36,\"YOffset\":18}");

        var store = new SettingsStore(filePath);
        var loaded = store.Load();

        Assert.True(loaded.UseFloatingWindow);
        Assert.Equal(100, loaded.WindowWidth);
        Assert.Equal(-36, loaded.XOffset);
        Assert.Equal(18, loaded.YOffset);
        Assert.True(store.Save(loaded));
        Assert.Equal(100, store.Load().WindowWidth);
    }

    [Fact]
    public void LoadDefaultsToEmbeddedWhenNoPresentationFieldExists()
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        File.WriteAllText(filePath, "{}");

        var loaded = new SettingsStore(filePath).Load();

        Assert.False(loaded.UseFloatingWindow);
    }

    [Fact]
    public void NewPresentationFieldTakesPrecedenceOverLegacyPresentationFields()
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var filePath = Path.Combine(directory, "settings.json");
        File.WriteAllText(
            filePath,
            "{\"UseFloatingWindow\":true,\"TaskbarEmbeddingEnabled\":true,\"EmbeddedTaskbarWidth\":600}");

        var loaded = new SettingsStore(filePath).Load();

        Assert.True(loaded.UseFloatingWindow);
        Assert.Equal(AppSettings.DefaultWindowWidth, loaded.WindowWidth);
    }
    [Theory]
    [InlineData(CoverPosition.Left)]
    [InlineData(CoverPosition.Right)]
    public void SaveAndLoadRoundTripsCoverPositionWithoutChangingAlignment(CoverPosition position)
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        Assert.True(store.Save(new AppSettings { CoverPosition = position, LyricsTextAlignment = LyricsTextAlignment.Center }));
        var loaded = store.Load();
        Assert.Equal(position, loaded.CoverPosition);
        Assert.Equal(LyricsTextAlignment.Center, loaded.LyricsTextAlignment);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"CoverPosition\":999}")]
    public void LoadMissingOrInvalidCoverPositionPreservesExistingLayout(string json)
    {
        using var files = new TestDirectory();
        var directory = files.Path;
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, json);
        Assert.Equal(CoverPosition.Left, new SettingsStore(path).Load().CoverPosition);
    }

}
