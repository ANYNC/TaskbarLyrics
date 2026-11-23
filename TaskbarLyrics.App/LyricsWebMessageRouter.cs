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
    public static bool TryGetLyricBackground(WebViewMessage? message, out LyricBackgroundRequest request)
    {
        request = null!;
        if (message is not { Version: 1, Type: "lyricBackground", Payload: { ValueKind: JsonValueKind.Object } payload } ||
            !payload.TryGetProperty("region", out var regionElement) || !TryReadNormalizedRect(regionElement, out var region) ||
            !TryReadRects(payload, "text", region, out var text) ||
            !TryReadRects(payload, "ink", region, out var ink))
        {
            return false;
        }

        var textBounds = new LyricBackgroundText[text.Length];
        var index = 0;
        foreach (var entry in payload.GetProperty("text").EnumerateArray())
        {
            if (!TryReadNumber(entry, "opacity", out var opacity) || opacity <= 0 || opacity > 1) return false;
            textBounds[index] = new LyricBackgroundText(text[index], opacity);
            index++;
        }

        request = new LyricBackgroundRequest(region, textBounds, ink);
        return true;
    }

    private static bool TryReadRects(JsonElement payload, string property, Rect region, out Rect[] result)
    {
        result = [];
        if (!payload.TryGetProperty(property, out var entries) || entries.ValueKind != JsonValueKind.Array ||
            entries.GetArrayLength() > 4)
        {
            return false;
        }

        result = new Rect[entries.GetArrayLength()];
        var index = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            if (!TryReadNormalizedRect(entry, out var rect) || !rect.IntersectsWith(region)) return false;
            result[index++] = Rect.Intersect(rect, region);
        }

        return true;
    }

    private static bool TryReadNormalizedRect(JsonElement element, out Rect rect)
    {
        rect = Rect.Empty;
        if (element.ValueKind != JsonValueKind.Object ||
            !TryReadNumber(element, "x", out var x) || !TryReadNumber(element, "y", out var y) ||
            !TryReadNumber(element, "width", out var width) || !TryReadNumber(element, "height", out var height) ||
            x < 0 || x >= 1 || y < 0 || y >= 1 || width <= 0 || height <= 0 ||
            x + width > 1.000001 || y + height > 1.000001)
        {
            return false;
        }

        rect = new Rect(x, y, Math.Min(width, 1 - x), Math.Min(height, 1 - y));
        return rect.Width > 0 && rect.Height > 0;
    }

    public static bool TryGetRegionVisibilityCompleted(WebViewMessage? message, out int generation, out bool visible)
    {
        generation = 0;
        visible = false;
        if (message is not { Version: 1, Type: "regionVisibilityCompleted", Payload: { ValueKind: JsonValueKind.Object } payload } ||
            !payload.TryGetProperty("generation", out var token) || token.ValueKind != JsonValueKind.Number ||
            !token.TryGetInt32(out generation) || generation < 0 ||
            !payload.TryGetProperty("visible", out var state) || state.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        visible = state.GetBoolean();
        return true;
    }

    public static bool TryGetControlPanelTrigger(WebViewMessage? message, out LyricsCoverClickBounds bounds,
        out ControlPanelTriggerArea area, out ControlPanelActivation activation)
    {
        area = ControlPanelTriggerArea.Cover;
        activation = ControlPanelActivation.Click;
        if (TryGetCoverClick(message, out bounds)) return true;
        return message?.Type == "controlPanelTrigger" &&
            message.Payload is { ValueKind: JsonValueKind.Object } payload &&
            TryReadEnum(payload, "area", out area) &&
            TryReadEnum(payload, "activation", out activation) &&
            TryReadBounds(payload, out bounds);
    }

    private static bool TryReadEnum<T>(JsonElement payload, string name, out T value) where T : struct, Enum
    {
        value = default;
        return payload.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String &&
            Enum.TryParse(element.GetString(), out value) && Enum.IsDefined(value) &&
            element.GetString() == value.ToString();
    }

    public static WebViewMessage? Parse(string? messageJson)
    {
        return WebViewMessageRouter.Parse(messageJson);
    }

    public static bool TryGetCoverClick(WebViewMessage? message, out LyricsCoverClickBounds bounds)
    {
        bounds = default;
        return message?.Type == "coverClick" && message.Payload is { ValueKind: JsonValueKind.Object } payload &&
            TryReadBounds(payload, out bounds);
    }

    private static bool TryReadBounds(JsonElement payload, out LyricsCoverClickBounds bounds)
    {
        bounds = default;
        if (!TryReadNumber(payload, "x", out var x) ||
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
