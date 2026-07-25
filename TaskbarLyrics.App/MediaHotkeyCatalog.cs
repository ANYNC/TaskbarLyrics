namespace TaskbarLyrics.App;

internal sealed record MediaHotkeyDefinition(
    MediaHotkeyAction Action,
    string SettingKey,
    string StatusKey,
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
            "Ctrl+Shift+P",
            RegistrationIdBase + (int)MediaHotkeyAction.TogglePlayPause,
            settings => settings.TogglePlayPause,
            (settings, value) => settings.TogglePlayPause = value),
        new(
            MediaHotkeyAction.PreviousTrack,
            "hotkeyPreviousTrack",
            "previousTrack",
            "Ctrl+Shift+Left",
            RegistrationIdBase + (int)MediaHotkeyAction.PreviousTrack,
            settings => settings.PreviousTrack,
            (settings, value) => settings.PreviousTrack = value),
        new(
            MediaHotkeyAction.NextTrack,
            "hotkeyNextTrack",
            "nextTrack",
            "Ctrl+Shift+Right",
            RegistrationIdBase + (int)MediaHotkeyAction.NextTrack,
            settings => settings.NextTrack,
            (settings, value) => settings.NextTrack = value),
        new(
            MediaHotkeyAction.SeekBackward,
            "hotkeySeekBackward",
            "seekBackward",
            "Ctrl+Alt+Shift+Left",
            RegistrationIdBase + (int)MediaHotkeyAction.SeekBackward,
            settings => settings.SeekBackward,
            (settings, value) => settings.SeekBackward = value),
        new(
            MediaHotkeyAction.SeekForward,
            "hotkeySeekForward",
            "seekForward",
            "Ctrl+Alt+Shift+Right",
            RegistrationIdBase + (int)MediaHotkeyAction.SeekForward,
            settings => settings.SeekForward,
            (settings, value) => settings.SeekForward = value),
        new(
            MediaHotkeyAction.ToggleLyricsVisibility,
            "hotkeyToggleLyricsVisibility",
            "toggleLyricsVisibility",
            "Ctrl+Shift+D",
            RegistrationIdBase + (int)MediaHotkeyAction.ToggleLyricsVisibility,
            settings => settings.ToggleLyricsVisibility,
            (settings, value) => settings.ToggleLyricsVisibility = value)
    ];

    public static MediaHotkeyDefinition Get(MediaHotkeyAction action) =>
        Definitions.First(definition => definition.Action == action);
}
