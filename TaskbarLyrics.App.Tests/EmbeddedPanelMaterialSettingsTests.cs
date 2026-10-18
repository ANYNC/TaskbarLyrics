using System.IO;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class EmbeddedPanelMaterialSettingsTests
{
    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(80, 1, 1, 64)]
    [InlineData(0.7, 0.7, 0.7, 0.7)]
    [InlineData(double.NaN, 0.45, 0.55, 24)]
    [InlineData(double.PositiveInfinity, 0.45, 0.55, 24)]
    public void TaskbarMaterialValuesAreBoundedAndCloned(double value, double brightness, double opacity, double blur)
    {
        var settings = new AppSettings { EmbeddedPanelBrightness = value, EmbeddedPanelTintOpacity = value, EmbeddedPanelBlurAmount = value };
        var copy = settings.Clone();
        Assert.Equal(brightness, copy.EmbeddedPanelBrightness);
        Assert.Equal(opacity, copy.EmbeddedPanelTintOpacity);
        Assert.Equal(blur, copy.EmbeddedPanelBlurAmount);
    }

    [Fact]
    public void OldSettingsKeepFloatingMaterialAndTaskbarDefaultsThenRoundTripIndependently()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "taskbar-material-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, "{\"FloatingPanelBrightness\":0.8,\"FloatingPanelTintOpacity\":0.2,\"FloatingPanelBlurAmount\":40}");
            var store = new SettingsStore(path);
            var settings = store.Load();
            Assert.Equal(0.45, settings.EmbeddedPanelBrightness);
            Assert.Equal(0.55, settings.EmbeddedPanelTintOpacity);
            Assert.Equal(24, settings.EmbeddedPanelBlurAmount);
            settings.EmbeddedPanelBrightness = 0.7;
            settings.EmbeddedPanelTintOpacity = 0.3;
            settings.EmbeddedPanelBlurAmount = 36;
            Assert.True(store.Save(settings));
            var loaded = store.Load();
            Assert.Equal(0.7, loaded.EmbeddedPanelBrightness);
            Assert.Equal(0.3, loaded.EmbeddedPanelTintOpacity);
            Assert.Equal(36, loaded.EmbeddedPanelBlurAmount);
            Assert.Equal(0.8, loaded.FloatingPanelBrightness);
            Assert.Equal(0.2, loaded.FloatingPanelTintOpacity);
            Assert.Equal(40, loaded.FloatingPanelBlurAmount);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyActiveTaskbarMaterialReappliesWithoutLayoutOrServiceChanges(bool floating)
    {
        var current = new AppSettings { UseFloatingWindow = floating };
        for (var field = 0; field < 3; field++)
        {
            var next = current.Clone();
            switch (field)
            {
                case 0: next.EmbeddedPanelBrightness = 0.8; break;
                case 1: next.EmbeddedPanelTintOpacity = 0.3; break;
                case 2: next.EmbeddedPanelBlurAmount = 40; break;
            }
            var changes = AppSettingsChangeSet.Create(current, next);
            Assert.Equal(!floating, changes.VisualStyleChanged);
            Assert.Equal(!floating, changes.RequiresLyricsWindowApply);
            Assert.False(changes.LyricsLayoutChanged);
            Assert.False(changes.WindowLayoutChanged);
            Assert.False(changes.LyricSyncServiceChanged);
        }
    }
}
