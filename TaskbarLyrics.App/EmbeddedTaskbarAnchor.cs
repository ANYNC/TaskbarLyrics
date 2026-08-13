using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TaskbarLyrics.App;

internal readonly record struct EmbeddedTaskbarNativeBounds(int Left, int Top, int Width, int Height);

internal static class EmbeddedTaskbarLayoutCalculator
{
    public static double CalculateHorizontalLeft(
        double taskbarWidth,
        double windowWidth,
        EmbeddedTaskbarHorizontalAnchor anchor,
        double offset)
    {
        return anchor switch
        {
            EmbeddedTaskbarHorizontalAnchor.Left => offset,
            EmbeddedTaskbarHorizontalAnchor.Center => (taskbarWidth - windowWidth) / 2.0 + offset,
            _ => taskbarWidth - windowWidth + offset
        };
    }

    public static double CalculateVerticalTop(
        double taskbarHeight,
        double windowHeight,
        double offset)
    {
        return (taskbarHeight - windowHeight) / 2.0 + offset;
    }

    public static EmbeddedTaskbarNativeBounds ToTaskbarClientBounds(
        double clientLeft,
        double clientTop,
        double width,
        double height,
        double pixelsPerDip)
    {
        pixelsPerDip = double.IsFinite(pixelsPerDip) && pixelsPerDip > 0 ? pixelsPerDip : 1;
        return new EmbeddedTaskbarNativeBounds(
            AlignPhysicalPixel(clientLeft, pixelsPerDip),
            AlignPhysicalPixel(clientTop, pixelsPerDip),
            Math.Max(1, AlignPhysicalPixel(width, pixelsPerDip)),
            Math.Max(1, AlignPhysicalPixel(height, pixelsPerDip)));
    }

    private static int AlignPhysicalPixel(double value, double pixelsPerDip) =>
        checked((int)Math.Round(value * pixelsPerDip, MidpointRounding.AwayFromZero));
}

