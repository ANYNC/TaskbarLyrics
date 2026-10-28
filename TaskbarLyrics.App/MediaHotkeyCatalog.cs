namespace TaskbarLyrics.App;

internal sealed record MediaHotkeyDefinition(
    MediaHotkeyAction Action,
    string SettingKey,
    string StatusKey,
    string DisplayName,
    string DefaultBinding,
    int RegistrationId,
    Func<GlobalMediaHotkeySettings, string> ReadBinding,
    Action<GlobalMediaHotkeySettings, string> WriteBinding)
{
    public string EnabledSettingKey => SettingKey + "Enabled";
}

internal static class MediaHotkeyCatalog
{
    private const int RegistrationIdBase = 0x5A00;

    public static IReadOnlyList<MediaHotkeyDefinition> Definitions { get; } =
    [
        new(
            MediaHotkeyAction.TogglePlayPause,
            "hotkeyTogglePlayPause",
            "togglePlayPause",
            Text.T("Hotkey_PlayPause"),
            "Alt+Shift+P",
            RegistrationIdBase + (int)MediaHotkeyAction.TogglePlayPause,
            settings => settings.TogglePlayPause,
            (settings, value) => settings.TogglePlayPause = value),
        new(
            MediaHotkeyAction.PreviousTrack,
            "hotkeyPreviousTrack",
            "previousTrack",
            Text.T("UI_PreviousTrack"),
            "Alt+Shift+Left",
            RegistrationIdBase + (int)MediaHotkeyAction.PreviousTrack,
            settings => settings.PreviousTrack,
            (settings, value) => settings.PreviousTrack = value),
        new(
            MediaHotkeyAction.NextTrack,
            "hotkeyNextTrack",
            "nextTrack",
            Text.T("UI_NextTrack"),
            "Alt+Shift+Right",
            RegistrationIdBase + (int)MediaHotkeyAction.NextTrack,
            settings => settings.NextTrack,
            (settings, value) => settings.NextTrack = value),
        new(
            MediaHotkeyAction.SeekBackward,
            "hotkeySeekBackward",
            "seekBackward",
            Text.T("Hotkey_Seekbackward5seconds"),
            "Ctrl+Alt+Shift+Left",
            RegistrationIdBase + (int)MediaHotkeyAction.SeekBackward,
            settings => settings.SeekBackward,
            (settings, value) => settings.SeekBackward = value),
        new(
            MediaHotkeyAction.SeekForward,
            "hotkeySeekForward",
            "seekForward",
            Text.T("Hotkey_Seekforward5seconds"),
            "Ctrl+Alt+Shift+Right",
            RegistrationIdBase + (int)MediaHotkeyAction.SeekForward,
            settings => settings.SeekForward,
            (settings, value) => settings.SeekForward = value),
        new(
            MediaHotkeyAction.ToggleLyricsVisibility,
            "hotkeyToggleLyricsVisibility",
            "toggleLyricsVisibility",
            Text.T("Hotkey_Showhidelyrics"),
            "Alt+Shift+D",
            RegistrationIdBase + (int)MediaHotkeyAction.ToggleLyricsVisibility,
            settings => settings.ToggleLyricsVisibility,
            (settings, value) => settings.ToggleLyricsVisibility = value),
        new(
            MediaHotkeyAction.ToggleTranslation,
            "hotkeyToggleTranslation",
            "toggleTranslation",
            Text.T("UI_ToggleTranslation"),
            "Alt+Shift+T",
            RegistrationIdBase + (int)MediaHotkeyAction.ToggleTranslation,
            settings => settings.ToggleTranslation,
            (settings, value) => settings.ToggleTranslation = value),
        new(
            MediaHotkeyAction.ToggleWordScanning,
            "hotkeyToggleWordScanning",
            "toggleWordScanning",
            Text.T("UI_ToggleWordHighlighting"),
            "Alt+Shift+S",
            RegistrationIdBase + (int)MediaHotkeyAction.ToggleWordScanning,
            settings => settings.ToggleWordScanning,
            (settings, value) => settings.ToggleWordScanning = value),
        new(
            MediaHotkeyAction.ToggleDisplayMode,
            "hotkeyToggleDisplayMode",
            "toggleDisplayMode",
            Text.T("Hotkey_Switchdisplaymodeembeddedfloating"),
            "Alt+Shift+C",
            RegistrationIdBase + (int)MediaHotkeyAction.ToggleDisplayMode,
            settings => settings.ToggleDisplayMode,
            (settings, value) => settings.ToggleDisplayMode = value)
    ];

    public static MediaHotkeyDefinition Get(MediaHotkeyAction action) =>
        Definitions.First(definition => definition.Action == action);
}
