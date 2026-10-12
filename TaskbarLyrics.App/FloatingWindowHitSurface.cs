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
    internal static readonly System.Windows.Media.Brush Fill = CreateFill();

    private static System.Windows.Media.Brush CreateFill()
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));
        brush.Freeze();
        return brush;
    }
}
