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
    public void CreateWhenLyricsTextAlignmentChangesOnlyReappliesVisualStyle()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.LyricsTextAlignment = LyricsTextAlignment.Right;

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.VisualStyleChanged);
        Assert.True(changes.RequiresLyricsWindowApply);
        Assert.False(changes.LyricsLayoutChanged);
        Assert.False(changes.WindowLayoutChanged);
        Assert.False(changes.LyricSyncServiceChanged);
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
    public void CreateWhenAutoHidePreferenceChangesReappliesLyricsWindowWithoutRebuildingService()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.AutoHideWhenNoPlayback = false;

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.AutoHideWhenNoPlaybackChanged);
        Assert.True(changes.RequiresLyricsWindowApply);
        Assert.False(changes.LyricSyncServiceChanged);
        Assert.False(changes.WindowLayoutChanged);
    }

    [Fact]
    public void CreateWhenSpectrumAudioAccessChangesReappliesSpectrumWithoutRebuildingOtherServices()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.SpectrumAudioAccessGranted = true;

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.SpectrumDisplayChanged);
        Assert.True(changes.RequiresLyricsWindowApply);
        Assert.False(changes.PlayerRecognitionChanged);
        Assert.False(changes.LocalMediaLibraryChanged);
        Assert.False(changes.LyricSyncServiceChanged);
        Assert.False(changes.WordScanningChanged);
        Assert.False(changes.TranslationDisplayChanged);
        Assert.False(changes.VisualStyleChanged);
        Assert.False(changes.LyricsLayoutChanged);
        Assert.False(changes.WindowLayoutChanged);
        Assert.False(changes.GlobalMediaHotkeysChanged);
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
    public void CreateWhenDisplayModeChangesReconcilesLyricsWindows()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.LyricsDisplayMode = LyricsDisplayMode.Selected;
        next.SelectedDisplayIds = ["display-a"];

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.WindowLayoutChanged);
        Assert.True(changes.RequiresLyricsWindowApply);
        Assert.False(changes.LyricSyncServiceChanged);
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
        Assert.True(changes.AutoHideWhenNoPlaybackChanged);
        Assert.True(changes.VisualStyleChanged);
        Assert.True(changes.LyricsLayoutChanged);
        Assert.True(changes.WindowLayoutChanged);
        Assert.True(changes.TaskbarEmbeddingChanged);
        Assert.True(changes.GlobalMediaHotkeysChanged);
    }

    [Fact]
    public void CreateWhenEmbeddingSwitchChangesReappliesLyricsWindowWithoutRebuildingOtherServices()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.TaskbarEmbeddingEnabled = true;

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.TaskbarEmbeddingChanged);
        Assert.True(changes.RequiresLyricsWindowApply);
        Assert.False(changes.WindowLayoutChanged);
        Assert.False(changes.LyricSyncServiceChanged);
    }

    [Fact]
    public void CreateWhenEmbeddingLayoutChangesReappliesLyricsWindow()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.EmbeddedTaskbarWidth = 500;
        next.EmbeddedTaskbarHorizontalAnchor = EmbeddedTaskbarHorizontalAnchor.Center;
        next.EmbeddedTaskbarHorizontalOffset = 10;
        next.EmbeddedTaskbarVerticalOffset = -2;

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.TaskbarEmbeddingChanged);
        Assert.True(changes.RequiresLyricsWindowApply);
    }
}
