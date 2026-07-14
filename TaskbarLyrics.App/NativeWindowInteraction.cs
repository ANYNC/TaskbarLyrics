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
        _ = SendMessage(hwnd, WindowMessageNonClientLeftButtonDown, hitTest, 0);
    }

    [DllImport("user32.dll", PreserveSig = true)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", PreserveSig = true)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, int wParam, int lParam);
}
