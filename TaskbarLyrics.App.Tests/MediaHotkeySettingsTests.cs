using System.IO;
using System.Text.Json;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class MediaHotkeySettingsTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"GlobalMediaHotkeys\":{\"TogglePlayPause\":\"Ctrl+P\"}}")]
    [InlineData("{\"GlobalMediaHotkeys\":{\"ActionEnabled\":null}}")]
    public void OldSettingsDefaultModeBindingAndIndividualActionsToEnabled(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.Equal("Alt+Shift+C", settings.GlobalMediaHotkeys.ToggleDisplayMode);
        Assert.All(MediaHotkeyCatalog.Definitions, definition =>
            Assert.True(settings.GlobalMediaHotkeys.IsActionEnabled(definition.Action)));
        if (json.Contains("Ctrl+P", StringComparison.Ordinal))
            Assert.Equal("Ctrl+P", settings.GlobalMediaHotkeys.TogglePlayPause);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData("Ctrl+F12", false)]
    public void EmptyBindingsAndActionSwitchesSurviveCloneCopyAndPersistence(string binding, bool enabled)
    {
        using var files = new TestDirectory();
        var store = new SettingsStore(Path.Combine(files.Path, "settings.json"));
        var settings = new AppSettings();
        settings.GlobalMediaHotkeys.ToggleDisplayMode = binding;
        settings.GlobalMediaHotkeys.SetActionEnabled(MediaHotkeyAction.ToggleDisplayMode, enabled);
        var clone = settings.Clone();
        clone.GlobalMediaHotkeys.SetActionEnabled(MediaHotkeyAction.ToggleDisplayMode, !enabled);
        Assert.Equal(enabled, settings.GlobalMediaHotkeys.IsActionEnabled(MediaHotkeyAction.ToggleDisplayMode));
        Assert.True(store.Save(settings));
        var loaded = store.Load();
        Assert.Equal(binding, loaded.GlobalMediaHotkeys.ToggleDisplayMode);
        Assert.Equal(enabled, loaded.GlobalMediaHotkeys.IsActionEnabled(MediaHotkeyAction.ToggleDisplayMode));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModeHotkeyChangesOnlyReapplyHotkeyRegistration(bool changeSwitch)
    {
        var current = new AppSettings();
        var next = current.Clone();
        if (changeSwitch) next.GlobalMediaHotkeys.SetActionEnabled(MediaHotkeyAction.ToggleDisplayMode, false);
        else next.GlobalMediaHotkeys.ToggleDisplayMode = "";
        var changes = AppSettingsChangeSet.Create(current, next);
        Assert.True(changes.GlobalMediaHotkeysChanged);
        Assert.False(changes.RequiresLyricsWindowApply);
        Assert.False(changes.LyricSyncServiceChanged);
        Assert.True(current.GlobalMediaHotkeys.IsActionEnabled(MediaHotkeyAction.ToggleDisplayMode));
    }
}
