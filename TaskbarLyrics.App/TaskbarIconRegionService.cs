using System.Runtime.InteropServices;
using System.Windows.Automation;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

/// <summary>
/// 探测任务栏图标区域边界：左侧图标组（开始/搜索/应用按钮）的右缘，
/// 以及右侧系统托盘的左缘，供歌词窗口自动避让与跟随。
/// 缓存值为物理像素，读取时按 pixelsPerDip 换算为逻辑像素。
/// </summary>
internal static class TaskbarIconRegionService
{
    private const string TaskbarWindowClass = "Shell_TrayWnd";
    private const string TrayNotifyWindowClass = "TrayNotifyWnd";
    private const string TaskbarFrameAutomationClass = "Taskbar.TaskbarFrameAutomationPeer";
    private const string TaskListButtonAutomationClass = "Taskbar.TaskListButtonAutomationPeer";
    private const string ToggleButtonAutomationClass = "ToggleButton";
    private const string SystemTrayAutomationClassPrefix = "SystemTray.";
    private const string LegacyStartButtonClass = "Button";
    private const string LegacyTaskBandRebarClass = "ReBarWindow32";
    private const long ScanThrottleMilliseconds = 500;

    private static readonly object CacheLock = new();
    private static long _lastScanTick;
    private static double _leftIconsRightEdge = double.NaN;
    private static double _trayLeftEdge = double.NaN;
    private static int _hasReportedScanFailure;

    public static bool TryGetLeftIconsRightEdge(double pixelsPerDip, out double logicalRight)
    {
        EnsureScanned();
        lock (CacheLock)
        {
            if (double.IsNaN(_leftIconsRightEdge))
            {
                logicalRight = 0;
                return false;
            }

            logicalRight = _leftIconsRightEdge / SanitizeScale(pixelsPerDip);
            return true;
        }
    }

    public static bool TryGetTrayLeftEdge(double pixelsPerDip, out double logicalLeft)
    {
        EnsureScanned();
        lock (CacheLock)
        {
            if (double.IsNaN(_trayLeftEdge))
            {
                logicalLeft = 0;
                return false;
            }

            logicalLeft = _trayLeftEdge / SanitizeScale(pixelsPerDip);
            return true;
        }
    }

    private static double SanitizeScale(double pixelsPerDip) =>
        double.IsFinite(pixelsPerDip) && pixelsPerDip > 0 ? pixelsPerDip : 1;

    private static void EnsureScanned()
    {
        lock (CacheLock)
        {
            if (Environment.TickCount64 - _lastScanTick < ScanThrottleMilliseconds)
            {
                return;
            }

            _lastScanTick = Environment.TickCount64;
        }

        double leftIconsRight;
        double trayLeft;
        try
        {
            leftIconsRight = ScanLeftIconsRightEdgePhysical();
            trayLeft = ScanTrayLeftEdgePhysical();
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or ElementNotAvailableException)
        {
            leftIconsRight = double.NaN;
            trayLeft = double.NaN;
            ReportScanFailure(ex);
        }

        lock (CacheLock)
        {
            _leftIconsRightEdge = leftIconsRight;
            _trayLeftEdge = trayLeft;
        }
    }

    private static void ReportScanFailure(Exception exception)
    {
        if (Interlocked.Exchange(ref _hasReportedScanFailure, 1) != 0)
        {
            return;
        }

        Log.Diagnostic("TASKBAR-REGION", $"Taskbar region scan failed: {exception.GetType().Name}: {exception.Message}");
    }

    private static double ScanLeftIconsRightEdgePhysical()
    {
        var tray = AutomationElement.RootElement.FindFirst(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.ClassNameProperty, TaskbarWindowClass));
        if (tray is not null)
        {
            // Windows 11：开始/搜索/应用按钮位于 TaskbarFrameAutomationPeer 之下。
            var frame = tray.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ClassNameProperty, TaskbarFrameAutomationClass));
            if (frame is not null)
            {
                var buttons = frame.FindAll(
                    TreeScope.Descendants,
                    new OrCondition(
                        new PropertyCondition(AutomationElement.ClassNameProperty, TaskListButtonAutomationClass),
                        new PropertyCondition(AutomationElement.ClassNameProperty, ToggleButtonAutomationClass)));

                var maxRight = 0d;
                foreach (AutomationElement button in buttons)
                {
                    var rect = button.Current.BoundingRectangle;
                    if (!rect.IsEmpty && rect.Width > 0)
                    {
                        maxRight = Math.Max(maxRight, rect.Right);
                    }
                }

                if (maxRight > 0)
                {
                    return maxRight;
                }
            }
        }

        // Windows 10 回退：开始按钮与任务带仍为传统 Win32 子窗口。
        var taskbarHwnd = TaskbarRegionNativeMethods.FindWindow(TaskbarWindowClass, null);
        if (taskbarHwnd == IntPtr.Zero)
        {
            return double.NaN;
        }

        var legacyMaxRight = 0d;
        for (var child = TaskbarRegionNativeMethods.FindWindowEx(taskbarHwnd, IntPtr.Zero, null, null);
             child != IntPtr.Zero;
             child = TaskbarRegionNativeMethods.FindWindowEx(taskbarHwnd, child, null, null))
        {
            var className = GetClassName(child);
            if (!string.Equals(className, LegacyStartButtonClass, StringComparison.Ordinal) &&
                !string.Equals(className, LegacyTaskBandRebarClass, StringComparison.Ordinal))
            {
                continue;
            }

            if (TaskbarRegionNativeMethods.GetWindowRect(child, out var rect))
            {
                legacyMaxRight = Math.Max(legacyMaxRight, rect.Right);
            }
        }

        return legacyMaxRight > 0 ? legacyMaxRight : double.NaN;
    }

    private static double ScanTrayLeftEdgePhysical()
    {
        var taskbarHwnd = TaskbarRegionNativeMethods.FindWindow(TaskbarWindowClass, null);
        if (taskbarHwnd != IntPtr.Zero)
        {
            var trayNotify = TaskbarRegionNativeMethods.FindWindowEx(taskbarHwnd, IntPtr.Zero, TrayNotifyWindowClass, null);
            if (trayNotify != IntPtr.Zero && TaskbarRegionNativeMethods.GetWindowRect(trayNotify, out var rect))
            {
                return rect.Left;
            }
        }

        var tray = AutomationElement.RootElement.FindFirst(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.ClassNameProperty, TaskbarWindowClass));
        if (tray is null)
        {
            return double.NaN;
        }

        var minLeft = double.NaN;
        foreach (AutomationElement child in tray.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            var className = child.Current.ClassName;
            if (string.IsNullOrEmpty(className) ||
                !className.StartsWith(SystemTrayAutomationClassPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var rect = child.Current.BoundingRectangle;
            if (!rect.IsEmpty && rect.Width > 0)
            {
                minLeft = double.IsNaN(minLeft) ? rect.X : Math.Min(minLeft, rect.X);
            }
        }

        return minLeft;
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var buffer = new char[256];
        var length = TaskbarRegionNativeMethods.GetClassName(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }
}

internal static class TaskbarRegionNativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hwnd, char[] className, int maxCount);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }
}
