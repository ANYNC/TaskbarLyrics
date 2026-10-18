using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TaskbarLyrics.App;

internal static class NativeWindowInteraction
{
    private const int WindowMessageNonClientLeftButtonDown = 0x00A1;
    private const int HitTestCaption = 2;
    private const int HitTestLeft = 10;
    private const int HitTestRight = 11;
    private const int HitTestTop = 12;
    private const int HitTestTopLeft = 13;
    private const int HitTestTopRight = 14;
    private const int HitTestBottom = 15;
    private const int HitTestBottomLeft = 16;
    private const int HitTestBottomRight = 17;

    internal static void SetClickThrough(IntPtr hwnd, bool enabled)
    {
        // Layered WPF windows need WS_EX_TRANSPARENT for cross-thread input passthrough.
        const long transparent = 0x00000020;
        var style = TaskbarNativeMethods.GetWindowLongPtr(hwnd, TaskbarNativeMethods.GWL_EXSTYLE).ToInt64();
        var next = enabled ? style | transparent : style & ~transparent;
        if (next != style) TaskbarNativeMethods.SetWindowLongPtr(hwnd, TaskbarNativeMethods.GWL_EXSTYLE, new IntPtr(next));
    }

    internal static Rect GetScreenBounds(FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0 ||
            PresentationSource.FromVisual(element) is null) return Rect.Empty;
        return new Rect(element.PointToScreen(new System.Windows.Point()),
            element.PointToScreen(new System.Windows.Point(element.ActualWidth, element.ActualHeight)));
    }

    internal static System.Windows.Point? GetCursorPosition() => GetCursorPos(out var point)
        ? new System.Windows.Point(point.X, point.Y) : null;

    public static void BeginDrag(Window window)
    {
        BeginNonClientInteraction(window, HitTestCaption);
    }

    public static void BeginResize(Window window, string? edge)
    {
        if (window.WindowState == WindowState.Maximized)
        {
            return;
        }

        var hitTest = edge switch
        {
            "left" => HitTestLeft,
            "right" => HitTestRight,
            "top" => HitTestTop,
            "topLeft" => HitTestTopLeft,
            "topRight" => HitTestTopRight,
            "bottom" => HitTestBottom,
            "bottomLeft" => HitTestBottomLeft,
            "bottomRight" => HitTestBottomRight,
            _ => 0
        };

        if (hitTest != 0)
        {
            BeginNonClientInteraction(window, hitTest);
        }
    }

    private static void BeginNonClientInteraction(Window window, int hitTest)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        _ = ReleaseCapture();
        var position = GetCursorPos(out var cursor) ? (cursor.Y << 16) | (cursor.X & 0xffff) : 0;
        _ = SendMessage(hwnd, WindowMessageNonClientLeftButtonDown, hitTest, position);
    }

    [DllImport("user32.dll", PreserveSig = true)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out TaskbarNativeMethods.NativePoint point);

    [DllImport("user32.dll", PreserveSig = true)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, int wParam, int lParam);
}
