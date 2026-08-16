namespace TaskbarLyrics.App;

internal readonly record struct AppSettingsChangeSet(
    bool IsInitialApplication,
    bool PlayerRecognitionChanged,
    bool LocalMediaLibraryChanged,
    bool LyricSyncServiceChanged,
    bool WordScanningChanged,
    bool TranslationDisplayChanged,
    bool AutoHideWhenNoPlaybackChanged,
    bool SpectrumDisplayChanged,
    bool VisualStyleChanged,
    bool LyricsLayoutChanged,
    bool WindowLayoutChanged,
    bool GlobalMediaHotkeysChanged)
{
    public bool RequiresLyricsWindowApply =>
        IsInitialApplication ||
        PlayerRecognitionChanged ||
        LocalMediaLibraryChanged ||
        LyricSyncServiceChanged ||
        WordScanningChanged ||
        TranslationDisplayChanged ||
        AutoHideWhenNoPlaybackChanged ||
        SpectrumDisplayChanged ||
        VisualStyleChanged ||
        LyricsLayoutChanged ||
        WindowLayoutChanged;

    public static AppSettingsChangeSet Create(
        AppSettings current,
        AppSettings next,
        bool isInitialApplication = false)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);

        var playerRecognitionChanged = isInitialApplication ||
            current.EnableNetease != next.EnableNetease ||
            current.EnableQQMusic != next.EnableQQMusic ||
            current.EnableKugou != next.EnableKugou ||
            current.EnableSpotify != next.EnableSpotify ||
            !AreSameStrings(current.SourceRecognitionOrder, next.SourceRecognitionOrder);

        var localMediaLibraryChanged = isInitialApplication ||
            current.EnableLocalLyrics != next.EnableLocalLyrics ||
            !AreSameStrings(current.LocalMusicFolders, next.LocalMusicFolders);

        var lyricSyncServiceChanged = isInitialApplication ||
            localMediaLibraryChanged ||
            !AreSamePlayerSourceSettings(current.PlayerSources, next.PlayerSources);

        var wordScanningChanged = isInitialApplication ||
            current.EnableWordScanning != next.EnableWordScanning;

        var translationDisplayChanged = isInitialApplication ||
            current.ShowLyricTranslation != next.ShowLyricTranslation;

        var autoHideWhenNoPlaybackChanged = isInitialApplication ||
            current.AutoHideWhenNoPlayback != next.AutoHideWhenNoPlayback;

        var spectrumDisplayChanged = isInitialApplication ||
            current.SpectrumDisplayMode != next.SpectrumDisplayMode ||
            current.SpectrumAudioAccessGranted != next.SpectrumAudioAccessGranted;

        var visualStyleChanged = isInitialApplication ||
            current.FontSize != next.FontSize ||
            current.ShowCover != next.ShowCover ||
            current.CoverSize != next.CoverSize ||
            current.CoverGap != next.CoverGap ||
            current.CoverCornerRadius != next.CoverCornerRadius ||
            current.LyricsLayoutScalePercent != next.LyricsLayoutScalePercent ||
            !string.Equals(current.FontFamily, next.FontFamily, StringComparison.Ordinal) ||
            !string.Equals(current.FontWeight, next.FontWeight, StringComparison.Ordinal) ||
            current.ForegroundColorMode != next.ForegroundColorMode ||
            !string.Equals(current.ForegroundColor, next.ForegroundColor, StringComparison.Ordinal) ||
            current.ShowBackground != next.ShowBackground ||
            current.BackgroundOpacity != next.BackgroundOpacity ||
            current.ShowBorder != next.ShowBorder ||
            current.ShowTextShadow != next.ShowTextShadow ||
            current.LyricsTextAlignment != next.LyricsTextAlignment;

        var lyricsLayoutChanged = isInitialApplication ||
            current.FontSize != next.FontSize ||
            current.ShowCover != next.ShowCover ||
            current.CoverSize != next.CoverSize ||
            current.CoverGap != next.CoverGap ||
            current.CoverCornerRadius != next.CoverCornerRadius ||
            current.LyricsLayoutScalePercent != next.LyricsLayoutScalePercent;

        var windowLayoutChanged = isInitialApplication ||
            current.WindowWidth != next.WindowWidth ||
            current.HorizontalAnchor != next.HorizontalAnchor ||
            current.XOffset != next.XOffset ||
            current.YOffset != next.YOffset ||
            current.ForceAlwaysOnTop != next.ForceAlwaysOnTop ||
            current.LyricsDisplayMode != next.LyricsDisplayMode ||
            !AreSameStrings(current.SelectedDisplayIds, next.SelectedDisplayIds);

        var globalMediaHotkeysChanged = isInitialApplication ||
            !AreSameGlobalMediaHotkeys(current.GlobalMediaHotkeys, next.GlobalMediaHotkeys);

        return new AppSettingsChangeSet(
            isInitialApplication,
            playerRecognitionChanged,
            localMediaLibraryChanged,
            lyricSyncServiceChanged,
            wordScanningChanged,
            translationDisplayChanged,
            autoHideWhenNoPlaybackChanged,
            spectrumDisplayChanged,
            visualStyleChanged,
            lyricsLayoutChanged,
            windowLayoutChanged,
            globalMediaHotkeysChanged);
    }

    private static bool AreSameStrings(List<string>? current, List<string>? next)
    {
        if (ReferenceEquals(current, next))
        {
            return true;
        }

        if (current is null || next is null || current.Count != next.Count)
        {
            return false;
        }

        for (var index = 0; index < current.Count; index++)
        {
            if (!string.Equals(current[index], next[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreSamePlayerSourceSettings(
        Dictionary<string, PlayerSourceSettings>? current,
        Dictionary<string, PlayerSourceSettings>? next)
    {
        if (ReferenceEquals(current, next))
        {
            return true;
        }

        if (current is null || next is null || current.Count != next.Count)
        {
            return false;
        }

        foreach (var (source, currentSettings) in current)
        {
            if (!next.TryGetValue(source, out var nextSettings) ||
                currentSettings?.LyricOffsetMilliseconds != nextSettings?.LyricOffsetMilliseconds)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreSameGlobalMediaHotkeys(
        GlobalMediaHotkeySettings? current,
        GlobalMediaHotkeySettings? next)
    {
        var currentSettings = current ?? new GlobalMediaHotkeySettings();
        var nextSettings = next ?? new GlobalMediaHotkeySettings();
        return currentSettings.Enabled == nextSettings.Enabled &&
            string.Equals(currentSettings.TogglePlayPause, nextSettings.TogglePlayPause, StringComparison.Ordinal) &&
            string.Equals(currentSettings.PreviousTrack, nextSettings.PreviousTrack, StringComparison.Ordinal) &&
            string.Equals(currentSettings.NextTrack, nextSettings.NextTrack, StringComparison.Ordinal) &&
            string.Equals(currentSettings.SeekBackward, nextSettings.SeekBackward, StringComparison.Ordinal) &&
            string.Equals(currentSettings.SeekForward, nextSettings.SeekForward, StringComparison.Ordinal) &&
            string.Equals(currentSettings.ToggleLyricsVisibility, nextSettings.ToggleLyricsVisibility, StringComparison.Ordinal) &&
            string.Equals(currentSettings.ToggleTranslation, nextSettings.ToggleTranslation, StringComparison.Ordinal) &&
            string.Equals(currentSettings.ToggleWordScanning, nextSettings.ToggleWordScanning, StringComparison.Ordinal);
    }
}
