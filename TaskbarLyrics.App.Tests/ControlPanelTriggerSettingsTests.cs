using System.IO;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class ControlPanelTriggerSettingsTests
{
    [Theory]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, false, true, false, true)]
    [InlineData(false, true, false, true, true)]
    [InlineData(false, false, false, false, true)]
    [InlineData(false, false, true, false, true)]
    public void NormalizationKeepsAnAvailableAreaAndPreservesCoverPreference(
        bool showCover, bool cover, bool lyrics, bool expectedCover, bool expectedLyrics)
    {
        var settings = new AppSettings
        {
            ShowCover = showCover,
            ControlPanelTriggerCover = cover,
            ControlPanelTriggerLyrics = lyrics,
            ControlPanelTriggerMethod = (ControlPanelTriggerMethod)999
        };
        settings.NormalizeControlPanelTriggers();
        settings.NormalizeControlPanelTriggers();
        Assert.Equal(expectedCover, settings.ControlPanelTriggerCover);
        Assert.Equal(expectedLyrics, settings.ControlPanelTriggerLyrics);
        Assert.Equal(ControlPanelTriggerMethod.Click, settings.ControlPanelTriggerMethod);
    }

    [Fact]
    public void TriggerPolicyEnforcesModeMethodVisibilityAndSelectedAreas()
    {
        var settings = new AppSettings();
        Assert.True(settings.CanTriggerControlPanel(ControlPanelTriggerArea.Cover, ControlPanelActivation.Click));
        Assert.False(settings.CanTriggerControlPanel(ControlPanelTriggerArea.Lyrics, ControlPanelActivation.Click));
        Assert.False(settings.CanTriggerControlPanel(ControlPanelTriggerArea.Cover, ControlPanelActivation.Hover));
        settings.ControlPanelTriggerMethod = ControlPanelTriggerMethod.Hover;
        settings.ControlPanelTriggerLyrics = true;
        foreach (var area in Enum.GetValues<ControlPanelTriggerArea>())
        {
            Assert.True(settings.CanTriggerControlPanel(area, ControlPanelActivation.Hover));
            Assert.True(settings.CanTriggerControlPanel(area, ControlPanelActivation.Keyboard));
            Assert.False(settings.CanTriggerControlPanel(area, ControlPanelActivation.Click));
        }
        settings.ShowCover = false;
        Assert.False(settings.CanTriggerControlPanel(ControlPanelTriggerArea.Cover, ControlPanelActivation.Hover));
        Assert.True(settings.CanTriggerControlPanel(ControlPanelTriggerArea.Lyrics, ControlPanelActivation.Hover));
        settings.EnableControlPanel = false;
        Assert.False(settings.CanTriggerControlPanel(ControlPanelTriggerArea.Lyrics, ControlPanelActivation.Hover));
        settings.UseFloatingWindow = true;
        settings.ShowCover = true;
        Assert.True(settings.CanTriggerControlPanel(ControlPanelTriggerArea.Cover, ControlPanelActivation.Click));
        Assert.False(settings.CanTriggerControlPanel(ControlPanelTriggerArea.Cover, ControlPanelActivation.Hover));
        Assert.False(settings.CanTriggerControlPanel(ControlPanelTriggerArea.Lyrics, ControlPanelActivation.Keyboard));
        Assert.False(settings.CanTriggerControlPanel((ControlPanelTriggerArea)999, ControlPanelActivation.Click));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingsStoreAddsDefaultsAndRoundTripsBothAreasWithoutChangingFloatingPreferences(bool floating)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"trigger-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, $"{{\"UseFloatingWindow\":{floating.ToString().ToLowerInvariant()},\"EnableControlPanel\":false,\"EnableFloatingControlPanel\":false}}");
            var store = new SettingsStore(path);
            var settings = store.Load();
            Assert.Equal(ControlPanelTriggerMethod.Click, settings.ControlPanelTriggerMethod);
            Assert.True(settings.ControlPanelTriggerCover);
            Assert.False(settings.ControlPanelTriggerLyrics);
            settings.ControlPanelTriggerMethod = ControlPanelTriggerMethod.Hover;
            settings.ControlPanelTriggerLyrics = true;
            Assert.True(store.Save(settings));
            var loaded = store.Load();
            Assert.Equal(ControlPanelTriggerMethod.Hover, loaded.ControlPanelTriggerMethod);
            Assert.True(loaded.ControlPanelTriggerCover);
            Assert.True(loaded.ControlPanelTriggerLyrics);
            Assert.False(loaded.EnableControlPanel);
            Assert.False(loaded.EnableFloatingControlPanel);
            Assert.Equal(floating, loaded.UseFloatingWindow);
            loaded.ResetWindowPresentation(floating: true);
            Assert.Equal(ControlPanelTriggerMethod.Hover, loaded.ControlPanelTriggerMethod);
            loaded.ResetWindowPresentation(floating: false);
            Assert.Equal(ControlPanelTriggerMethod.Click, loaded.ControlPanelTriggerMethod);
            Assert.True(loaded.ControlPanelTriggerCover);
            Assert.False(loaded.ControlPanelTriggerLyrics);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyEmbeddedModeAppliesTriggerPreferenceChanges(bool floating)
    {
        var current = new AppSettings { UseFloatingWindow = floating };
        var next = current.Clone();
        next.ControlPanelTriggerMethod = ControlPanelTriggerMethod.Hover;
        next.ControlPanelTriggerLyrics = true;
        var changes = AppSettingsChangeSet.Create(current, next);
        Assert.Equal(!floating, changes.VisualStyleChanged);
        Assert.Equal(!floating, changes.RequiresLyricsWindowApply);
        Assert.False(changes.LyricSyncServiceChanged);
        Assert.False(changes.WindowLayoutChanged);
        var script = LyricsStyleScriptFactory.Create(next, 1.5);
        Assert.Contains("\"controlPanelTriggerMethod\":\"Hover\"", script);
        Assert.Contains("\"controlPanelTriggerLyrics\":true", script);
    }
}
