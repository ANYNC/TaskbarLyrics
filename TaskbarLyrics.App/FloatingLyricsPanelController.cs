using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

internal sealed class FloatingLyricsPanelController : IDisposable
{
    private sealed class Target(Window window, Func<DisplayMonitor?> display, Func<bool> contentVisible, Action<bool, bool> surface)
    {
        internal Window Window { get; } = window;
        internal Func<DisplayMonitor?> Display { get; } = display;
        internal Func<bool> ContentVisible { get; } = contentVisible;
        internal Action<bool, bool> Surface { get; } = surface;
        internal FloatingLyricsControlWindow? Panel;
        internal IFloatingPanelMaterial? Material;
        internal Rect LyricsBounds = Rect.Empty;
        internal Rect CardBounds = Rect.Empty;
        internal Rect WorkArea = Rect.Empty;
        internal bool Above;
        internal double Scale;
        internal bool HighContrast;
        internal bool Topmost;
    }
    private readonly List<Target> _targets = [];
    private readonly DispatcherTimer _timer;
    private readonly Func<FloatingLyricsControlWindow> _create;
    private FloatingMaterialCompositor? _context;
    private Target? _candidate;
    private Target? _active;
    private long _enteredAt;
    private long _leftAt;
    private bool _suppressed;
    private bool _enabled;
    private bool _light;
    private double _brightness = 0.45;
    private double _tintOpacity = 0.55;
    private bool _compositionUnavailable;
    private Target? _fadingTarget;
    private long _fadeStartedAt;

