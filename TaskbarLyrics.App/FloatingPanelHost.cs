using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace TaskbarLyrics.App;

/// <summary>Arguments for a control-panel request raised by a lyrics surface.</summary>
internal sealed class FloatingPanelActionRequestedEventArgs(string action, TimeSpan? position = null) : EventArgs
{
    internal string Action { get; } = action;

    internal TimeSpan? Position { get; } = position;
}

/// <summary>
/// Owns the control-panel reveal of one floating lyrics window. The animation is driven
/// here, frame by frame: the panel height reported to the page and the native window size
/// are then always the same fraction of the animation, which a CSS-only transition cannot
/// guarantee (and the mismatch is what reads as a jump when the panel collapses).
/// </summary>
internal sealed class FloatingPanelHost : IDisposable
{
    /// <summary>
    /// Reveal duration. The page animates its panel height over the same interval with the
    /// matching easeOutCubic cubic-bezier, so both halves move as one.
    /// </summary>
    private const double DurationMs = 200;
    private const int FrameIntervalMs = 10;
    private const double AckTimeoutMs = 300;

    private readonly Window _window;
    private readonly Action<double> _reportProgress;

    private readonly DispatcherTimer _timer;
    private Rect? _baseBounds;
    private Request? _request;
    private double _linear;
    private double _target;
    private long _lastFrameTicks;
    private bool _animating;
    private bool _awaitingLayoutAck;
    private double _elapsedSinceStartMs;
    private DisplayMonitor? _currentDisplay;
    private bool _isDisposed;

    private readonly record struct Request(double PanelHeight, double Inset, double Gap);

