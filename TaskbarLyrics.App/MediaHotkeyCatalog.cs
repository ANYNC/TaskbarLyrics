namespace TaskbarLyrics.App;

internal sealed record MediaHotkeyDefinition(
    MediaHotkeyAction Action,
    string SettingKey,
    string StatusKey,
    string DisplayName,
    string DefaultBinding,
    int RegistrationId,
    Func<GlobalMediaHotkeySettings, string> ReadBinding,
    Action<GlobalMediaHotkeySettings, string> WriteBinding);

internal static class MediaHotkeyCatalog
{
    private const int RegistrationIdBase = 0x5A00;

    public static IReadOnlyList<MediaHotkeyDefinition> Definitions { get; } =
    [
        new(
            MediaHotkeyAction.TogglePlayPause,
            "hotkeyTogglePlayPause",
            "togglePlayPause",
            "播放 / 暂停",
            "Alt+Shift+P",
            RegistrationIdBase + (int)MediaHotkeyAction.TogglePlayPause,
            settings => settings.TogglePlayPause,
            (settings, value) => settings.TogglePlayPause = value),
        new(
            MediaHotkeyAction.PreviousTrack,
            "hotkeyPreviousTrack",
            "previousTrack",
            "上一首",
            "Alt+Shift+Left",
            RegistrationIdBase + (int)MediaHotkeyAction.PreviousTrack,
            settings => settings.PreviousTrack,
            (settings, value) => settings.PreviousTrack = value),
        new(
            MediaHotkeyAction.NextTrack,
            "hotkeyNextTrack",
            "nextTrack",
            "下一首",
            "Alt+Shift+Right",
            RegistrationIdBase + (int)MediaHotkeyAction.NextTrack,
            settings => settings.NextTrack,
            (settings, value) => settings.NextTrack = value),
        new(
            MediaHotkeyAction.SeekBackward,
            "hotkeySeekBackward",
            "seekBackward",
            "后退 5 秒",
            "Ctrl+Alt+Shift+Left",
            RegistrationIdBase + (int)MediaHotkeyAction.SeekBackward,
            settings => settings.SeekBackward,
            (settings, value) => settings.SeekBackward = value),
        new(
            MediaHotkeyAction.SeekForward,
            "hotkeySeekForward",
            "seekForward",
            "前进 5 秒",
            "Ctrl+Alt+Shift+Right",
            RegistrationIdBase + (int)MediaHotkeyAction.SeekForward,
            settings => settings.SeekForward,
            (settings, value) => settings.SeekForward = value),
        new(
            MediaHotkeyAction.ToggleLyricsVisibility,
            "hotkeyToggleLyricsVisibility",
            "toggleLyricsVisibility",
            "显示 / 隐藏歌词",
            "Alt+Shift+D",
            RegistrationIdBase + (int)MediaHotkeyAction.ToggleLyricsVisibility,
            settings => settings.ToggleLyricsVisibility,
            (settings, value) => settings.ToggleLyricsVisibility = value),
        new(
            MediaHotkeyAction.ToggleTranslation,
            "hotkeyToggleTranslation",
            "toggleTranslation",
            "开启/关闭翻译",
            "Alt+Shift+T",
            RegistrationIdBase + (int)MediaHotkeyAction.ToggleTranslation,
            settings => settings.ToggleTranslation,
            (settings, value) => settings.ToggleTranslation = value),
        new(
            MediaHotkeyAction.ToggleWordScanning,
            "hotkeyToggleWordScanning",
            "toggleWordScanning",
            "开启/关闭逐词扫描",
            "Alt+Shift+S",
            RegistrationIdBase + (int)MediaHotkeyAction.ToggleWordScanning,
            settings => settings.ToggleWordScanning,
            (settings, value) => settings.ToggleWordScanning = value)
    ];

    public static MediaHotkeyDefinition Get(MediaHotkeyAction action) =>
        Definitions.First(definition => definition.Action == action);
}
