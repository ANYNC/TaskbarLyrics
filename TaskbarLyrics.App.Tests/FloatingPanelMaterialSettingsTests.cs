using System.IO;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class FloatingPanelMaterialSettingsTests
{
    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(2, 1, 1)]
    [InlineData(0.7, 0.7, 0.7)]
    [InlineData(double.NaN, 0.45, 0.55)]
    [InlineData(double.PositiveInfinity, 0.45, 0.55)]
    public void NormalizesAppearanceAndPreservesItInClones(double value, double brightness, double opacity)
    {
        var settings = new AppSettings { FloatingPanelBrightness = value, FloatingPanelTintOpacity = value };
        Assert.Equal(brightness, settings.FloatingPanelBrightness);
        Assert.Equal(opacity, settings.FloatingPanelTintOpacity);
        Assert.Equal(brightness, settings.Clone().FloatingPanelBrightness);
        Assert.Equal(opacity, settings.Clone().FloatingPanelTintOpacity);
    }

    [Theory]
    [InlineData("{}", 0.45, 0.55)]
    [InlineData("{\"FloatingPanelBrightness\":0.7,\"FloatingPanelTintOpacity\":0.3}", 0.7, 0.3)]
    [InlineData("{\"FloatingPanelBrightness\":-2,\"FloatingPanelTintOpacity\":4}", 0, 1)]
    public void LoadsOldDefaultsAndRoundTripsNewAppearance(string json, double brightness, double opacity)
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
            Assert.True(store.Save(loaded));
            Assert.Equal(brightness, store.Load().FloatingPanelBrightness);
            Assert.Equal(opacity, store.Load().FloatingPanelTintOpacity);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AppearanceReappliesWithoutMovingLyricsOrRebuildingServices(bool brightness)
    {
        var old = new AppSettings();
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
