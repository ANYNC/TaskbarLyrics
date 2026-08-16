using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

internal sealed class TaskbarPlacementService
{
    private const int WmShowWindow = 0x0018;
    private static int _hasReportedNativeBoundsCommitFailure;

    public uint TaskbarCreatedMessage { get; } = TaskbarNativeMethods.RegisterWindowMessage("TaskbarCreated");

    public bool RequiresReattach(int message) =>
        (uint)message == TaskbarCreatedMessage || message == WmShowWindow;

    public static bool IsShowWindowMessage(int message) => message == WmShowWindow;

    public static void Anchor(Window window, AppSettings settings, DisplayMonitor? targetDisplay = null)
    {
        var pixelsPerDip = targetDisplay?.PixelsPerDip ?? GetPixelsPerDip(window);
        var display = targetDisplay is null
            ? GetPrimaryDisplayMetrics(pixelsPerDip)
            : ConvertPhysicalDisplayMetrics(targetDisplay.Bounds, targetDisplay.WorkArea, pixelsPerDip);
        var taskbar = ResolveTaskbarBounds(display);
        var metrics = LyricsLayoutMetrics.Create(settings, pixelsPerDip);
        window.Height = Math.Min(metrics.DesiredWindowHeight, display.Height);

        window.Left = settings.HorizontalAnchor switch
        {
            LyricsHorizontalAnchor.Left => Math.Max(display.Left, display.Left + settings.XOffset),
            LyricsHorizontalAnchor.Center => display.Left + ((display.Width - window.Width) / 2.0) + settings.XOffset,
            _ => Math.Max(display.Left, display.Right - window.Width - 230 + settings.XOffset)
        };

        window.Top = display.Top + CalculateVerticalPosition(
            taskbar.CenterY - display.Top,
            window.Height,
            display.Height,
            settings.YOffset);

        CommitNativeBounds(window, pixelsPerDip);
    }

    internal static double GetPixelsPerDip(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            var dpi = TaskbarNativeMethods.GetDpiForWindow(hwnd);
            if (dpi > 0)
            {
                return dpi / 96.0;
            }
        }

