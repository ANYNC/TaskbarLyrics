namespace TaskbarLyrics.App;

internal sealed record MediaHotkeyDefinition(
    MediaHotkeyAction Action,
    string SettingKey,
    string StatusKey,
    string DisplayName,
    string Description,
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
            "根据当前播放状态自动执行播放或暂停。",
            "Ctrl+Shift+P",
            RegistrationIdBase + (int)MediaHotkeyAction.TogglePlayPause,
            settings => settings.TogglePlayPause,
            (settings, value) => settings.TogglePlayPause = value),
        new(
            MediaHotkeyAction.PreviousTrack,
            "hotkeyPreviousTrack",
            "previousTrack",
            "上一首",
            "切换到所选播放器的上一首歌曲。",
            "Ctrl+Shift+Left",
            RegistrationIdBase + (int)MediaHotkeyAction.PreviousTrack,
            settings => settings.PreviousTrack,
            (settings, value) => settings.PreviousTrack = value),
        new(
            MediaHotkeyAction.NextTrack,
            "hotkeyNextTrack",
            "nextTrack",
            "下一首",
            "切换到所选播放器的下一首歌曲。",
            "Ctrl+Shift+Right",
            RegistrationIdBase + (int)MediaHotkeyAction.NextTrack,
            settings => settings.NextTrack,
            (settings, value) => settings.NextTrack = value),
        new(
            MediaHotkeyAction.SeekBackward,
            "hotkeySeekBackward",
            "seekBackward",
            "后退 5 秒",
            "从当前播放进度向后跳转 5 秒。",
            "Ctrl+Alt+Shift+Left",
            RegistrationIdBase + (int)MediaHotkeyAction.SeekBackward,
            settings => settings.SeekBackward,
            (settings, value) => settings.SeekBackward = value),
        new(
            MediaHotkeyAction.SeekForward,
            "hotkeySeekForward",
            "seekForward",
            "前进 5 秒",
            "从当前播放进度向前跳转 5 秒。",
            "Ctrl+Alt+Shift+Right",
            RegistrationIdBase + (int)MediaHotkeyAction.SeekForward,
            settings => settings.SeekForward,
            (settings, value) => settings.SeekForward = value),
        new(
            MediaHotkeyAction.ToggleLyricsVisibility,
            "hotkeyToggleLyricsVisibility",
            "toggleLyricsVisibility",
            "显示 / 隐藏歌词",
            "切换任务栏歌词窗口的可见状态。",
            "Ctrl+Shift+D",
            RegistrationIdBase + (int)MediaHotkeyAction.ToggleLyricsVisibility,
            settings => settings.ToggleLyricsVisibility,
            (settings, value) => settings.ToggleLyricsVisibility = value)
    ];

    public static MediaHotkeyDefinition Get(MediaHotkeyAction action) =>
        Definitions.First(definition => definition.Action == action);
}
