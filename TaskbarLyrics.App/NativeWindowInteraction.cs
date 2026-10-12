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
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExNoActivate = 0x08000000;

    internal const int WindowMessageExitSizeMove = 0x0232;

    public static void BeginDrag(Window window)
    {
        NonClientInteractionActive = true;
        BeginNonClientInteraction(window, HitTestCaption);
    }

    /// <summary>
    /// True while the OS owns a move/resize loop. Re-anchoring during that loop would
    /// fight the user's gesture (the window snaps back to its stale bounds).
    /// </summary>
    internal static bool NonClientInteractionActive { get; private set; }

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

    /// <summary>Called from the window procedure when the OS move/resize loop ends.</summary>
    internal static void EndNonClientInteraction() => NonClientInteractionActive = false;

    /// <summary>
    /// Computes the extended style that makes a window ignore mouse input and, for the
    /// top-level host, activation as well.
    /// </summary>
    internal static long ApplyClickThroughStyle(long current, bool includeNoActivate) =>
        current | ClickThroughMask(includeNoActivate);

    /// <summary>
    /// Reports whether a window still carries exactly the styles this controller added,
    /// so restoring the recorded style cannot undo a change made by another component
    /// (the taskbar embedding anchor also owns extended styles on the same window).
    /// </summary>
    internal static bool ShouldRestoreClickThroughStyle(long current, long original, bool includeNoActivate) =>
        current == (original | ClickThroughMask(includeNoActivate));

    internal static long ClickThroughMask(bool includeNoActivate) =>
        includeNoActivate ? WsExTransparent | WsExNoActivate : WsExTransparent;

    [DllImport("user32.dll", PreserveSig = true)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", PreserveSig = true)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, int wParam, int lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

    internal const int ExtendedStyleIndex = GwlExStyle;

    internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);
}

/// <summary>
/// Toggles click-through for a window and every child HWND it hosts. The controller
/// remembers the extended styles it changed so disabling click-through restores exactly
/// those windows instead of clearing style bits owned by other components.
/// </summary>
internal sealed class WindowClickThroughController(Window window)
{
    private readonly Dictionary<IntPtr, long> _originalExtendedStyles = [];

    public void Apply(bool enabled)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var handles = new List<IntPtr> { hwnd };
        _ = NativeWindowInteraction.EnumChildWindows(hwnd, (child, _) =>
        {
            handles.Add(child);
            return true;
        }, IntPtr.Zero);

        if (enabled)
        {
            foreach (var handle in handles)
            {
                var includeNoActivate = handle == hwnd;
                var current = NativeWindowInteraction.GetWindowLongPtr(handle, NativeWindowInteraction.ExtendedStyleIndex).ToInt64();
                _originalExtendedStyles.TryAdd(handle, current);
                var next = NativeWindowInteraction.ApplyClickThroughStyle(current, includeNoActivate);
                if (next != current)
                {
                    _ = NativeWindowInteraction.SetWindowLongPtr(handle, NativeWindowInteraction.ExtendedStyleIndex, new IntPtr(next));
                }
            }

            return;
        }

        foreach (var (handle, original) in _originalExtendedStyles)
        {
            var includeNoActivate = handle == hwnd;
            var current = NativeWindowInteraction.GetWindowLongPtr(handle, NativeWindowInteraction.ExtendedStyleIndex).ToInt64();
            if (NativeWindowInteraction.ShouldRestoreClickThroughStyle(current, original, includeNoActivate))
            {
                _ = NativeWindowInteraction.SetWindowLongPtr(handle, NativeWindowInteraction.ExtendedStyleIndex, new IntPtr(original));
            }
        }

        _originalExtendedStyles.Clear();
    }
}