        var visualScale = VisualTreeHelper.GetDpi(window).DpiScaleY;
        return double.IsFinite(visualScale) && visualScale > 0 ? visualScale : 1;
    }

    private static TaskbarDisplayMetrics GetPrimaryDisplayMetrics(double pixelsPerDip)
    {
        var monitor = TaskbarNativeMethods.MonitorFromPoint(
            new TaskbarNativeMethods.NativePoint(0, 0),
            TaskbarNativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var monitorInfo = new TaskbarNativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<TaskbarNativeMethods.MonitorInfo>()
        };

        if (monitor != IntPtr.Zero && TaskbarNativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return ConvertPhysicalDisplayMetrics(monitorInfo.Monitor, monitorInfo.WorkArea, pixelsPerDip);
        }

        var workArea = SystemParameters.WorkArea;
        return new TaskbarDisplayMetrics(
            0,
            0,
            SystemParameters.PrimaryScreenWidth,
            SystemParameters.PrimaryScreenHeight,
            workArea.Left,
            workArea.Top,
            workArea.Width,
            workArea.Height);
    }

    private static void CommitNativeBounds(Window window, double pixelsPerDip)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var bounds = ConvertLogicalWindowBounds(
            window.Left,
            window.Top,
            window.Width,
            window.Height,
            pixelsPerDip);
        var setWindowPosSucceeded = TaskbarNativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            TaskbarNativeMethods.SWP_NOZORDER |
            TaskbarNativeMethods.SWP_NOACTIVATE);
        if (!setWindowPosSucceeded)
        {
            ReportNativeBoundsCommitFailure(bounds, pixelsPerDip, Marshal.GetLastWin32Error());
        }
    }

    private static void ReportNativeBoundsCommitFailure(
        TaskbarNativeBounds bounds,
        double pixelsPerDip,
        int errorCode)
    {
        if (Interlocked.Exchange(ref _hasReportedNativeBoundsCommitFailure, 1) != 0)
        {
            return;
        }

        Log.Diagnostic(
            "DPI-WINDOW",
            $"SetWindowPosFailed Error={errorCode} Bounds={bounds.Left},{bounds.Top},{bounds.Width},{bounds.Height} " +
            $"PixelsPerDip={pixelsPerDip:0.####}");
    }

    internal static TaskbarNativeBounds ConvertLogicalWindowBounds(
        double left,
        double top,
        double width,
        double height,
        double pixelsPerDip)
    {
        pixelsPerDip = double.IsFinite(pixelsPerDip) && pixelsPerDip > 0 ? pixelsPerDip : 1;
        return new TaskbarNativeBounds(
            AlignPhysicalPixel(left, pixelsPerDip),
            AlignPhysicalPixel(top, pixelsPerDip),
            Math.Max(1, AlignPhysicalPixel(width, pixelsPerDip)),
            Math.Max(1, AlignPhysicalPixel(height, pixelsPerDip)));
    }

    private static int AlignPhysicalPixel(double value, double pixelsPerDip) =>
        checked((int)Math.Round(value * pixelsPerDip, MidpointRounding.AwayFromZero));

    internal static TaskbarDisplayMetrics ConvertPhysicalDisplayMetrics(
        TaskbarNativeMethods.NativeRect monitor,
        TaskbarNativeMethods.NativeRect workArea,
        double pixelsPerDip)
    {
        pixelsPerDip = double.IsFinite(pixelsPerDip) && pixelsPerDip > 0 ? pixelsPerDip : 1;
        return new TaskbarDisplayMetrics(
            monitor.Left / pixelsPerDip,
            monitor.Top / pixelsPerDip,
            (monitor.Right - monitor.Left) / pixelsPerDip,
            (monitor.Bottom - monitor.Top) / pixelsPerDip,
            workArea.Left / pixelsPerDip,
            workArea.Top / pixelsPerDip,
            (workArea.Right - workArea.Left) / pixelsPerDip,
            (workArea.Bottom - workArea.Top) / pixelsPerDip);
    }

    internal static TaskbarDisplayMetrics ConvertPhysicalDisplayMetrics(
        NativeRect monitor,
        NativeRect workArea,
        double pixelsPerDip) =>
        ConvertPhysicalDisplayMetrics(
            new TaskbarNativeMethods.NativeRect(monitor.Left, monitor.Top, monitor.Right, monitor.Bottom),
            new TaskbarNativeMethods.NativeRect(workArea.Left, workArea.Top, workArea.Right, workArea.Bottom),
            pixelsPerDip);

    internal static TaskbarBounds ResolveTaskbarBounds(TaskbarDisplayMetrics display)
    {
        const double defaultThickness = 48;
        const double edgeTolerance = 0.5;
        if (display.WorkAreaBottom < display.Bottom - edgeTolerance)
        {
            return new TaskbarBounds(
                display.Left,
                display.WorkAreaBottom,
                display.Width,
                Math.Max(defaultThickness, display.Bottom - display.WorkAreaBottom));
        }

        if (display.WorkAreaTop > display.Top + edgeTolerance)
        {
            return new TaskbarBounds(
                display.Left,
                display.Top,
                display.Width,
                Math.Max(defaultThickness, display.WorkAreaTop - display.Top));
        }

        if (display.WorkAreaLeft > display.Left + edgeTolerance)
        {
            return new TaskbarBounds(
                display.Left,
                display.Top,
                Math.Max(defaultThickness, display.WorkAreaLeft - display.Left),
                display.Height);
        }

        if (display.WorkAreaRight < display.Right - edgeTolerance)
        {
            return new TaskbarBounds(
                display.WorkAreaRight,
                display.Top,
                Math.Max(defaultThickness, display.Right - display.WorkAreaRight),
                display.Height);
        }

        return new TaskbarBounds(
            display.Left,
            display.Bottom - defaultThickness,
            display.Width,
            defaultThickness);
    }

    public static void Attach(Window window, bool forceAlwaysOnTop)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        window.Topmost = forceAlwaysOnTop;
        var hWndInsertAfter = forceAlwaysOnTop
            ? TaskbarNativeMethods.HWND_TOPMOST
            : TaskbarNativeMethods.HWND_NOTOPMOST;

        TaskbarNativeMethods.SetWindowPos(
            hwnd,
            hWndInsertAfter,
            0,
            0,
            0,
            0,
            TaskbarNativeMethods.SWP_NOMOVE |
            TaskbarNativeMethods.SWP_NOSIZE |
            TaskbarNativeMethods.SWP_NOACTIVATE |
            TaskbarNativeMethods.SWP_ASYNCWINDOWPOS |
            TaskbarNativeMethods.SWP_SHOWWINDOW);
        TaskbarNativeMethods.ShowWindow(hwnd, TaskbarNativeMethods.SW_SHOWNOACTIVATE);
    }

    public static void ApplyToolWindowStyle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var extendedStyle = TaskbarNativeMethods.GetWindowLongPtr(hwnd, TaskbarNativeMethods.GWL_EXSTYLE);
        var nextStyle = new IntPtr(extendedStyle.ToInt64() | TaskbarNativeMethods.WS_EX_TOOLWINDOW);
        if (nextStyle != extendedStyle)
        {
            TaskbarNativeMethods.SetWindowLongPtr(hwnd, TaskbarNativeMethods.GWL_EXSTYLE, nextStyle);
        }
    }

    internal static double CalculateVerticalPosition(
        double anchorCenterY,
        double windowHeight,
        double screenHeight,
        double yOffset)
    {
        var centeredTop = anchorCenterY - (windowHeight / 2);
        return Math.Clamp(
            centeredTop + yOffset,
            0,
            Math.Max(0, screenHeight - windowHeight));
    }
}

internal readonly record struct TaskbarDisplayMetrics(
    double Left,
    double Top,
    double Width,
    double Height,
    double WorkAreaLeft,
    double WorkAreaTop,
    double WorkAreaWidth,
    double WorkAreaHeight)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public double WorkAreaRight => WorkAreaLeft + WorkAreaWidth;

    public double WorkAreaBottom => WorkAreaTop + WorkAreaHeight;
}

internal readonly record struct TaskbarBounds(double Left, double Top, double Width, double Height)
{
    public double CenterY => Top + (Height / 2);
}

internal readonly record struct TaskbarNativeBounds(int Left, int Top, int Width, int Height);

internal static class TaskbarNativeMethods
{
    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal static readonly IntPtr HWND_NOTOPMOST = new(-2);
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_ASYNCWINDOWPOS = 0x4000;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const int SW_SHOWNOACTIVATE = 4;
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        internal NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal NativeRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        internal int Size;
        internal NativeRect Monitor;
        internal NativeRect WorkArea;
        internal uint Flags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);
}
