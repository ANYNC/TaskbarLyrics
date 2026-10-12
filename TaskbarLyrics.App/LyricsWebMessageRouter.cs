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

    public static bool IsWindowDragRequest(WebViewMessage? message) =>
        string.Equals(message?.Type, "windowDrag", StringComparison.Ordinal);

    /// <summary>
    /// The lyrics surface reports that it applied the requested reveal direction; the host
    /// then starts growing the window.
    /// </summary>
    public static bool IsFloatingPanelLayoutApplied(WebViewMessage? message) =>
        string.Equals(message?.Type, "floatingPanelLayoutApplied", StringComparison.Ordinal);

    /// <summary>
    /// Panel size request from the floating lyrics surface. The height must be finite
    /// and positive because the host sizes its native window from it.
    /// </summary>
    public static bool TryGetFloatingPanelToggle(
        WebViewMessage? message,
        out bool open,
        out double panelHeight,
        out double inset,
        out double gap)
    {
        open = false;
        panelHeight = 0;
        inset = 0;
        gap = 0;
        if (message?.Type != "floatingPanelToggle" ||
            message.Payload is not { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty("open", out var openElement) ||
            openElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !TryReadNumber(payload, "height", out panelHeight) ||
            !TryReadNumber(payload, "inset", out inset) ||
            !TryReadNumber(payload, "gap", out gap) ||
            panelHeight is <= 0 or > 400 ||
            inset is < 0 or > 40 ||
            gap is < 0 or > 40)
        {
            return false;
        }

        open = openElement.ValueKind == JsonValueKind.True;
        return true;
    }

    public static bool TryGetFloatingPanelAction(WebViewMessage? message, out string action)
    {
        action = string.Empty;
        if (message?.Type != "floatingPanelAction" ||
            message.Payload is not { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty("action", out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        action = element.GetString() ?? string.Empty;
        return action is "previous" or "next" or "playPause" or "seek";
    }

    public static bool TryGetFloatingPanelSeek(WebViewMessage? message, out TimeSpan position)
    {
        position = TimeSpan.Zero;
        if (message?.Type != "floatingPanelSeek" ||
            message.Payload is not { ValueKind: JsonValueKind.Object } payload ||
            !TryReadNumber(payload, "position", out var seconds) ||
            seconds < 0)
        {
            return false;
        }

        position = TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds));
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
