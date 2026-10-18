using Point = System.Windows.Point;

namespace TaskbarLyrics.App;

internal readonly record struct FloatingWindowSize(double Width, double ScalePercent)
{
    internal static FloatingWindowSize FromSettings(AppSettings settings) => new(settings.FloatingWindowWidth, settings.FloatingLyricsLayoutScalePercent);

    internal AppSettings ApplyTo(AppSettings settings)
    {
        var result = settings.Clone();
        result.FloatingWindowWidth = Width;
        result.FloatingLyricsLayoutScalePercent = ScalePercent;
        return result;
    }
}

internal sealed class FloatingWindowSizeGesture(FloatingWindowSize initial, Point startPixels,
    double pixelsPerDip, double initialWidthInDips, double maxWidthInDips)
{
    internal FloatingWindowSize Initial => initial;

    internal FloatingWindowSize Calculate(Point positionPixels)
    {
        var delta = (positionPixels - startPixels) / pixelsPerDip;
        if (!double.IsFinite(delta.X) || !double.IsFinite(delta.Y)) return initial;
        if (delta.X == 0 && delta.Y == 0) return initial;
        var width = delta.X == 0 ? initial.Width
            : AppSettings.ClampEffectiveWindowWidth(initialWidthInDips + delta.X, maxWidthInDips);
        var scale = AppSettings.ClampLyricsLayoutScalePercent(Math.Round(initial.ScalePercent - delta.Y));
        return new(width, scale);
    }
}
