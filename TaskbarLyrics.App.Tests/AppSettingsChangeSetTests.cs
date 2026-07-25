using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class AppSettingsChangeSetTests
{
    [Fact]
    public void Create_WhenOnlyVisualStyleChanges_DoesNotRebuildLyricsOrHotkeys()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.FontSize += 1;

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.VisualStyleChanged);
        Assert.True(changes.RequiresLyricsWindowApply);
        Assert.False(changes.LyricSyncServiceChanged);
        Assert.False(changes.GlobalMediaHotkeysChanged);
    }

    [Fact]
    public void Create_WhenLocalMusicFolderChanges_ReconfiguresMediaAndLyrics()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.LocalMusicFolders.Add("D:\\Music");

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.LocalMediaLibraryChanged);
        Assert.True(changes.LyricSyncServiceChanged);
    }

    [Fact]
    public void Create_WhenRecognitionOrderChanges_DoesNotRebuildLyrics()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.SourceRecognitionOrder.Reverse();

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.PlayerRecognitionChanged);
        Assert.False(changes.LyricSyncServiceChanged);
    }

    [Fact]
    public void Create_WhenPlayerOffsetChanges_RebuildsLyrics()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.SetPlayerLyricOffsetMilliseconds("QQMusic", 123);

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.LyricSyncServiceChanged);
        Assert.False(changes.PlayerRecognitionChanged);
    }

    [Fact]
    public void Create_WhenHotkeyChanges_DoesNotApplyLyricsWindow()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.GlobalMediaHotkeys.TogglePlayPause = "Ctrl+Alt+P";

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.GlobalMediaHotkeysChanged);
        Assert.False(changes.RequiresLyricsWindowApply);
    }

    [Fact]
    public void Create_WhenOnlyUpdatePreferenceChanges_DoesNotApplyRuntimeServices()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.AutoCheckUpdates = !next.AutoCheckUpdates;

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.False(changes.RequiresLyricsWindowApply);
        Assert.False(changes.GlobalMediaHotkeysChanged);
    }

    [Fact]
    public void Create_ForInitialApplication_RequiresEveryRuntimeArea()
    {
        var settings = new AppSettings();

        var changes = AppSettingsChangeSet.Create(settings, settings.Clone(), isInitialApplication: true);

        Assert.True(changes.PlayerRecognitionChanged);
        Assert.True(changes.LocalMediaLibraryChanged);
        Assert.True(changes.LyricSyncServiceChanged);
        Assert.True(changes.SpectrumDisplayChanged);
        Assert.True(changes.VisualStyleChanged);
        Assert.True(changes.WindowLayoutChanged);
        Assert.True(changes.GlobalMediaHotkeysChanged);
    }
}
