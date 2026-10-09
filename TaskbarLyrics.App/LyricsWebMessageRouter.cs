using System.Text.Json;
using System.Windows;
using Size = System.Windows.Size;

namespace TaskbarLyrics.App;

internal readonly record struct LyricsCoverClickBounds(
    double X, double Y, double Width, double Height, double ViewportWidth, double ViewportHeight)
{
    public Rect InHost(Size hostSize) => new(
        X * hostSize.Width / ViewportWidth,
        Y * hostSize.Height / ViewportHeight,
        Width * hostSize.Width / ViewportWidth,
        Height * hostSize.Height / ViewportHeight);
}

internal static class LyricsWebMessageRouter
{
    public static WebViewMessage? Parse(string? messageJson)
    {
        return WebViewMessageRouter.Parse(messageJson);
    }

    public static bool TryGetCoverClick(WebViewMessage? message, out LyricsCoverClickBounds bounds)
    {
        bounds = default;
        if (message?.Type != "coverClick" || message.Payload is not { ValueKind: JsonValueKind.Object } payload ||
            !TryReadNumber(payload, "x", out var x) ||
            !TryReadNumber(payload, "y", out var y) ||
            !TryReadNumber(payload, "width", out var width) ||
            !TryReadNumber(payload, "height", out var height) ||
            !TryReadNumber(payload, "viewportWidth", out var viewportWidth) ||
            !TryReadNumber(payload, "viewportHeight", out var viewportHeight) ||
            x < 0 || y < 0 || width <= 0 || height <= 0 ||
            viewportWidth <= 0 || viewportHeight <= 0 ||
            x + width > viewportWidth + 1 || y + height > viewportHeight + 1)
        {
            return false;
        }

        bounds = new LyricsCoverClickBounds(x, y, width, height, viewportWidth, viewportHeight);
        return true;
    }

    private static bool TryReadNumber(JsonElement payload, string name, out double value)
    {
        value = 0;
        return payload.TryGetProperty(name, out var element) &&
               element.ValueKind == JsonValueKind.Number &&
               element.TryGetDouble(out value) &&
               double.IsFinite(value);
    }
}
