using System.Buffers;
using System.Runtime.InteropServices;

namespace TaskbarLyrics.App;

internal interface ISmartTopmostWindowProbe
{
    bool TryGetForegroundWindow(out SmartTopmostForegroundWindow foregroundWindow);
    SmartTopmostDecision GetDesktopPlacement();
}

internal sealed class SmartTopmostWindowProbe : ISmartTopmostWindowProbe
{
    private const uint DwmwaExtendedFrameBounds = 9;
    private const uint GaRoot = 2;
    private static readonly uint CurrentProcessId = (uint)Environment.ProcessId;
    private bool _showingDesktop;

    public SmartTopmostDecision GetDesktopPlacement()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        // Retain the desktop state while the user operates our owned controls. A normal app,
        // including our settings window, returns the card to the bottom of the application band.
        if (foreground != IntPtr.Zero && !IsOwnToolWindow(foreground))
            _showingDesktop = IsDesktopOrShellWindow(foreground, desktopOnly: true);

        // Absolute band handles avoid access-denied errors when an external HWND is protected.
        // Win+D raises Explorer over ordinary windows, so briefly use the topmost band on desktop.
        return new SmartTopmostDecision(_showingDesktop,
            _showingDesktop ? IntPtr.Zero : TaskbarNativeMethods.HWND_BOTTOM, Reassert: true);
    }

    private static bool IsOwnToolWindow(IntPtr hwnd) =>
        NativeMethods.GetWindowProcessId(hwnd) == CurrentProcessId &&
        (TaskbarNativeMethods.GetWindowLongPtr(hwnd, TaskbarNativeMethods.GWL_EXSTYLE).ToInt64() & TaskbarNativeMethods.WS_EX_TOOLWINDOW) != 0;

    public bool TryGetForegroundWindow(out SmartTopmostForegroundWindow foregroundWindow)
    {
        foregroundWindow = default;
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return false;
        }

        if (!TryGetVisibleBounds(hwnd, out var visibleBounds))
        {
            return false;
        }

        var processId = NativeMethods.GetWindowProcessId(hwnd);
        foregroundWindow = new SmartTopmostForegroundWindow(
            hwnd,
            visibleBounds,
            NativeMethods.IsWindowVisible(hwnd),
            NativeMethods.IsIconic(hwnd),
            processId == CurrentProcessId,
            IsDesktopOrShellWindow(hwnd));
        return true;
    }

    private static bool TryGetVisibleBounds(IntPtr hwnd, out NativeRect bounds)
    {
        if (NativeMethods.DwmGetWindowAttribute(
                hwnd,
                DwmwaExtendedFrameBounds,
                out var extendedBounds,
                Marshal.SizeOf<NativeMethods.NativeRect>()) == 0 &&
            extendedBounds.Right > extendedBounds.Left &&
            extendedBounds.Bottom > extendedBounds.Top)
        {
            bounds = new NativeRect(
                extendedBounds.Left,
                extendedBounds.Top,
                extendedBounds.Right,
                extendedBounds.Bottom);
            return true;
        }

        if (NativeMethods.GetWindowRect(hwnd, out var windowBounds) &&
            windowBounds.Right > windowBounds.Left &&
            windowBounds.Bottom > windowBounds.Top)
        {
            bounds = new NativeRect(
                windowBounds.Left,
                windowBounds.Top,
                windowBounds.Right,
                windowBounds.Bottom);
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool IsDesktopOrShellWindow(IntPtr hwnd, bool desktopOnly = false)
    {
        var desktop = NativeMethods.GetDesktopWindow();
        var shell = NativeMethods.GetShellWindow();
        if (hwnd == desktop || hwnd == shell)
        {
            return true;
        }

        var root = NativeMethods.GetAncestor(hwnd, GaRoot);
        if (root == desktop || root == shell)
        {
            return true;
        }

        var classNameBuffer = ArrayPool<char>.Shared.Rent(256);
        try
        {
            var length = NativeMethods.GetClassName(hwnd, classNameBuffer, classNameBuffer.Length);
            if (length == 0)
            {
                return false;
            }

            var className = classNameBuffer.AsSpan(0, length);
            return className.SequenceEqual("Progman") ||
                className.SequenceEqual("WorkerW") ||
                (!desktopOnly && (className.SequenceEqual("Shell_TrayWnd") ||
                className.SequenceEqual("Shell_SecondaryTrayWnd") ||
                className.SequenceEqual("DV2ControlHost") ||
                className.SequenceEqual("XamlExplorerHostIslandWindow")));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(classNameBuffer);
        }
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeRect
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        internal static uint GetWindowProcessId(IntPtr hwnd)
        {
            return GetWindowThreadProcessId(hwnd, out var processId) == 0
                ? 0
                : processId;
        }

        [DllImport("user32.dll")]
        internal static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        internal static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(
            IntPtr hwnd,
            [Out] char[] className,
            int maxCount);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(
            IntPtr hwnd,
            uint attribute,
            out NativeRect value,
            int valueSize);
    }
}
