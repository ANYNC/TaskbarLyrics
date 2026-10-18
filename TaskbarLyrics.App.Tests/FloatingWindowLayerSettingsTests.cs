using System.IO;
using System.Text.Json;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class FloatingWindowLayerSettingsTests
{
    [Theory]
    [InlineData(true, FloatingWindowLayer.Fullscreen)]
    [InlineData(false, FloatingWindowLayer.Window)]
    public void LegacySwitchKeepsItsBehaviorWhenNewFieldIsAbsent(bool force, FloatingWindowLayer expected)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>($"{{\"ForceAlwaysOnTop\":{force.ToString().ToLowerInvariant()}}}")!;
        Assert.Equal(expected, settings.FloatingWindowLayer);
        Assert.Equal(expected, settings.Clone().FloatingWindowLayer);
    }

    [Theory]
    [InlineData("{\"FloatingWindowLayer\":0,\"ForceAlwaysOnTop\":true}")]
    [InlineData("{\"ForceAlwaysOnTop\":true,\"FloatingWindowLayer\":0}")]
    public void ExplicitDesktopChoiceWinsRegardlessOfJsonFieldOrder(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.Equal(FloatingWindowLayer.Desktop, settings.FloatingWindowLayer);
        Assert.Equal(FloatingWindowLayer.Desktop, settings.Clone().FloatingWindowLayer);
    }

    [Theory]
    [InlineData(true, FloatingWindowLayer.Fullscreen)]
    [InlineData(false, FloatingWindowLayer.Window)]
    public void UnknownStoredLevelFallsBackToLegacyPreference(bool force, FloatingWindowLayer expected)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>($"{{\"FloatingWindowLayer\":99,\"ForceAlwaysOnTop\":{force.ToString().ToLowerInvariant()}}}")!;
        Assert.Equal(expected, settings.FloatingWindowLayer);
    }

    [Theory]
    [InlineData(FloatingWindowLayer.Desktop)]
    [InlineData(FloatingWindowLayer.Window)]
    [InlineData(FloatingWindowLayer.Fullscreen)]
    public void LevelSurvivesSaveAndReload(FloatingWindowLayer level)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "floating-layer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            var settings = new AppSettings { FloatingWindowLayer = level };
            Assert.True(store.Save(settings));
            Assert.Equal(level, store.Load().FloatingWindowLayer);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangingLevelOnlyReappliesActiveFloatingWindows(bool floating)
    {
        var current = new AppSettings { UseFloatingWindow = floating };
        var next = current.Clone();
        next.FloatingWindowLayer = FloatingWindowLayer.Desktop;
        var changes = AppSettingsChangeSet.Create(current, next);
        Assert.Equal(floating, changes.WindowLayoutChanged);
        Assert.Equal(floating, changes.RequiresLyricsWindowApply);
        Assert.False(changes.LyricSyncServiceChanged);
        Assert.False(changes.TaskbarEmbeddingChanged);
    }
}