internal sealed class EmbeddedTaskbarAnchor : IDisposable
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsPopup = 0x80000000L;
    private const long WsChild = 0x40000000L;
    private const long WsVisible = 0x10000000L;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;

    private IntPtr _windowHandle;
    private IntPtr _taskbarHandle;
    private IntPtr _parentHandle;
    private IntPtr _taskListHandle;
    private TaskbarNativeMethods.NativeRect _originalTaskListRect;
    private bool _taskListSqueezed;
    private bool _disposed;

    public void Attach(Window window, AppSettings settings)
    {
        if (_disposed)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var taskbar = FindPrimaryTaskbar();
        if (taskbar == IntPtr.Zero)
        {
            return;
        }

        if (_windowHandle != hwnd || _taskbarHandle != taskbar)
        {
            Detach();
            _windowHandle = hwnd;
            _taskbarHandle = taskbar;
        }

        var isWin11 = Environment.OSVersion.Version.Major == 10 && Environment.OSVersion.Version.Build >= 22000;
        var parent = isWin11 ? taskbar : FindTaskbarContainer(taskbar);
        if (parent == IntPtr.Zero)
        {
            return;
        }

        if (_parentHandle != parent || !IsAttachedToParent(hwnd, parent))
        {
            SetParent(hwnd, parent);
            ApplyChildWindowStyle(hwnd);
            _parentHandle = parent;
        }

        if (!isWin11)
        {
            SqueezeTaskList(parent, settings);
        }

        Position(hwnd, parent, window, settings);
    }

    public void Detach()
    {
        RestoreTaskList();

        if (_windowHandle != IntPtr.Zero && IsWindow(_windowHandle))
        {
            SetParent(_windowHandle, IntPtr.Zero);
            RestoreTopLevelStyle(_windowHandle);
            TaskbarPlacementService.ApplyToolWindowStyle(_windowHandle);
        }

        _windowHandle = IntPtr.Zero;
        _taskbarHandle = IntPtr.Zero;
        _parentHandle = IntPtr.Zero;
        _taskListHandle = IntPtr.Zero;
        _taskListSqueezed = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Detach();
        _disposed = true;
    }

    private static void Position(IntPtr hwnd, IntPtr parent, Window window, AppSettings settings)
    {
        if (!GetWindowRect(parent, out var parentRect))
        {
            return;
        }

        var pixelsPerDip = TaskbarPlacementService.GetPixelsPerDip(window);
        var taskbarWidth = (parentRect.Right - parentRect.Left) / pixelsPerDip;
        var taskbarHeight = (parentRect.Bottom - parentRect.Top) / pixelsPerDip;

        var width = AppSettings.ClampEmbeddedTaskbarWidth(settings.EmbeddedTaskbarWidth);
        var height = LyricsLayoutMetrics.Create(settings, pixelsPerDip).DesiredWindowHeight;

        window.Width = width;
        window.Height = height;

        var clientLeft = EmbeddedTaskbarLayoutCalculator.CalculateHorizontalLeft(
            taskbarWidth,
            width,
            settings.EmbeddedTaskbarHorizontalAnchor,
            settings.EmbeddedTaskbarHorizontalOffset);
        var clientTop = EmbeddedTaskbarLayoutCalculator.CalculateVerticalTop(
            taskbarHeight,
            height,
            settings.EmbeddedTaskbarVerticalOffset);

        var bounds = EmbeddedTaskbarLayoutCalculator.ToTaskbarClientBounds(
            clientLeft,
            clientTop,
            width,
            height,
            pixelsPerDip);

        TaskbarNativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            TaskbarNativeMethods.SWP_NOZORDER | TaskbarNativeMethods.SWP_NOACTIVATE);
    }

    private void SqueezeTaskList(IntPtr parent, AppSettings settings)
    {
        if (_taskListHandle == IntPtr.Zero || !IsWindow(_taskListHandle))
        {
            _taskListHandle = FindTaskList(parent);
        }

        if (_taskListHandle == IntPtr.Zero || !GetWindowRect(_taskListHandle, out var taskListRect))
        {
            return;
        }

        if (!_taskListSqueezed)
        {
            _originalTaskListRect = taskListRect;
        }

        if (!GetWindowRect(parent, out var parentRect))
        {
            return;
        }

        var pixelsPerDip = GetPixelsPerDipForHandle(parent);
        var width = AppSettings.ClampEmbeddedTaskbarWidth(settings.EmbeddedTaskbarWidth);
        var windowPhysicalWidth = (int)Math.Round(width * pixelsPerDip, MidpointRounding.AwayFromZero);
        var horizontalOffsetPhysical = (int)Math.Round(
            settings.EmbeddedTaskbarHorizontalOffset * pixelsPerDip,
            MidpointRounding.AwayFromZero);

        var taskListLeft = taskListRect.Left - parentRect.Left;
        var taskListWidth = taskListRect.Right - taskListRect.Left;
        var targetWidth = Math.Max(0, taskListWidth - windowPhysicalWidth - horizontalOffsetPhysical);

        if (_taskListSqueezed && Math.Abs(targetWidth - taskListWidth) <= 1)
        {
            return;
        }

        MoveWindow(_taskListHandle, taskListLeft, 0, targetWidth, taskListRect.Bottom - taskListRect.Top, true);
        _taskListSqueezed = true;
    }

    private void RestoreTaskList()
    {
        if (!_taskListSqueezed || _taskListHandle == IntPtr.Zero || !IsWindow(_taskListHandle))
        {
            return;
        }

        MoveWindow(
            _taskListHandle,
            0,
            0,
            _originalTaskListRect.Right - _originalTaskListRect.Left,
            _originalTaskListRect.Bottom - _originalTaskListRect.Top,
            true);
        _taskListSqueezed = false;
    }

    private static bool IsAttachedToParent(IntPtr hwnd, IntPtr parent)
    {
        return GetParent(hwnd) == parent;
    }

    private static void ApplyChildWindowStyle(IntPtr hwnd)
    {
        var style = TaskbarNativeMethods.GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        var nextStyle = (style & ~WsPopup) | WsChild | WsVisible;
        if (nextStyle != style)
        {
            TaskbarNativeMethods.SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(nextStyle));
        }

        var extendedStyle = TaskbarNativeMethods.GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        var nextExtendedStyle = (extendedStyle & ~WsExToolWindow) | WsExNoActivate;
        if (nextExtendedStyle != extendedStyle)
        {
            TaskbarNativeMethods.SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(nextExtendedStyle));
        }
    }

    private static void RestoreTopLevelStyle(IntPtr hwnd)
    {
        var style = TaskbarNativeMethods.GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        var nextStyle = (style & ~WsChild) | WsPopup;
        if (nextStyle != style)
        {
            TaskbarNativeMethods.SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(nextStyle));
        }

        var extendedStyle = TaskbarNativeMethods.GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        var nextExtendedStyle = extendedStyle & ~WsExNoActivate;
        if (nextExtendedStyle != extendedStyle)
        {
            TaskbarNativeMethods.SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(nextExtendedStyle));
        }
    }

    private static double GetPixelsPerDipForHandle(IntPtr hwnd)
    {
        var dpi = TaskbarNativeMethods.GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96.0 : 1;
    }

    private static IntPtr FindPrimaryTaskbar()
    {
        return FindWindow("Shell_TrayWnd", null);
    }

    private static IntPtr FindTaskbarContainer(IntPtr taskbar)
    {
        var reBar = FindWindowEx(taskbar, IntPtr.Zero, "ReBarWindow32", null);
        return reBar != IntPtr.Zero ? reBar : FindWindowEx(taskbar, IntPtr.Zero, "WorkerW", null);
    }

    private static IntPtr FindTaskList(IntPtr container)
    {
        var taskList = FindWindowEx(container, IntPtr.Zero, "MSTaskSwWClass", null);
        return taskList != IntPtr.Zero ? taskList : FindWindowEx(container, IntPtr.Zero, "MSTaskListWClass", null);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetParent(IntPtr child);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out TaskbarNativeMethods.NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);
}
