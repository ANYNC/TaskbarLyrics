using System.Windows;
using System.Windows.Interop;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace TaskbarLyrics.App;

internal sealed class FloatingWindowPlacement(Window window, FloatingWindowPositionStore store)
{
    private DisplayMonitor? _home;
    private DisplayMonitor? _display;
    private IReadOnlyList<DisplayMonitor> _displays = [];
    private bool _enabled;
    internal bool IsDragging { get; private set; }
    internal DisplayMonitor? Display => IsDragging
        ? Nearest(FloatingLyricsPanelController.GetBounds(window), _displays) ?? _display : _display;

    internal void Configure(AppSettings settings)
    {
        _enabled = settings.UseFloatingWindow;
    }

    internal void ResetPositions()
    {
        store.Clear();
        _display = _home;
    }

    internal bool Refresh(DisplayMonitor home)
    {
        var previous = _display;
        _home = home;
        _displays = DisplayMonitorService.GetDisplays();
        var position = store.Get(home.Id);
        _display = RestoreDisplay(position, home, _displays);
        return previous != _display;
    }

    internal bool TryRestore()
    {
        if (!_enabled || _home is null || _display is null) return false;
        if (IsDragging) return true;
        var position = store.Get(_home.Id);
        if (position is null) return false;
        var scale = _display.PixelsPerDip;
        var bounds = RestoreBounds(position, _display, new Size(window.Width * scale, window.Height * scale));
        Move(bounds, scale);
        return true;
    }

    internal void RememberCurrentPosition()
    {
        if (!_enabled || _home is null || !window.IsVisible) return;
        _displays = DisplayMonitorService.GetDisplays();
        var bounds = FloatingLyricsPanelController.GetBounds(window);
        _display = Nearest(bounds, _displays) ?? _home;
        var clamped = Clamp(bounds, _display.Bounds);
        Move(clamped, _display.PixelsPerDip);
        store.Remember(_home.Id, Capture(clamped, _display));
    }

    internal void Drag()
    {
        if (!_enabled || _home is null || IsDragging || !window.IsVisible) return;
        var home = _home;
        var original = FloatingLyricsPanelController.GetBounds(window);
        _displays = DisplayMonitorService.GetDisplays();
        IsDragging = true;
        try { NativeWindowInteraction.BeginDrag(window); }
        finally { IsDragging = false; }
        var bounds = FloatingLyricsPanelController.GetBounds(window);
        if (!_enabled || _home.Id != home.Id || bounds.IsEmpty || bounds.TopLeft == original.TopLeft || !window.IsVisible) return;
        _display = Nearest(bounds, _displays) ?? home;
        var clamped = Clamp(bounds, _display.Bounds);
        Move(clamped, _display.PixelsPerDip);
        store.Remember(home.Id, Capture(clamped, _display));
    }

    private void Move(Rect bounds, double scale)
    {
        window.Left = bounds.Left / scale;
        window.Top = bounds.Top / scale;
        TaskbarNativeMethods.SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero,
            (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top), 0, 0,
            TaskbarNativeMethods.SWP_NOSIZE | TaskbarNativeMethods.SWP_NOZORDER | TaskbarNativeMethods.SWP_NOACTIVATE);
    }

    internal static void ResizeFromBottom(Window window, double width, double height, Rect anchor, DisplayMonitor display)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var scale = TaskbarNativeMethods.GetDpiForWindow(handle) / 96.0;
        var physicalWidth = Math.Round(width * scale);
        var physicalHeight = Math.Round(height * scale);
        var bounds = Clamp(new Rect(anchor.Left, anchor.Bottom - physicalHeight, physicalWidth, physicalHeight), display.Bounds);
        // Apply the size and bottom anchoring before WPF can expose an intermediate HWND.
        TaskbarNativeMethods.SetWindowPos(handle, IntPtr.Zero,
            (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top), (int)physicalWidth, (int)physicalHeight,
            TaskbarNativeMethods.SWP_NOZORDER | TaskbarNativeMethods.SWP_NOACTIVATE);
    }

    internal static Rect Clamp(Rect bounds, NativeRect area) => new(
        Math.Clamp(bounds.Left, area.Left, Math.Max(area.Left, area.Right - bounds.Width)),
        Math.Clamp(bounds.Top, area.Top, Math.Max(area.Top, area.Bottom - bounds.Height)), bounds.Width, bounds.Height);

    internal static Rect RestoreBounds(FloatingWindowPosition position, DisplayMonitor display, Size size)
    {
        var area = position.UseScreenBounds ? display.Bounds : display.WorkArea;
        return new Rect(area.Left + Math.Max(0, area.Width - size.Width) * position.X,
            area.Top + Math.Max(0, area.Height - size.Height) * position.Y, size.Width, size.Height);
    }

    internal static FloatingWindowPosition Capture(Rect bounds, DisplayMonitor display) => new(display.Id,
        Math.Clamp((bounds.Left - display.Bounds.Left) / Math.Max(1, display.Width - bounds.Width), 0, 1),
        Math.Clamp((bounds.Top - display.Bounds.Top) / Math.Max(1, display.Height - bounds.Height), 0, 1), UseScreenBounds: true);

    internal static DisplayMonitor? Nearest(Rect bounds, IReadOnlyList<DisplayMonitor> displays) => displays
        .OrderByDescending(item => Overlap(bounds, item.Bounds)).ThenBy(item => Distance(bounds, item.Bounds)).FirstOrDefault();

    internal static DisplayMonitor RestoreDisplay(FloatingWindowPosition? position, DisplayMonitor home,
        IReadOnlyList<DisplayMonitor> displays) => displays.FirstOrDefault(item =>
            item.Id.Equals(position?.DisplayId, StringComparison.OrdinalIgnoreCase)) ?? home;

    private static double Overlap(Rect windowBounds, NativeRect display)
    {
        var intersection = Rect.Intersect(windowBounds, new Rect(display.Left, display.Top, display.Width, display.Height));
        return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
    }

    private static double Distance(Rect windowBounds, NativeRect display)
    {
        var center = new Point(windowBounds.Left + windowBounds.Width / 2, windowBounds.Top + windowBounds.Height / 2);
        var x = Math.Max(display.Left - center.X, Math.Max(0, center.X - display.Right));
        var y = Math.Max(display.Top - center.Y, Math.Max(0, center.Y - display.Bottom));
        return x * x + y * y;
    }
}
