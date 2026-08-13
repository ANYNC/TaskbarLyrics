using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarLyrics.App;

internal sealed class WebViewMessage
{
    public int Version { get; set; }

    public string? Type { get; set; }

    public JsonElement? Payload { get; set; }
}

internal static class WebViewMessageRouter
{
    public const int CurrentVersion = 1;

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static WebViewMessage? Parse(string? messageJson)
    {
        if (string.IsNullOrWhiteSpace(messageJson))
        {
            return null;
        }

        try
        {
            var message = JsonSerializer.Deserialize<WebViewMessage>(messageJson, JsonOptions);
            return message is { Version: CurrentVersion } &&
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
