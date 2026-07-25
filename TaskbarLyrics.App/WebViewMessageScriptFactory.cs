using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarLyrics.App;

internal static class WebViewMessageScriptFactory
{
    private const int ProtocolVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Dispatch(string receiver, string type, object? payload)
    {
        var message = new
        {
            version = ProtocolVersion,
            type,
            payload
        };
        return $"window.{receiver}?.receive({JsonSerializer.Serialize(message, SerializerOptions)});";
    }
}
