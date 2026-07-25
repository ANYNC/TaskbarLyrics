using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarLyrics.App;

internal sealed class SettingsWebMessage
{
    public const int CurrentVersion = 1;

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
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}

internal sealed class SettingsWebMessageRouter
{
    public SettingsWebMessage? Parse(string? messageJson)
    {
        if (string.IsNullOrWhiteSpace(messageJson))
        {
            return null;
        }

        try
        {
            var message = JsonSerializer.Deserialize<SettingsWebMessage>(messageJson, SettingsWebJson.Options);
            if (message is null || message.Version != SettingsWebMessage.CurrentVersion ||
                string.IsNullOrWhiteSpace(message.Type))
            {
                return null;
            }

            if (!string.Equals(message.Type, "update", StringComparison.Ordinal) ||
                message.Payload is not { ValueKind: JsonValueKind.Object } payload ||
                !payload.TryGetProperty("key", out var keyElement) ||
                !payload.TryGetProperty("value", out var valueElement))
            {
                return new SettingsWebMessage
                {
                    Version = message.Version,
                    Type = message.Type,
                    Payload = message.Payload,
                    Value = message.Payload is JsonElement value ? value.Clone() : null
                };
            }

            return new SettingsWebMessage
            {
                Version = message.Version,
                Type = message.Type,
                Payload = message.Payload,
                Key = keyElement.GetString(),
                Value = valueElement.Clone()
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