    internal FloatingPanelHost(Window window, Action<double> reportProgress)
    {
        _window = window;
        _reportProgress = reportProgress;
        // Normal priority: a Render-priority timer only runs while the dispatcher renders,
        // and the WPF visual tree stays static during this animation (the lyrics live in a
        // WebView), which starved the animation entirely.
        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(FrameIntervalMs)
        };
        _timer.Tick += OnFrame;
    }

    /// <summary>True while the surface holds geometry for a panel, including mid-animation.</summary>
    internal bool HoldsGeometry => _baseBounds is not null;

    /// <summary>Raised when the reveal animation finished collapsing; the caller re-anchors.</summary>
    internal event EventHandler? Collapsed;

    /// <summary>
    /// Starts, or re-targets, the reveal. The direction is decided here from the real
    /// window bounds; the caller pushes the resulting layout to the page, and the first
    /// frames are held at zero so the page can apply it before the window grows.
    /// </summary>
    internal bool TryExpand(
        DisplayMonitor? display,
        double panelHeight,
        double inset,
        double gap)
    {
        if (panelHeight <= 0 || inset < 0 || gap < 0)
        {
            return false;
        }

        // Start from the real window rect: WPF's cached bounds can lag an OS move loop, and
        // a stale base would cap the reveal in the wrong place.
        var current = TaskbarPlacementService.TryGetNativeLogicalBounds(_window, GetPixelsPerDip(), out var nativeBounds)
            ? nativeBounds
            : new Rect(_window.Left, _window.Top, _window.Width, _window.Height);
        var request = new Request(panelHeight, inset, gap);
        if (_baseBounds is not null && _request == request && _target > 0)
        {
            return false;
        }

        _baseBounds ??= current;
        _request = request;
        _target = 1;
        // Hold every frame at zero growth until the page confirms it laid the panel out
        // below the lyrics, so the reveal never stretches the lyrics for a frame.
        _awaitingLayoutAck = true;
        StartAnimation(display);
        return true;
    }

    /// <summary>
    /// Called when the lyrics surface reports it applied the requested direction; the
    /// reveal may start growing now.
    /// </summary>
    internal void AcknowledgeLayout() => _awaitingLayoutAck = false;

    /// <summary>Reverses the reveal; the panel collapses with the same curve.</summary>
    internal bool Collapse(DisplayMonitor? display)
    {
        if (_baseBounds is null || (_target == 0 && _linear <= 0))
        {
            return false;
        }

        _target = 0;
        // Nothing to wait for while retracting: a pending layout acknowledgement would hold
        // the retract on its first frame while the panel above it is already moving.
        _awaitingLayoutAck = false;
        StartAnimation(display);
        return true;
    }

    /// <summary>Drops the panel immediately, without animating (settings changed, window moved).</summary>
    internal bool Reset()
    {
        if (_baseBounds is not { } baseBounds)
        {
            return false;
        }

        StopAnimation();
        _baseBounds = null;
        _request = null;
        _linear = 0;
        _target = 0;
        ApplyNativeBounds(new Rect(baseBounds.Left, baseBounds.Top, baseBounds.Width, baseBounds.Height));
        TaskbarPlacementService.SyncBoundsFromNativeWindow(_window, GetPixelsPerDip());
        _reportProgress(0);
        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        StopAnimation();
        _timer.Tick -= OnFrame;
    }

    private void StartAnimation(DisplayMonitor? display)
    {
        _currentDisplay = display;
        if (_animating || _isDisposed)
        {
            return;
        }

        _animating = true;
        _elapsedSinceStartMs = 0;
        _lastFrameTicks = Stopwatch.GetTimestamp();
        _timer.Start();
    }

    private void StopAnimation()
    {
        if (_animating)
        {
            _timer.Stop();
            _animating = false;
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsedMs = Stopwatch.GetElapsedTime(_lastFrameTicks, now).TotalMilliseconds;
        _lastFrameTicks = now;
        _elapsedSinceStartMs += elapsedMs;
        if (_baseBounds is null || _request is not { } request)
        {
            StopAnimation();
            return;
        }

        var step = elapsedMs <= 0 ? 0 : elapsedMs / DurationMs;
        if (_awaitingLayoutAck && _target > 0)
        {
            // Give up waiting after a third of a second so a lost ack cannot freeze the
            // reveal; the layout message is normally applied within a frame or two.
            if (_elapsedSinceStartMs < AckTimeoutMs)
            {
                step = 0;
            }
            else
            {
                _awaitingLayoutAck = false;
            }
        }

        _linear = _target > 0
            ? Math.Min(_target, _linear + step)
            : Math.Max(_target, _linear - step);
        // Ease-out cubic: the panel leaves quickly and settles.
        var eased = 1 - Math.Pow(1 - _linear, 3);
        var baseBounds = _baseBounds.Value;
        var (workAreaTop, workAreaBottom) = GetWorkArea(_currentDisplay);
        var bounds = FloatingPanelPlacement.Interpolate(
            baseBounds.Left,
            baseBounds.Top,
            baseBounds.Width,
            baseBounds.Height,
            request.PanelHeight,
            request.Inset,
            request.Gap,
            eased,
            workAreaTop,
            workAreaBottom);
        ApplyBounds(bounds);
        _reportProgress(eased);

        if (_linear != _target)
        {
            return;
        }

        StopAnimation();
        if (_target == 0)
        {
            FinishCollapse();
        }
    }

    private void FinishCollapse()
    {
        if (_baseBounds is not { } baseBounds)
        {
            return;
        }

        _baseBounds = null;
        _request = null;
        // Ending exactly on the recorded bounds keeps the lyrics pixel-stable. The WPF
        // properties are only read back from the native rect: writing them would let WPF
        // re-issue its own move and settle the window somewhere else.
        ApplyNativeBounds(baseBounds);
        TaskbarPlacementService.SyncBoundsFromNativeWindow(_window, GetPixelsPerDip());
        _reportProgress(0);
        Collapsed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Applies the new geometry in a single native move. While the reveal animates the WPF
    /// properties are left alone on purpose: each of their setters issues its own native
    /// move, and the resulting intermediate states (moved but not yet resized) make the
    /// lyrics jump and snap back.
    /// </summary>
    private void ApplyBounds(FloatingPanelBounds bounds) =>
        TaskbarPlacementService.ApplyNativeBounds(
            _window,
            new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height),
            GetPixelsPerDip());

    private void ApplyNativeBounds(Rect bounds) =>
        TaskbarPlacementService.ApplyNativeBounds(_window, bounds, GetPixelsPerDip());

    private double GetPixelsPerDip()
    {
        var scale = TaskbarPlacementService.GetPixelsPerDip(_window);
        return scale > 0 && double.IsFinite(scale) ? scale : 1;
    }

    private static (double Top, double Bottom) GetWorkArea(DisplayMonitor? display)
    {
        if (display is null)
        {
            var workArea = SystemParameters.WorkArea;
            return (workArea.Top, workArea.Bottom);
        }

        var metrics = TaskbarPlacementService.ConvertPhysicalDisplayMetrics(
            display.Bounds,
            display.WorkArea,
            display.PixelsPerDip);
        return (metrics.Top, metrics.Bottom);
    }
}
