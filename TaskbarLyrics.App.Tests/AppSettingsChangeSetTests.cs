using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class AppSettingsChangeSetTests
{
    [Fact]
    public void CreateWhenOnlyVisualStyleChangesDoesNotRebuildLyricsOrHotkeys()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.FontSize += 1;

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.VisualStyleChanged);
        Assert.True(changes.LyricsLayoutChanged);
        Assert.True(changes.RequiresLyricsWindowApply);
        Assert.False(changes.LyricSyncServiceChanged);
        Assert.False(changes.GlobalMediaHotkeysChanged);
    }

    [Fact]
    public void CreateWhenLayoutScaleChangesReappliesStyleAndWindowLayout()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.LyricsLayoutScalePercent = 125;

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.VisualStyleChanged);
        Assert.True(changes.LyricsLayoutChanged);
        Assert.True(changes.RequiresLyricsWindowApply);
        Assert.False(changes.LyricSyncServiceChanged);
    }

    [Fact]
    public void CreateWhenCoverVisibilityChangesReappliesStyleAndWindowLayout()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.ShowCover = false;

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.VisualStyleChanged);
        Assert.True(changes.LyricsLayoutChanged);
        Assert.True(changes.RequiresLyricsWindowApply);
        Assert.False(changes.LyricSyncServiceChanged);
    }

    [Fact]
    public void CreateWhenLocalMusicFolderChangesReconfiguresMediaAndLyrics()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.LocalMusicFolders.Add("D:\\Music");

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.LocalMediaLibraryChanged);
        Assert.True(changes.LyricSyncServiceChanged);
    }

    [Fact]
    public void CreateWhenRecognitionOrderChangesDoesNotRebuildLyrics()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.SourceRecognitionOrder.Reverse();

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.PlayerRecognitionChanged);
        Assert.False(changes.LyricSyncServiceChanged);
    }

    [Fact]
    public void CreateWhenPlayerOffsetChangesRebuildsLyrics()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.SetPlayerLyricOffsetMilliseconds("QQMusic", 123);

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.LyricSyncServiceChanged);
        Assert.False(changes.PlayerRecognitionChanged);
    }

    [Fact]
    public void CreateWhenWordScanningChangesReappliesLyricsWindowWithoutRebuildingService()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.EnableWordScanning = false;

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.WordScanningChanged);
        Assert.True(changes.RequiresLyricsWindowApply);
        Assert.False(changes.LyricSyncServiceChanged);
    }

    [Fact]
    public void CreateWhenTranslationDisplayChangesReappliesLyricsWindowWithoutRebuildingService()
    {
        var current = new AppSettings
        {
            ShowLyricTranslation = false
        };
        var next = current.Clone();
        next.ShowLyricTranslation = true;

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.TranslationDisplayChanged);
        Assert.True(changes.RequiresLyricsWindowApply);
        Assert.False(changes.LyricSyncServiceChanged);
    }

    [Fact]
    public void CreateWhenHotkeyChangesDoesNotApplyLyricsWindow()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.GlobalMediaHotkeys.TogglePlayPause = "Ctrl+Shift+P";

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.GlobalMediaHotkeysChanged);
        Assert.False(changes.RequiresLyricsWindowApply);
    }

    [Fact]
    public void CreateWhenOnlyUpdatePreferenceChangesDoesNotApplyRuntimeServices()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.AutoCheckUpdates = !next.AutoCheckUpdates;

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.False(changes.RequiresLyricsWindowApply);
        Assert.False(changes.GlobalMediaHotkeysChanged);
    }

    [Fact]
    public void CreateForInitialApplicationRequiresEveryRuntimeArea()
    {
        var settings = new AppSettings();

        var changes = AppSettingsChangeSet.Create(settings, settings.Clone(), isInitialApplication: true);

        Assert.True(changes.PlayerRecognitionChanged);
        Assert.True(changes.LocalMediaLibraryChanged);
        Assert.True(changes.LyricSyncServiceChanged);
        Assert.True(changes.SpectrumDisplayChanged);
        Assert.True(changes.VisualStyleChanged);
        Assert.True(changes.LyricsLayoutChanged);
        Assert.True(changes.WindowLayoutChanged);
        Assert.True(changes.GlobalMediaHotkeysChanged);
    }
}
