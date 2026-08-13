using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarLyrics.App;

internal sealed class SettingsWebMessage
{
    public const int CurrentVersion = WebViewMessageRouter.CurrentVersion;

    public int Version { get; set; }

    public string? Type { get; set; }

    public JsonElement? Payload { get; set; }

    [JsonIgnore]
    public string? Key { get; init; }

    [JsonIgnore]
    public JsonElement? Value { get; init; }
}

internal static class SettingsWebJson
{
    public static JsonSerializerOptions Options => WebViewMessageRouter.JsonOptions;
}

internal static class SettingsWebMessageRouter
{
    public static SettingsWebMessage? Parse(string? messageJson)
    {
        var message = WebViewMessageRouter.Parse(messageJson);
        if (message is null)
        {
            return null;
        }

        if (!IsSettingValueMessage(message.Type) ||
            message.Payload is not { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty("key", out var keyElement) ||
            !payload.TryGetProperty("value", out var valueElement))
        {
            return ToSettingsMessage(message, value: message.Payload?.Clone());
        }

        return ToSettingsMessage(
            message,
            key: keyElement.ValueKind == JsonValueKind.String ? keyElement.GetString() : null,
            value: valueElement.Clone());
    }

    private static SettingsWebMessage ToSettingsMessage(
        WebViewMessage message,
        string? key = null,
        JsonElement? value = null)
    {
        return new SettingsWebMessage
        {
            Version = message.Version,
            Type = message.Type,
            Payload = message.Payload,
            Key = key,
            Value = value
        };
    }

    private static bool IsSettingValueMessage(string? type)
    {
        return type is "update" or "previewUpdate";
    }
}