    internal FloatingLyricsPanelController(Dispatcher dispatcher, Func<FloatingLyricsControlWindow> create)
    {
        _create = create;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += Tick;
    }
    internal bool Enabled
    {
        set
        {
            _enabled = value;
            if (value) _timer.Start();
            else
            {
                _timer.Stop(); Close(immediate: true);
                foreach (var target in _targets) Release(target);
            }
        }
    }
    internal FloatingLyricsControlWindow? Panel => _active?.Panel;
    internal IEnumerable<FloatingLyricsControlWindow> Panels => _targets.Select(target => target.Panel).OfType<FloatingLyricsControlWindow>();
    internal void Configure(bool light, double brightness, double opacity)
    {
        _light = light;
        _brightness = brightness;
        _tintOpacity = opacity;
        foreach (var target in _targets) target.Material?.Configure(light, brightness, opacity);
    }
    internal void Register(Window window, Func<DisplayMonitor?> display, Func<bool> contentVisible, Action<bool, bool> surface)
    {
        _targets.Add(new(window, display, contentVisible, surface));
        window.Closed += TargetClosed;
    }
    private void TargetClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        window.Closed -= TargetClosed;
        var target = _targets.Find(item => item.Window == window);
        if (target is null) return;
        if (_active == target) Close(immediate: true);
        if (_candidate == target) _candidate = null;
        if (_fadingTarget == target) _fadingTarget = null;
        Release(target);
        _targets.Remove(target);
    }
    internal void Open(string displayId)
    {
        var target = _targets.Find(target => target.Display()?.Id == displayId);
        if (!_enabled || target is null || !Prepare(target)) return;
        _candidate = target;
        _suppressed = false;
        Show(target, focus: true);
    }
    private bool Prepare(Target target)
    {
        if (!target.Window.IsVisible || !target.ContentVisible() || target.Display() is not { } display)
        {
            if (_active == target) Close(immediate: true);
            Release(target); return false;
        }
        var bounds = GetBounds(target.Window);
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return false;
        var scale = TaskbarNativeMethods.GetDpiForWindow(new WindowInteropHelper(target.Window).Handle) / 96.0;
        if (scale <= 0) scale = display.PixelsPerDip;
        var work = display.WorkArea;
        var workArea = new Rect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
        try
        {
            if (target.Material is not null && target.Panel is { IsClosed: false } && target.LyricsBounds == bounds &&
                target.Scale == scale && target.WorkArea == workArea && target.HighContrast == SystemParameters.HighContrast)
            {
                if (target.Topmost != target.Window.Topmost)
                {
                    target.Panel.Topmost = target.Window.Topmost;
                    target.Material.Position(target.CardBounds, scale, new WindowInteropHelper(target.Window).Handle);
                    target.Topmost = target.Window.Topmost;
                }
                return true;
            }
            if (_active == target) Close(immediate: true);
            if (target.Material is not null && target.HighContrast != SystemParameters.HighContrast) Release(target);
            var placement = FloatingLyricsPanelPlacement.Place(bounds, workArea, scale);
            var requiredHeight = (bounds.Width / scale < 220 ? 136 : 104) * scale;
            if (placement.Bounds.Height < requiredHeight) { Release(target); return false; }
            target.Material ??= CreateMaterial();
            if (target.Panel is null || target.Panel.IsClosed) target.Panel = _create();
            target.Panel.Prepare(target.Window, placement, scale);
            target.Panel.SetPanelVisible(false, immediate: true);
            target.LyricsBounds = bounds;
            target.CardBounds = Rect.Union(bounds, placement.Bounds);
            target.WorkArea = workArea;
            target.Above = placement.Above;
            target.Scale = scale;
            target.HighContrast = SystemParameters.HighContrast;
            target.Topmost = target.Window.Topmost;
            target.Material.Configure(_light, _brightness, _tintOpacity);
            target.Material.Position(target.CardBounds, scale, new WindowInteropHelper(target.Window).Handle);
            target.Material.SetVisible(false, immediate: true);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException)
        {
            Release(target);
            _timer.Stop();
            Log.Error($"Floating material initialization failed: {ex}");
            return false;
        }
    }
    private void Tick(object? sender, EventArgs e)
    {
        if (!GetCursorPos(out var cursor)) return;
        var point = new System.Windows.Point(cursor.X, cursor.Y);
        var hoveredRoot = GetAncestor(WindowFromPoint(cursor), 2);
        Target? hovered = null;
        foreach (var target in _targets)
        {
            if (!Prepare(target)) continue;
            if (target.CardBounds.Contains(point) && (hoveredRoot == target.Material!.Handle ||
                hoveredRoot == new WindowInteropHelper(target.Window).Handle || hoveredRoot == new WindowInteropHelper(target.Panel!).Handle))
                hovered = target;
        }
        if (_active is not null && (hovered == _active || _active.Panel is { IsActive: true, KeepOpenForKeyboard: true }))
        { _leftAt = 0; return; }
        if (_active is not null && (GetAsyncKeyState(1) & 0x8000) != 0) return;
        if (_active is not null)
        {
            if (_leftAt == 0) _leftAt = Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(_leftAt).TotalMilliseconds >= 300) Close(suppressUntilLeave: false);
            return;
        }
        if (hovered is not null && hovered == _fadingTarget && !_suppressed &&
            Stopwatch.GetElapsedTime(_fadeStartedAt).TotalMilliseconds < 180)
        { Show(hovered); return; }
        if (hovered != _candidate)
        { _candidate = hovered; _enteredAt = Stopwatch.GetTimestamp(); _suppressed = false; }
        if (hovered is not null && !_suppressed && Stopwatch.GetElapsedTime(_enteredAt).TotalMilliseconds >= 200) Show(hovered);
    }
    private void Show(Target target, bool focus = false)
    {
        if (_active == target) { if (focus) target.Panel?.FocusControls(); return; }
        Close();
        _active = target;
        _suppressed = false;
        target.Material!.SetVisible(true);
        target.Panel!.SetPanelVisible(true);
        target.Surface(true, target.Above);
        if (focus) target.Panel.FocusControls();
    }

    private IFloatingPanelMaterial CreateMaterial()
    {
        if (!_compositionUnavailable && !SystemParameters.HighContrast)
        {
            try
            {
                _context ??= new FloatingMaterialCompositor();
                var material = new FloatingMaterialWindow(_context);
                if (material.UsesBackdrop) return material;
                material.Dispose();
                _compositionUnavailable = true;
                Log.Diagnostic("FLOATING_MATERIAL", "Host backdrop is unsupported; using translucent fallback");
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException or DllNotFoundException)
            {
                _compositionUnavailable = true;
                Log.Diagnostic("FLOATING_MATERIAL", $"Native backdrop unavailable; using translucent fallback: {ex.GetType().Name}");
            }
        }
        return new FloatingMaterialFallbackWindow();
    }
    internal void Close(bool immediate = false, bool suppressUntilLeave = true)
    {
        var target = _active;
        _active = null;
        target?.Material?.SetVisible(false, immediate);
        target?.Panel?.SetPanelVisible(false, immediate);
        target?.Surface(false, false);
        _leftAt = 0;
        _suppressed = suppressUntilLeave;
        _fadingTarget = suppressUntilLeave || immediate ? null : target;
        _fadeStartedAt = Stopwatch.GetTimestamp();
        if (!suppressUntilLeave) _candidate = null;
    }
    private static void Release(Target target)
    {
        target.Material?.Dispose(); target.Material = null;
        target.Panel?.Close(); target.Panel = null;
        target.LyricsBounds = Rect.Empty; target.CardBounds = Rect.Empty;
    }
    internal static Rect GetBounds(Window window)
    {
        if (!GetWindowRect(new WindowInteropHelper(window).Handle, out var bounds)) return Rect.Empty;
        return new Rect(bounds.Left, bounds.Top, Math.Max(0, bounds.Right - bounds.Left), Math.Max(0, bounds.Bottom - bounds.Top));
    }
    public void Dispose()
    {
        _timer.Stop(); _timer.Tick -= Tick; Close(immediate: true);
        foreach (var target in _targets) { target.Window.Closed -= TargetClosed; Release(target); }
        _targets.Clear(); _candidate = null;
        _context?.Dispose(); _context = null;
    }
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out TaskbarNativeMethods.NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(TaskbarNativeMethods.NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out TaskbarNativeMethods.NativeRect bounds);
}
