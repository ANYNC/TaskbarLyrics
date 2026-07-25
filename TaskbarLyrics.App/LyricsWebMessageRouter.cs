using System.Text.Json;

namespace TaskbarLyrics.App;

internal sealed class LyricsWebMessage
{
    public int Version { get; set; }

    public string? Type { get; set; }

    public JsonElement? Payload { get; set; }
}

internal static class LyricsWebMessageRouter
{
    public static LyricsWebMessage? Parse(string? messageJson)
    {
        if (string.IsNullOrWhiteSpace(messageJson))
        {
            return null;
        }

        try
        {
            var message = JsonSerializer.Deserialize<LyricsWebMessage>(messageJson, SettingsWebJson.Options);
            return message is { Version: SettingsWebMessage.CurrentVersion } &&
                   !string.IsNullOrWhiteSpace(message.Type)
                ? message
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
