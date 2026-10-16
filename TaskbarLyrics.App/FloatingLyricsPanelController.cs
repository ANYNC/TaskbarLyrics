using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

internal sealed class FloatingLyricsPanelController : IDisposable
{
    private sealed class Target(Window window, Func<DisplayMonitor?> display, Func<bool> contentVisible, Action<bool, bool> surface, Action drag)
    {
        internal Window Window { get; } = window;
        internal Func<DisplayMonitor?> Display { get; } = display;
        internal Func<bool> ContentVisible { get; } = contentVisible;
        internal Action<bool, bool> Surface { get; } = surface;
        internal Action Drag { get; } = drag;
        internal FloatingLyricsControlWindow? Panel;
        internal IFloatingPanelMaterial? Material;
        internal Rect LyricsBounds = Rect.Empty;
        internal Rect CardBounds = Rect.Empty;
        internal Rect WorkArea = Rect.Empty;
        internal bool Above;
        internal double Scale;
        internal bool HighContrast;
        internal bool Topmost;
        internal Func<Rect>? CoverBounds;
        internal Action<bool, bool>? LockChanged;
        internal bool Locked;
        internal bool CoverHovered;
        internal FloatingCoverUnlockWindow? UnlockWindow;
        internal Func<FloatingWindowSize>? ReadSize;
        internal Action<FloatingWindowSize>? PreviewSize;
        internal Action<FloatingWindowSize>? CommitSize;
        internal FloatingResizeGripWindow? ResizeGrip;
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
    private double _blurAmount = 24;
    private bool _compositionUnavailable;
    private Target? _fadingTarget;
    private long _fadeStartedAt;
    private Target? _dragging;
    private bool _updatingDrag;
    private Target? _resizing;
    private FloatingWindowSizeGesture? _sizeGesture;
    private FloatingWindowSize _sizePreview;
    private Rect _sizeOriginalBounds;

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
            if (!value) _resizing?.ResizeGrip?.CancelAdjustment();
            _enabled = value;
            if (value) _timer.Start();
            else
            {
                _timer.Stop(); Close(immediate: true);
                foreach (var target in _targets)
                {
                    SetLocked(target, false);
                    Release(target);
                }
            }
        }
    }
    internal FloatingLyricsControlWindow? Panel => _active?.Panel;
    internal IEnumerable<FloatingLyricsControlWindow> Panels => _targets.Select(target => target.Panel).OfType<FloatingLyricsControlWindow>();
    internal bool IsAdjustingSize => _resizing is not null;

    internal void RegisterSizeAdjustment(Window window, Func<FloatingWindowSize> read,
        Action<FloatingWindowSize> preview, Action<FloatingWindowSize> commit)
    {
        var target = _targets.Find(target => target.Window == window)
            ?? throw new InvalidOperationException("Register the floating window before its sizing callbacks.");
        target.ReadSize = read;
        target.PreviewSize = preview;
        target.CommitSize = commit;
    }

    private void BeginSizeAdjustment(Target target, System.Windows.Point position)
    {
        if (!_enabled || target.Locked || _active != target || _dragging is not null ||
            target.ReadSize is null || target.Display() is not { } display) return;
        var scale = TaskbarNativeMethods.GetDpiForWindow(new WindowInteropHelper(target.Window).Handle) / 96.0;
        _sizeOriginalBounds = GetBounds(target.Window);
        _sizePreview = target.ReadSize();
        _sizeGesture = new(_sizePreview, position, scale, _sizeOriginalBounds.Width / scale, display.WorkAreaWidth / scale);
        _resizing = _dragging = target;
    }

    private void MoveSizeAdjustment(Target target, System.Windows.Point position)
    {
        if (_resizing != target || _sizeGesture is null) return;
        var size = _sizeGesture.Calculate(position);
        if (size == _sizePreview) return;
        _sizePreview = size;
        PreviewSizeAdjustment(target, size, restore: false);
    }

    private void PreviewSizeAdjustment(Target target, FloatingWindowSize size, bool restore)
    {
        if ((!restore && !target.Window.IsVisible) || target.Display() is not { } display ||
            new WindowInteropHelper(target.Window).Handle == IntPtr.Zero) return;
        _updatingDrag = true;
        try
        {
            target.PreviewSize?.Invoke(size);
            target.Window.UpdateLayout();
            var bounds = GetBounds(target.Window);
            var desired = restore ? _sizeOriginalBounds : FloatingWindowPlacement.Clamp(
                new Rect(_sizeOriginalBounds.Left, _sizeOriginalBounds.Bottom - bounds.Height, bounds.Width, bounds.Height), display.WorkArea);
            TaskbarNativeMethods.SetWindowPos(new WindowInteropHelper(target.Window).Handle, IntPtr.Zero,
                (int)Math.Round(desired.Left), (int)Math.Round(desired.Top),
                (int)Math.Round(desired.Width), (int)Math.Round(desired.Height),
                TaskbarNativeMethods.SWP_NOACTIVATE | TaskbarNativeMethods.SWP_NOZORDER);
            target.LyricsBounds = Rect.Empty;
            Prepare(target, keepOpen: true);
        }
        finally { _updatingDrag = false; }
    }

    private void FinishSizeAdjustment(Target target, bool canceled)
    {
        if (_resizing != target || _sizeGesture is not { } gesture) return;
        _resizing = null;
        _sizeGesture = null;
        try
        {
            if (canceled) PreviewSizeAdjustment(target, gesture.Initial, restore: true);
            else if (_sizePreview != gesture.Initial && target.Window.IsVisible)
                target.CommitSize?.Invoke(_sizePreview);
        }
        finally
        {
            _dragging = null;
            _leftAt = 0;
            if (_enabled && target.Window.IsVisible)
            {
                target.LyricsBounds = Rect.Empty;
                Prepare(target, keepOpen: true);
            }
        }
    }
    internal void Configure(bool light, double brightness, double opacity, double blurAmount)
    {
        _light = light;
        _brightness = brightness;
        _tintOpacity = opacity;
        _blurAmount = blurAmount;
        foreach (var target in _targets) target.Material?.Configure(light, brightness, opacity, blurAmount);
    }
    internal void Register(Window window, Func<DisplayMonitor?> display, Func<bool> contentVisible, Action<bool, bool> surface,
        Action? drag = null, Func<Rect>? coverBounds = null, Action<bool, bool>? lockChanged = null)
    {
        _targets.Add(new(window, display, contentVisible, surface, drag ?? (() => NativeWindowInteraction.BeginDrag(window)))
        { CoverBounds = coverBounds, LockChanged = lockChanged });
        window.Closed += TargetClosed;
        window.LocationChanged += TargetMoved;
        window.SizeChanged += TargetResized;
    }
    private void TargetMoved(object? sender, EventArgs e)
    {
        if (_dragging is not { } target || target.Window != sender || _updatingDrag) return;
        _updatingDrag = true;
        try { Prepare(target, keepOpen: true); }
        finally { _updatingDrag = false; }
    }
    private void TargetResized(object sender, SizeChangedEventArgs e) => TargetMoved(sender, e);

    private void PanelDragRequested(object? sender, EventArgs e)
    {
        if (!_enabled || _dragging is not null || _active is not { } target || target.Panel != sender) return;
        _dragging = target;
        try { target.Drag(); }
        finally
        {
            _dragging = null;
            if (_enabled && _targets.Contains(target))
            {
                target.LyricsBounds = Rect.Empty; // Re-evaluate the opening direction only after releasing the grip.
                Prepare(target, keepOpen: true);
                _leftAt = 0;
            }
        }
    }
    private void TargetClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        window.Closed -= TargetClosed;
        window.LocationChanged -= TargetMoved;
        window.SizeChanged -= TargetResized;
        var target = _targets.Find(item => item.Window == window);
        if (target is null) return;
        SetLocked(target, false);
        if (_active == target) Close(immediate: true);
        if (_candidate == target) _candidate = null;
        if (_fadingTarget == target) _fadingTarget = null;
        Release(target);
        _targets.Remove(target);
    }
    internal void Open(string displayId, Window? source = null)
    {
        var target = _targets.Find(target => source is not null ? target.Window == source : target.Display()?.Id == displayId);
        if (!_enabled || target is null || target.Locked || !Prepare(target)) return;
        _candidate = target;
        _suppressed = false;
        Show(target, focus: true);
    }
    internal bool IsLocked(Window window) => _targets.Find(target => target.Window == window)?.Locked == true;

    internal void ToggleLock(Window window)
    {
        var target = _targets.Find(target => target.Window == window);
        if (!_enabled || target is null) return;
        SetLocked(target, !target.Locked);
    }

    private void SetLocked(Target target, bool locked)
    {
        if (target.Locked == locked) return;
        var bounds = target.CoverBounds?.Invoke() ?? Rect.Empty;
        if (locked && (!target.Window.IsVisible || !target.ContentVisible() || bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)) return;
        if (_active == target) Close(immediate: true);
        target.Locked = locked;
        if (locked)
        {
            Release(target);
            target.UnlockWindow = new FloatingCoverUnlockWindow(target.Window, () => SetLocked(target, false), hovered =>
            {
                if (!target.Locked || target.CoverHovered == hovered) return;
                target.CoverHovered = hovered;
                target.LockChanged?.Invoke(true, hovered);
            });
            var scale = TaskbarNativeMethods.GetDpiForWindow(new WindowInteropHelper(target.Window).Handle) / 96.0;
            target.UnlockWindow.Position(bounds, scale);
        }
        else
        {
            target.UnlockWindow?.Close();
            target.UnlockWindow = null;
        }
        NativeWindowInteraction.SetClickThrough(new WindowInteropHelper(target.Window).Handle, locked);
        target.CoverHovered = locked && target.UnlockWindow?.IsMouseOver == true;
        target.LockChanged?.Invoke(locked, target.CoverHovered);
        // Resume the usual hover delay after unlocking, including when the cursor stays on the cover.
        _candidate = null;
        _suppressed = false;
    }

    private bool Prepare(Target target, bool keepOpen = false)
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
                    target.ResizeGrip?.Position(target.CardBounds, scale, _active == target, _light);
                    target.Topmost = target.Window.Topmost;
                }
                return target.Material.EnsureBelow(new WindowInteropHelper(target.Window).Handle);
            }
            var remainVisible = _active == target && (keepOpen || _dragging == target);
            if (_active == target && !remainVisible) Close(immediate: true);
            if (target.Material is not null && target.HighContrast != SystemParameters.HighContrast) Release(target);
            var placement = _dragging == target
                ? FloatingLyricsPanelPlacement.Follow(bounds, target.Above, scale)
                : FloatingLyricsPanelPlacement.Place(bounds, workArea, scale);
            var requiredHeight = FloatingLyricsPanelPlacement.GetHeight(bounds.Width / scale) * scale;
            if (placement.Bounds.Height < requiredHeight) { Release(target); return false; }
            target.Material ??= CreateMaterial();
            if (target.Panel is null || target.Panel.IsClosed)
            {
                target.Panel = _create();
                target.Panel.DragRequested += PanelDragRequested;
            }
            target.Panel.Prepare(target.Window, placement, scale);
            target.Panel.SetPanelVisible(remainVisible, immediate: true);
            target.LyricsBounds = bounds;
            target.CardBounds = Rect.Union(bounds, placement.Bounds);
            target.WorkArea = workArea;
            var directionChanged = target.Above != placement.Above;
            target.Above = placement.Above;
            target.Scale = scale;
            target.HighContrast = SystemParameters.HighContrast;
            target.Topmost = target.Window.Topmost;
            target.Material.Configure(_light, _brightness, _tintOpacity, _blurAmount);
            target.Material.Position(target.CardBounds, scale, new WindowInteropHelper(target.Window).Handle);
            if (!target.Material.EnsureBelow(new WindowInteropHelper(target.Window).Handle))
            { Release(target); return false; }
            target.Material.SetVisible(remainVisible, immediate: true);
            if (target.ReadSize is not null && target.ResizeGrip is null)
            {
                target.ResizeGrip = new FloatingResizeGripWindow(target.Window);
                target.ResizeGrip.AdjustmentStarted += position => BeginSizeAdjustment(target, position);
                target.ResizeGrip.AdjustmentMoved += position => MoveSizeAdjustment(target, position);
                target.ResizeGrip.AdjustmentCompleted += canceled => FinishSizeAdjustment(target, canceled);
            }
            target.ResizeGrip?.Position(target.CardBounds, scale, remainVisible, _light);
            if (remainVisible && directionChanged) target.Surface(true, target.Above);
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
        if (_dragging is not null) return;
        if (!GetCursorPos(out var cursor)) return;
        var point = new System.Windows.Point(cursor.X, cursor.Y);
        var hoveredRoot = GetAncestor(WindowFromPoint(cursor), 2);
        Target? hovered = null;
        foreach (var target in _targets)
        {
            if (target.Locked)
            {
                var cover = target.CoverBounds?.Invoke() ?? Rect.Empty;
                if (!target.Window.IsVisible || !target.ContentVisible() || cover.IsEmpty || cover.Width <= 0 || cover.Height <= 0)
                    SetLocked(target, false);
                else
                {
                    var scale = TaskbarNativeMethods.GetDpiForWindow(new WindowInteropHelper(target.Window).Handle) / 96.0;
                    target.UnlockWindow?.Position(cover, scale);
                    continue;
                }
            }
            if (!Prepare(target)) continue;
            var hoverBounds = _active == target ? target.CardBounds : target.LyricsBounds;
            if (hoverBounds.Contains(point) && (hoveredRoot == target.Material!.Handle ||
                hoveredRoot == new WindowInteropHelper(target.Window).Handle || hoveredRoot == new WindowInteropHelper(target.Panel!).Handle ||
                target.ResizeGrip is { } grip && hoveredRoot == new WindowInteropHelper(grip).Handle))
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
        if (target.Locked) return;
        if (_active == target) { if (focus) target.Panel?.FocusControls(); return; }
        Close();
        _active = target;
        _suppressed = false;
        target.Material!.SetVisible(true);
        target.Panel!.SetPanelVisible(true);
        target.ResizeGrip?.Position(target.CardBounds, target.Scale, true, _light);
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
        _resizing?.ResizeGrip?.CancelAdjustment();
        var target = _active;
        _active = null;
        target?.Material?.SetVisible(false, immediate);
        target?.Panel?.SetPanelVisible(false, immediate);
        target?.ResizeGrip?.Hide();
        target?.Surface(false, false);
        _leftAt = 0;
        _suppressed = suppressUntilLeave;
        _fadingTarget = suppressUntilLeave || immediate ? null : target;
        _fadeStartedAt = Stopwatch.GetTimestamp();
        if (!suppressUntilLeave) _candidate = null;
    }
    private void Release(Target target)
    {
        if (_resizing == target) target.ResizeGrip?.CancelAdjustment();
        if (_active == target) Close(immediate: true);
        target.Material?.Dispose(); target.Material = null;
        if (target.Panel is not null) target.Panel.DragRequested -= PanelDragRequested;
        target.Panel?.Close(); target.Panel = null;
        target.ResizeGrip?.Close(); target.ResizeGrip = null;
        target.LyricsBounds = Rect.Empty; target.CardBounds = Rect.Empty;
    }
    internal static Rect GetBounds(Window window)
    {
        if (!GetWindowRect(new WindowInteropHelper(window).Handle, out var bounds)) return Rect.Empty;
        return new Rect(bounds.Left, bounds.Top, Math.Max(0, bounds.Right - bounds.Left), Math.Max(0, bounds.Bottom - bounds.Top));
    }
    public void Dispose()
    {
        Enabled = false;
        _timer.Tick -= Tick;
        foreach (var target in _targets)
        {
            SetLocked(target, false);
            target.Window.Closed -= TargetClosed;
            target.Window.LocationChanged -= TargetMoved;
            target.Window.SizeChanged -= TargetResized;
            Release(target);
        }
        _targets.Clear(); _candidate = null;
        _context?.Dispose(); _context = null;
    }
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out TaskbarNativeMethods.NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(TaskbarNativeMethods.NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out TaskbarNativeMethods.NativeRect bounds);
}
