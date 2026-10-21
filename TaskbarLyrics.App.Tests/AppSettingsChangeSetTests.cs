using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class AppSettingsChangeSetTests
{
    [Theory]
    [InlineData(nameof(AppSettings.EnableControlPanel), false)]
    [InlineData(nameof(AppSettings.LyricsTextAlignment), false)]
    [InlineData(nameof(AppSettings.FontSize), true)]
    [InlineData(nameof(AppSettings.LyricsLayoutScalePercent), true)]
    [InlineData(nameof(AppSettings.ShowCover), true)]
    public void AppearanceChangesReapplyOnlyTheRequiredLayout(string field, bool layoutChanged)
    {
        var current = new AppSettings();
        var next = current.Clone();
        switch (field)
        {
            case nameof(AppSettings.EnableControlPanel): next.EnableControlPanel = false; break;
            case nameof(AppSettings.LyricsTextAlignment): next.LyricsTextAlignment = LyricsTextAlignment.Right; break;
            case nameof(AppSettings.FontSize): next.FontSize += 1; break;
            case nameof(AppSettings.LyricsLayoutScalePercent): next.LyricsLayoutScalePercent = 125; break;
            case nameof(AppSettings.ShowCover): next.ShowCover = false; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
        var change = AppSettingsChangeSet.Create(current, next);
        Assert.True(change.VisualStyleChanged);
        Assert.True(change.RequiresLyricsWindowApply);
        Assert.Equal(layoutChanged, change.LyricsLayoutChanged);
        Assert.False(change.WindowLayoutChanged);
        Assert.False(change.LyricSyncServiceChanged);
        Assert.False(change.GlobalMediaHotkeysChanged);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OnlyTheActivePresentationWidthRequiresWindowLayout(bool floating, bool changeFloatingWidth)
    {
        var current = new AppSettings { UseFloatingWindow = floating, WindowWidth = 420, FloatingWindowWidth = 640 };
        var next = current.Clone();
        if (changeFloatingWidth) next.FloatingWindowWidth = 800;
        else next.WindowWidth = 500;
        var changes = AppSettingsChangeSet.Create(current, next);
        Assert.Equal(floating == changeFloatingWidth, changes.WindowLayoutChanged);
        Assert.Equal(floating == changeFloatingWidth, changes.RequiresLyricsWindowApply);
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
        Assert.True(changes.SearchStatusDisplayChanged);
        Assert.True(changes.LyricsLayoutChanged);
        Assert.True(changes.WindowLayoutChanged);
        Assert.True(changes.TaskbarEmbeddingChanged);
        Assert.True(changes.GlobalMediaHotkeysChanged);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateWhenFloatingWindowSwitchChangesReappliesLyricsWindowWithoutRebuildingOtherServices(bool floating)
    {
        var current = new AppSettings { UseFloatingWindow = floating };
        var next = current.Clone();
        next.UseFloatingWindow = !floating;

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.TaskbarEmbeddingChanged);
        Assert.True(changes.RequiresLyricsWindowApply);
        Assert.True(changes.WindowLayoutChanged);
        Assert.True(changes.VisualStyleChanged);
        Assert.False(changes.LyricSyncServiceChanged);
    }

    [Fact]
    public void CreateWhenSharedWindowLayoutChangesReappliesLyricsWindow()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.WindowWidth = 500;
        next.XOffset = 10;
        next.YOffset = -2;

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.False(changes.TaskbarEmbeddingChanged);
        Assert.True(changes.WindowLayoutChanged);
        Assert.True(changes.RequiresLyricsWindowApply);
    }

    [Fact]
    public void CreateWhenHorizontalAnchorChangesReappliesWindowLayoutForEmbedding()
    {
        var current = new AppSettings();
        var next = current.Clone();
        next.HorizontalAnchor = LyricsHorizontalAnchor.Center;

        var changes = AppSettingsChangeSet.Create(current, next);

        Assert.True(changes.WindowLayoutChanged);
        Assert.True(changes.RequiresLyricsWindowApply);
    }
    [Theory]
    [InlineData(CoverPosition.Left, CoverPosition.Right)]
    [InlineData(CoverPosition.Right, CoverPosition.Left)]
    public void CoverPositionChangeReappliesHostLayoutAndStyleWithoutRebuildingServices(
        CoverPosition currentPosition, CoverPosition nextPosition)
    {
        var current = new AppSettings { CoverPosition = currentPosition };
        var next = current.Clone();
        next.CoverPosition = nextPosition;
        var changes = AppSettingsChangeSet.Create(current, next);
        Assert.True(changes.VisualStyleChanged);
        Assert.True(changes.RequiresLyricsWindowApply);
        Assert.True(changes.LyricsLayoutChanged);
        Assert.False(changes.WindowLayoutChanged);
        Assert.False(changes.LyricSyncServiceChanged);
    }

}
