using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TaskbarLyrics.App;

internal sealed class TaskbarPlacementService
{
    private const int WmShowWindow = 0x0018;

    public uint TaskbarCreatedMessage { get; } = TaskbarNativeMethods.RegisterWindowMessage("TaskbarCreated");

    public bool RequiresReattach(int message) =>
        (uint)message == TaskbarCreatedMessage || message == WmShowWindow;

    public static bool IsShowWindowMessage(int message) => message == WmShowWindow;

    public static void Anchor(Window window, AppSettings settings)
    {
        var workArea = SystemParameters.WorkArea;
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        const double normalTaskbarHeight = 48;
        var taskbarHeight = Math.Max(normalTaskbarHeight, screenHeight - workArea.Height);
        var metrics = LyricsLayoutMetrics.Create(settings, VisualTreeHelper.GetDpi(window).DpiScaleY);
        window.Height = Math.Min(metrics.DesiredWindowHeight, screenHeight);

        window.Left = settings.HorizontalAnchor switch
        {
            LyricsHorizontalAnchor.Left => Math.Max(0, settings.XOffset),
            LyricsHorizontalAnchor.Center => ((screenWidth - window.Width) / 2.0) + settings.XOffset,
            _ => Math.Max(0, screenWidth - window.Width - 230 + settings.XOffset)
        };

        var taskbarCenterY = screenHeight - (taskbarHeight / 2);
        window.Top = CalculateVerticalPosition(
            taskbarCenterY,
            window.Height,
            screenHeight,
            settings.YOffset);
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

internal static class TaskbarNativeMethods
{
    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal static readonly IntPtr HWND_NOTOPMOST = new(-2);
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_ASYNCWINDOWPOS = 0x4000;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const int SW_SHOWNOACTIVATE = 4;
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;

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
}
