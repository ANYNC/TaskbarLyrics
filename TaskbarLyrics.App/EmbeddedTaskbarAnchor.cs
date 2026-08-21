using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TaskbarLyrics.App;

internal readonly record struct EmbeddedTaskbarNativeBounds(int Left, int Top, int Width, int Height);

internal static class EmbeddedTaskbarLayoutCalculator
{
    public static long CalculateIntersectionArea(NativeRect first, NativeRect second)
    {
        var width = Math.Max(0, Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left));
        var height = Math.Max(0, Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top));
        return (long)width * height;
    }

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

    public static double CalculateVerticalTop(double taskbarHeight, double windowHeight, double offset)
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
    private EmbeddedTaskbarNativeBounds _originalTaskListBounds;
    private long _originalStyle;
    private long _originalExtendedStyle;
    private bool _hasOriginalStyles;
    private bool _taskListSqueezed;
    private bool _disposed;

    public bool Attach(Window window, AppSettings settings, DisplayMonitor? targetDisplay)
    {
        if (_disposed)
        {
            return false;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var taskbar = FindTaskbar(targetDisplay);
        if (taskbar == IntPtr.Zero)
        {
            return false;
        }

        if (_windowHandle != hwnd || _taskbarHandle != taskbar)
        {
            Detach();
            _windowHandle = hwnd;
            _taskbarHandle = taskbar;
            CaptureOriginalStyles(hwnd);
        }

        var isWindows11 = Environment.OSVersion.Version.Major == 10 &&
            Environment.OSVersion.Version.Build >= 22000;
        var parent = isWindows11 ? taskbar : FindTaskbarContainer(taskbar);
        if (parent == IntPtr.Zero)
        {
            return false;
        }

        if (_parentHandle != parent || GetParent(hwnd) != parent)
        {
            _ = SetParent(hwnd, parent);
            if (GetParent(hwnd) != parent)
            {
                return false;
            }

            ApplyChildWindowStyle(hwnd);
            _parentHandle = parent;
        }

        if (!isWindows11)
        {
            SqueezeTaskList(parent, settings);
        }

        return Position(hwnd, parent, window, settings, targetDisplay);
    }

    public void Detach()
    {
        RestoreTaskList();

        if (_windowHandle != IntPtr.Zero && IsWindow(_windowHandle))
        {
            _ = SetParent(_windowHandle, IntPtr.Zero);
            RestoreTopLevelStyles(_windowHandle);
        }

        _windowHandle = IntPtr.Zero;
        _taskbarHandle = IntPtr.Zero;
        _parentHandle = IntPtr.Zero;
        _taskListHandle = IntPtr.Zero;
        _hasOriginalStyles = false;
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

    private static bool Position(
        IntPtr hwnd,
        IntPtr parent,
        Window window,
        AppSettings settings,
        DisplayMonitor? targetDisplay)
    {
        if (!GetWindowRect(parent, out var parentRect))
        {
            return false;
        }

        var pixelsPerDip = targetDisplay?.PixelsPerDip ?? TaskbarPlacementService.GetPixelsPerDip(window);
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
            AppSettings.ClampEmbeddedTaskbarOffset(settings.EmbeddedTaskbarHorizontalOffset));
        var clientTop = EmbeddedTaskbarLayoutCalculator.CalculateVerticalTop(
            taskbarHeight,
            height,
            AppSettings.ClampEmbeddedTaskbarOffset(settings.EmbeddedTaskbarVerticalOffset));
        var bounds = EmbeddedTaskbarLayoutCalculator.ToTaskbarClientBounds(
            clientLeft,
            clientTop,
            width,
            height,
            pixelsPerDip);

        return TaskbarNativeMethods.SetWindowPos(
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

        if (_taskListHandle == IntPtr.Zero ||
            !GetWindowRect(_taskListHandle, out var taskListRect) ||
            !GetWindowRect(parent, out var parentRect))
        {
            return;
        }

        if (!_taskListSqueezed)
        {
            _originalTaskListBounds = new EmbeddedTaskbarNativeBounds(
                taskListRect.Left - parentRect.Left,
                taskListRect.Top - parentRect.Top,
                taskListRect.Right - taskListRect.Left,
                taskListRect.Bottom - taskListRect.Top);
        }

        var pixelsPerDip = GetPixelsPerDipForHandle(parent);
        var reservedWidth = (int)Math.Round(
            (AppSettings.ClampEmbeddedTaskbarWidth(settings.EmbeddedTaskbarWidth) +
                AppSettings.ClampEmbeddedTaskbarOffset(settings.EmbeddedTaskbarHorizontalOffset)) * pixelsPerDip,
            MidpointRounding.AwayFromZero);
        var targetWidth = Math.Clamp(
            _originalTaskListBounds.Width - reservedWidth,
            0,
            _originalTaskListBounds.Width);

        _ = MoveWindow(
            _taskListHandle,
            _originalTaskListBounds.Left,
            _originalTaskListBounds.Top,
            targetWidth,
            _originalTaskListBounds.Height,
            true);
        _taskListSqueezed = true;
    }

    private void RestoreTaskList()
    {
        if (!_taskListSqueezed || _taskListHandle == IntPtr.Zero || !IsWindow(_taskListHandle))
        {
            return;
        }

        _ = MoveWindow(
            _taskListHandle,
            _originalTaskListBounds.Left,
            _originalTaskListBounds.Top,
            _originalTaskListBounds.Width,
            _originalTaskListBounds.Height,
            true);
        _taskListSqueezed = false;
    }

    private void CaptureOriginalStyles(IntPtr hwnd)
    {
        _originalStyle = TaskbarNativeMethods.GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        _originalExtendedStyle = TaskbarNativeMethods.GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        _hasOriginalStyles = true;
    }

    private static void ApplyChildWindowStyle(IntPtr hwnd)
    {
        var style = TaskbarNativeMethods.GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        _ = TaskbarNativeMethods.SetWindowLongPtr(
            hwnd,
            GwlStyle,
            new IntPtr((style & ~WsPopup) | WsChild | WsVisible));

        var extendedStyle = TaskbarNativeMethods.GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        _ = TaskbarNativeMethods.SetWindowLongPtr(
            hwnd,
            GwlExStyle,
            new IntPtr((extendedStyle & ~WsExToolWindow) | WsExNoActivate));
    }

    private void RestoreTopLevelStyles(IntPtr hwnd)
    {
        if (_hasOriginalStyles)
        {
            _ = TaskbarNativeMethods.SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(_originalStyle));
            _ = TaskbarNativeMethods.SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(_originalExtendedStyle));
            return;
        }

        var style = TaskbarNativeMethods.GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        _ = TaskbarNativeMethods.SetWindowLongPtr(hwnd, GwlStyle, new IntPtr((style & ~WsChild) | WsPopup));
        var extendedStyle = TaskbarNativeMethods.GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        _ = TaskbarNativeMethods.SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(extendedStyle & ~WsExNoActivate));
    }

    private static IntPtr FindTaskbar(DisplayMonitor? targetDisplay)
    {
        var primaryTaskbar = FindWindow("Shell_TrayWnd", null);
        if (targetDisplay is null)
        {
            return primaryTaskbar;
        }

        var taskbars = new List<IntPtr>();
        if (primaryTaskbar != IntPtr.Zero)
        {
            taskbars.Add(primaryTaskbar);
        }

        var secondaryTaskbar = IntPtr.Zero;
        while (true)
        {
            secondaryTaskbar = FindWindowEx(
                IntPtr.Zero,
                secondaryTaskbar,
                "Shell_SecondaryTrayWnd",
                null);
            if (secondaryTaskbar == IntPtr.Zero)
            {
                break;
            }

            taskbars.Add(secondaryTaskbar);
        }

        var match = taskbars
            .Select(handle => new { Handle = handle, Score = CalculateTaskbarScore(handle, targetDisplay.Bounds) })
            .OrderByDescending(candidate => candidate.Score.IntersectionArea)
            .ThenBy(candidate => candidate.Score.CenterDistanceSquared)
            .FirstOrDefault();
        return match is not null && match.Score.IntersectionArea > 0
            ? match.Handle
            : IntPtr.Zero;
    }

    private static (long IntersectionArea, long CenterDistanceSquared) CalculateTaskbarScore(
        IntPtr taskbar,
        NativeRect displayBounds)
    {
        if (!GetWindowRect(taskbar, out var taskbarBounds))
        {
            return (0, long.MaxValue);
        }

        var intersectionArea = EmbeddedTaskbarLayoutCalculator.CalculateIntersectionArea(
            new NativeRect(taskbarBounds.Left, taskbarBounds.Top, taskbarBounds.Right, taskbarBounds.Bottom),
            displayBounds);
        var taskbarCenterX = ((long)taskbarBounds.Left + taskbarBounds.Right) / 2;
        var taskbarCenterY = ((long)taskbarBounds.Top + taskbarBounds.Bottom) / 2;
        var displayCenterX = ((long)displayBounds.Left + displayBounds.Right) / 2;
        var displayCenterY = ((long)displayBounds.Top + displayBounds.Bottom) / 2;
        var deltaX = taskbarCenterX - displayCenterX;
        var deltaY = taskbarCenterY - displayCenterY;
        return (intersectionArea, (deltaX * deltaX) + (deltaY * deltaY));
    }

    private static IntPtr FindTaskbarContainer(IntPtr taskbar)
    {
        var reBar = FindWindowEx(taskbar, IntPtr.Zero, "ReBarWindow32", null);
        return reBar != IntPtr.Zero ? reBar : FindWindowEx(taskbar, IntPtr.Zero, "WorkerW", null);
    }

    private static IntPtr FindTaskList(IntPtr container)
    {
        var taskList = FindWindowEx(container, IntPtr.Zero, "MSTaskSwWClass", null);
        return taskList != IntPtr.Zero
            ? taskList
            : FindWindowEx(container, IntPtr.Zero, "MSTaskListWClass", null);
    }

    private static double GetPixelsPerDipForHandle(IntPtr hwnd)
    {
        var dpi = TaskbarNativeMethods.GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96.0 : 1;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(
        IntPtr parent,
        IntPtr childAfter,
        string? className,
        string? windowName);

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
