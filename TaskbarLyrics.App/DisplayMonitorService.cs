using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace TaskbarLyrics.App;

internal static class DisplayMonitorService
{
    public static IReadOnlyList<DisplayMonitor> GetDisplays()
    {
        var screens = Forms.Screen.AllScreens
            .OrderByDescending(screen => screen.Primary)
            .ThenBy(screen => screen.Bounds.Top)
            .ThenBy(screen => screen.Bounds.Left)
            .ToList();

        return screens.Select((screen, index) => CreateDisplay(screen, index + 1)).ToList();
    }

    private static DisplayMonitor CreateDisplay(Forms.Screen screen, int displayNumber)
    {
        var bounds = NativeRect.FromRectangle(screen.Bounds);
        var workArea = NativeRect.FromRectangle(screen.WorkingArea);
        var center = new TaskbarNativeMethods.NativePoint(
            bounds.Left + (bounds.Width / 2),
            bounds.Top + (bounds.Height / 2));
        var monitor = TaskbarNativeMethods.MonitorFromPoint(
            center,
            TaskbarNativeMethods.MONITOR_DEFAULTTONEAREST);
        var pixelsPerDip = GetPixelsPerDip(monitor);
        var device = GetDisplayDevice(screen.DeviceName);
        var displayName = $"显示器 {GetWindowsDisplayNumber(screen.DeviceName, displayNumber)}";
        if (!string.IsNullOrWhiteSpace(device.FriendlyName))
        {
            displayName += $" · {device.FriendlyName}";
        }

        return new DisplayMonitor(
            device.Id,
            displayName,
            screen.Primary,
            bounds,
            workArea,
            pixelsPerDip);
    }

    private static DisplayDeviceIdentity GetDisplayDevice(string screenDeviceName)
    {
        var device = new DisplayMonitorNativeMethods.DisplayDevice
        {
            Size = Marshal.SizeOf<DisplayMonitorNativeMethods.DisplayDevice>()
        };
        if (!DisplayMonitorNativeMethods.EnumDisplayDevices(
                screenDeviceName,
                0,
                ref device,
                DisplayMonitorNativeMethods.EDD_GET_DEVICE_INTERFACE_NAME))
        {
            return new DisplayDeviceIdentity(screenDeviceName, string.Empty);
        }

        var id = string.IsNullOrWhiteSpace(device.DeviceId)
            ? screenDeviceName
            : device.DeviceId;
        return new DisplayDeviceIdentity(id, device.DeviceString.Trim());
    }

    private static string GetWindowsDisplayNumber(string screenDeviceName, int fallbackNumber)
    {
        const string prefix = @"\\.\DISPLAY";
        return screenDeviceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? screenDeviceName[prefix.Length..]
            : fallbackNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static double GetPixelsPerDip(IntPtr monitor)
    {
        if (monitor != IntPtr.Zero &&
            DisplayMonitorNativeMethods.GetDpiForMonitor(
                monitor,
                DisplayMonitorNativeMethods.MDT_EFFECTIVE_DPI,
                out var dpiX,
                out _) == 0 &&
            dpiX > 0)
        {
            return dpiX / 96.0;
        }

        return 1;
    }
}

internal readonly record struct DisplayDeviceIdentity(string Id, string FriendlyName);

internal static class LyricsDisplayTargetSelector
{
    public static IReadOnlyList<DisplayMonitor> Select(
        IReadOnlyList<DisplayMonitor> availableDisplays,
        LyricsDisplayMode mode,
        IReadOnlyCollection<string>? selectedDisplayIds)
    {
        if (availableDisplays.Count == 0)
        {
            return [];
        }

        if (mode == LyricsDisplayMode.All)
        {
            return availableDisplays.ToList();
        }

        var selectedIds = new HashSet<string>(
            selectedDisplayIds ?? [],
            StringComparer.OrdinalIgnoreCase);
        var selectedDisplays = availableDisplays
            .Where(display => selectedIds.Contains(display.Id))
            .ToList();
        if (selectedDisplays.Count > 0)
        {
            return selectedDisplays;
        }

        return
        [
            availableDisplays.FirstOrDefault(display => display.IsPrimary) ?? availableDisplays[0]
        ];
    }
}

internal sealed record DisplayMonitor(
    string Id,
    string Name,
    bool IsPrimary,
    NativeRect Bounds,
    NativeRect WorkArea,
    double PixelsPerDip)
{
    public int Width => Bounds.Width;

    public int Height => Bounds.Height;

    public int WorkAreaWidth => WorkArea.Width;

    public int WorkAreaHeight => WorkArea.Height;
}

internal readonly record struct NativeRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public static NativeRect FromRectangle(System.Drawing.Rectangle rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
}

internal static class DisplayMonitorNativeMethods
{
    internal const int MDT_EFFECTIVE_DPI = 0;
    internal const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevices(
        string? deviceName,
        uint deviceNumber,
        ref DisplayDevice displayDevice,
        uint flags);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(
        IntPtr hmonitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);
}
