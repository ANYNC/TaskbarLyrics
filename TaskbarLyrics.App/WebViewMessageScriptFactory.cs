using System.Text.Json;

namespace TaskbarLyrics.App;

internal static class WebViewMessageScriptFactory
{
    private const int ProtocolVersion = 1;

    public static string Dispatch(string receiver, string type, object? payload)
    {
        var message = new
        {
            version = ProtocolVersion,
            type,
            payload
        };
        return $"window.{receiver}?.receive({JsonSerializer.Serialize(message)});";
    }
}
