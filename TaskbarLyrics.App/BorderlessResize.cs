using System.Runtime.InteropServices;
using System.Windows;

namespace TaskbarLyrics.App;

internal static class BorderlessResize
{
    private static bool _resizing;
    private static int _hit;
    private static int _startX, _startY;
    private static double _l, _t, _w, _h;

    private const int WmNcLButtonDown = 0x00A1;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonUp = 0x0202;
    private const int HitLeft = 10, HitRight = 11, HitTop = 12, HitTopLeft = 13, HitTopRight = 14, HitBottom = 15, HitBottomLeft = 16, HitBottomRight = 17;

    public static bool Process(Window w, IntPtr hwnd, int msg, IntPtr wParam, ref bool handled)
    {
        if (msg == WmNcLButtonDown && IsResize((int)wParam.ToInt64()) && !_resizing)
        {
            _resizing = true;
            _hit = (int)wParam.ToInt64();
            GetCursorPos(out var p);
            _startX = p.X;
            _startY = p.Y;
            _l = w.Left;
            _t = w.Top;
            _w = w.Width;
            _h = w.Height;
            SetCapture(hwnd);
            handled = true;
            return true;
        }

        if (msg == WmMouseMove && _resizing)
        {
            GetCursorPos(out var p);
            var dx = p.X - _startX;
            var dy = p.Y - _startY;
            double l = _l, t = _t, ww = _w, hh = _h;
            var left = _hit == HitLeft || _hit == HitTopLeft || _hit == HitBottomLeft;
            var right = _hit == HitRight || _hit == HitTopRight || _hit == HitBottomRight;
            var top = _hit == HitTop || _hit == HitTopLeft || _hit == HitTopRight;
            var bottom = _hit == HitBottom || _hit == HitBottomLeft || _hit == HitBottomRight;
            if (left) { l = _l + dx; ww = _w - dx; }
            if (right) { ww = _w + dx; }
            if (top) { t = _t + dy; hh = _h - dy; }
            if (bottom) { hh = _h + dy; }
            if (ww < w.MinWidth) { if (left) l = _l + (_w - w.MinWidth); ww = w.MinWidth; }
            if (hh < w.MinHeight) { if (top) t = _t + (_h - w.MinHeight); hh = w.MinHeight; }
            w.Left = l; w.Top = t; w.Width = ww; w.Height = hh;
            handled = true;
            return true;
        }

        if (msg == WmLButtonUp && _resizing)
        {
            _resizing = false;
            ReleaseCapture();
            handled = true;
            return true;
        }

        return false;
    }

    private static bool IsResize(int hit) =>
        hit == HitLeft || hit == HitRight || hit == HitTop || hit == HitBottom ||
        hit == HitTopLeft || hit == HitTopRight || hit == HitBottomLeft || hit == HitBottomRight;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
}
