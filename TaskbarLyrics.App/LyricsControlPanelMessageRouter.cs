using System.Text.Json;

namespace TaskbarLyrics.App;

internal enum LyricsControlPanelCommandKind
{
    Dismiss,
    OpenSettings,
    ToggleTranslation,
    Previous,
    TogglePlayPause,
    Next,
    Seek,
    SetVolume,
    ToggleMute
}

internal readonly record struct LyricsControlPanelCommand(LyricsControlPanelCommandKind Kind, long Value = 0);

internal static class LyricsControlPanelMessageRouter
{
    internal static bool TryParse(WebViewMessage? message, out LyricsControlPanelCommand command)
    {
        command = default;
        if (message?.Version != WebViewMessageRouter.CurrentVersion ||
            message.Payload is not { ValueKind: JsonValueKind.Object } payload)
        {
            return false;
        }

        switch (message.Type)
        {
            case "dismiss":
            case "openSettings":
            case "toggleTranslation":
            case "toggleMute":
                if (payload.EnumerateObject().Any()) return false;
                command = new LyricsControlPanelCommand(message.Type switch
                {
                    "dismiss" => LyricsControlPanelCommandKind.Dismiss,
                    "openSettings" => LyricsControlPanelCommandKind.OpenSettings,
                    "toggleTranslation" => LyricsControlPanelCommandKind.ToggleTranslation,
                    _ => LyricsControlPanelCommandKind.ToggleMute
                });
                return true;
            case "mediaAction":
                if (!payload.TryGetProperty("action", out var action) ||
                    action.ValueKind != JsonValueKind.String ||
                    payload.EnumerateObject().Count() != 1)
                {
                    return false;
                }

                var mediaAction = action.GetString() switch
                {
                    "previous" => LyricsControlPanelCommandKind.Previous,
                    "toggle" => LyricsControlPanelCommandKind.TogglePlayPause,
                    "next" => LyricsControlPanelCommandKind.Next,
                    _ => (LyricsControlPanelCommandKind?)null
                };
                if (!mediaAction.HasValue) return false;
                command = new LyricsControlPanelCommand(mediaAction.Value);
                return true;
            case "seek":
            case "setVolume":
                var name = message.Type == "seek" ? "positionMs" : "level";
                if (!payload.TryGetProperty(name, out var number) ||
                    number.ValueKind != JsonValueKind.Number ||
                    !number.TryGetInt64(out var value) ||
                    payload.EnumerateObject().Count() != 1 ||
                    (message.Type == "seek" && value < 0) ||
                    (message.Type == "setVolume" && value is < 0 or > 100))
                {
                    return false;
                }

                command = new LyricsControlPanelCommand(
                    message.Type == "seek" ? LyricsControlPanelCommandKind.Seek : LyricsControlPanelCommandKind.SetVolume,
                    value);
                return true;
            default:
                return false;
        }
    }
}
