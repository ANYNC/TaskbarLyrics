using System.Windows.Media;

namespace TaskbarLyrics.App;
/// <summary>
/// Hit surface for the floating lyrics window. WPF renders a layered window with
/// <c>AllowsTransparency</c> as a per-pixel alpha surface, and Windows skips mouse
/// input on fully transparent pixels. A single-step alpha fill makes the whole window
/// hit-testable while staying visually invisible.
/// </summary>
internal static class FloatingWindowHitSurface
{
    internal static readonly SolidColorBrush Fill = CreateFill();

    /// <summary>
    /// Whether the surface has to keep a hittable pixel under the pointer. An embedded
    /// surface paints the lyrics area itself, and a click-through surface never receives
    /// the mouse, so only a floating, non-click-through surface is tracked.
    /// </summary>
    internal static bool ShouldTrackPointer(bool useFloatingWindow, bool clickThrough) =>
        useFloatingWindow && !clickThrough;

    /// <summary>
    /// Physical-pixel containment. Screen coordinates are negative left of the primary
    /// monitor's origin, so the comparison is deliberately not clamped to positive values.
    /// </summary>
    internal static bool ContainsPoint(int left, int top, int right, int bottom, int x, int y) =>
        x >= left && x < right && y >= top && y < bottom;

    private static SolidColorBrush CreateFill()
    {
        // Alpha 1 out of 255 is the smallest fill Windows still hit-tests, and it darkens
        // the whole window by one level — which is why the fill is only held while the
        // cursor is over the surface (FloatingWindowHitSurfaceController).
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));
        brush.Freeze();
        return brush;
    }
}
