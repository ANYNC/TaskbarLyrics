using System.Runtime.InteropServices;

namespace TaskbarLyrics.App;

internal static class EdgeHitThrough
{
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const int Border = 8;

    private static readonly SubclassProc Proc = HitTestSubclass;
    private static IntPtr _topLevel;

    public static void Attach(IntPtr topLevelHwnd)
    {
        _topLevel = topLevelHwnd;
        EnumChildWindows(topLevelHwnd, EnumProc, IntPtr.Zero);
    }

    public static void Detach(IntPtr topLevelHwnd)
    {
        _topLevel = topLevelHwnd;
        EnumChildWindows(topLevelHwnd, DetachProc, IntPtr.Zero);
    }

    private static bool EnumProc(IntPtr child, IntPtr _)
    {
        SetWindowSubclass(child, Proc, IntPtr.Zero, _topLevel);
        EnumChildWindows(child, EnumProc, IntPtr.Zero);
        return true;
    }

    private static bool DetachProc(IntPtr child, IntPtr _)
    {
        RemoveWindowSubclass(child, Proc, IntPtr.Zero);
        EnumChildWindows(child, DetachProc, IntPtr.Zero);
        return true;
    }

    private static IntPtr HitTestSubclass(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (msg == WM_NCHITTEST && dwRefData != IntPtr.Zero && GetWindowRect(dwRefData, out var rect))
        {
            var lo = lParam.ToInt32() & 0xFFFF;
            var hi = (lParam.ToInt32() >> 16) & 0xFFFF;
            var x = (short)lo;
            var y = (short)hi;
            if (x < rect.Left + Border || x > rect.Right - Border || y < rect.Top + Border || y > rect.Bottom - Border)
            {
                return new IntPtr(HTTRANSPARENT);
            }
        }

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);
    private delegate IntPtr SubclassProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
