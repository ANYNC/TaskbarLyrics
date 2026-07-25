using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarLyrics.App;

internal sealed class SettingsWebMessage
{
    public string? Type { get; set; }

    public string? Key { get; set; }

    public JsonElement? Value { get; set; }
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
        return string.IsNullOrWhiteSpace(messageJson)
            ? null
            : JsonSerializer.Deserialize<SettingsWebMessage>(messageJson, SettingsWebJson.Options);
    }
}
