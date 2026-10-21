using System.IO;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class FloatingPanelMaterialSettingsTests
{
    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"ShowBackground\":true}", false)]
    [InlineData("{\"ShowFloatingBackground\":true,\"EnableFloatingControlPanel\":false}", true)]
    public void PersistentBackgroundDefaultsOffAndRoundTripsIndependently(string json, bool expected)
    {
        using var files = new TestDirectory();
        var file = Path.Combine(files.Path, "settings.json");
        File.WriteAllText(file, json);
        var store = new SettingsStore(file);
        var settings = store.Load();
        Assert.Equal(expected, settings.ShowFloatingBackground);
        Assert.Equal(expected, settings.Clone().ShowFloatingBackground);
        settings.ShowFloatingBackground = !expected;
        Assert.True(store.Save(settings));
        var loaded = store.Load();
        Assert.Equal(!expected, loaded.ShowFloatingBackground);
        Assert.Equal(settings.ShowBackground, loaded.ShowBackground);
        Assert.Equal(settings.EnableFloatingControlPanel, loaded.EnableFloatingControlPanel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PersistentBackgroundReappliesOnlyTheActiveFloatingAppearance(bool floating)
    {
        var current = new AppSettings { UseFloatingWindow = floating };
        var next = current.Clone();
        next.ShowFloatingBackground = true;
        var changes = AppSettingsChangeSet.Create(current, next);
        Assert.Equal(floating, changes.RequiresLyricsWindowApply);
        Assert.Equal(floating, changes.VisualStyleChanged);
        Assert.False(changes.LyricsLayoutChanged);
        Assert.False(changes.WindowLayoutChanged);
        Assert.False(changes.LyricSyncServiceChanged);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(2, 1, 1)]
    [InlineData(0.7, 0.7, 0.7)]
    [InlineData(double.NaN, 0.25, 0.55)]
    [InlineData(double.PositiveInfinity, 0.25, 0.55)]
    public void NormalizesAppearanceAndPreservesItInClones(double value, double brightness, double opacity)
    {
        var settings = new AppSettings { FloatingPanelBrightness = value, FloatingPanelTintOpacity = value };
        Assert.Equal(brightness, settings.FloatingPanelBrightness);
        Assert.Equal(opacity, settings.FloatingPanelTintOpacity);
        Assert.Equal(brightness, settings.Clone().FloatingPanelBrightness);
        Assert.Equal(opacity, settings.Clone().FloatingPanelTintOpacity);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidBlurFallsBackToTheFloatingDefault(double value)
    {
        var settings = new AppSettings { FloatingPanelBlurAmount = value };
        Assert.Equal(32, settings.FloatingPanelBlurAmount);
        Assert.Equal(32, settings.Clone().FloatingPanelBlurAmount);
    }

    [Theory]
    [InlineData("{}", 0.25, 0.55, 32)]
    [InlineData("{\"FloatingPanelBrightness\":0.7,\"FloatingPanelTintOpacity\":0.3}", 0.7, 0.3, 32)]
    [InlineData("{\"FloatingPanelBrightness\":-2,\"FloatingPanelTintOpacity\":4}", 0, 1, 32)]
    [InlineData("{\"FloatingPanelBrightness\":0.45,\"FloatingPanelTintOpacity\":0.55,\"FloatingPanelBlurAmount\":24}", 0.45, 0.55, 24)]
    public void LoadsOldDefaultsAndRoundTripsNewAppearance(string json, double brightness, double opacity, double blur)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "material-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "settings.json");
        try
        {
            File.WriteAllText(file, json);
            var store = new SettingsStore(file);
            var loaded = store.Load();
            Assert.Equal(brightness, loaded.FloatingPanelBrightness);
            Assert.Equal(opacity, loaded.FloatingPanelTintOpacity);
            Assert.Equal(blur, loaded.FloatingPanelBlurAmount);
            Assert.True(store.Save(loaded));
            Assert.Equal(brightness, store.Load().FloatingPanelBrightness);
            Assert.Equal(opacity, store.Load().FloatingPanelTintOpacity);
            Assert.Equal(blur, store.Load().FloatingPanelBlurAmount);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AppearanceReappliesWithoutMovingLyricsOrRebuildingServices(bool brightness)
    {
        var old = new AppSettings { UseFloatingWindow = true };
        var next = old.Clone();
        if (brightness) next.FloatingPanelBrightness = 0.8;
        else next.FloatingPanelTintOpacity = 0.8;
        var changes = AppSettingsChangeSet.Create(old, next);
        Assert.True(changes.VisualStyleChanged);
        Assert.False(changes.LyricsLayoutChanged);
        Assert.False(changes.WindowLayoutChanged);
        Assert.False(changes.LyricSyncServiceChanged);
    }
}
