using System.Runtime.InteropServices;
using System.Windows;
using Forms = System.Windows.Forms;

namespace TaskbarLyrics.Light.App;

internal sealed record DisplayMonitorMetrics(Rect Bounds, Rect WorkArea, double PixelsPerDip);

internal static class DisplayMonitorMetricsService
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const int EffectiveDpi = 0;

    public static DisplayMonitorMetrics? Resolve(AppSettings settings)
    {
        var screens = Forms.Screen.AllScreens;
        var screen = settings.TargetScreenMode switch
        {
            TargetScreenMode.Cursor => Forms.Screen.FromPoint(Forms.Cursor.Position),
            TargetScreenMode.ScreenIndex when screens.Length > 0 =>
                screens[Math.Clamp(settings.TargetScreenIndex, 0, screens.Length - 1)],
            _ => Forms.Screen.PrimaryScreen ?? screens.FirstOrDefault()
        };

        if (screen is null)
        {
            return null;
        }

        var center = new NativePoint(
            screen.Bounds.Left + (screen.Bounds.Width / 2),
            screen.Bounds.Top + (screen.Bounds.Height / 2));
        var monitor = MonitorFromPoint(center, MonitorDefaultToNearest);
        var pixelsPerDip = GetPixelsPerDip(monitor);
        return new DisplayMonitorMetrics(
            ToLogicalRect(screen.Bounds, pixelsPerDip),
            ToLogicalRect(screen.WorkingArea, pixelsPerDip),
            pixelsPerDip);
    }

    private static Rect ToLogicalRect(System.Drawing.Rectangle rectangle, double pixelsPerDip)
    {
        return new Rect(
            rectangle.Left / pixelsPerDip,
            rectangle.Top / pixelsPerDip,
            rectangle.Width / pixelsPerDip,
            rectangle.Height / pixelsPerDip);
    }

    private static double GetPixelsPerDip(IntPtr monitor)
    {
        if (monitor != IntPtr.Zero &&
            GetDpiForMonitor(monitor, EffectiveDpi, out var dpiX, out _) == 0 &&
            dpiX > 0)
        {
            return dpiX / 96.0;
        }

        return 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
