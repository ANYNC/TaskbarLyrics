using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TaskbarLyrics.App;

internal sealed class SmartTopmostController : IDisposable
{
    private static readonly TimeSpan ReevaluationInterval = TimeSpan.FromMilliseconds(200);

    private readonly Window _window;
    private readonly ISmartTopmostWindowProbe _windowProbe;
    private readonly DispatcherTimer _timer;
    private bool _useFloatingWindow;
    private FloatingWindowLayer _layer;
    private NativeRect? _targetMonitorBounds;
    private bool _isDisposed;
    private bool _isApplying;
    private SmartTopmostDecision? _lastDecision;

    internal SmartTopmostController(
        Window window,
        ISmartTopmostWindowProbe? windowProbe = null)
    {
        _window = window;
        _windowProbe = windowProbe ?? new SmartTopmostWindowProbe();
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = ReevaluationInterval
        };
        _timer.Tick += OnTimerTick;
    }

    internal void ApplySettings(
        bool useFloatingWindow,
        FloatingWindowLayer layer,
        NativeRect? targetMonitorBounds)
    {
        if (_isDisposed)
        {
            return;
        }

        var settingsChanged = _useFloatingWindow != useFloatingWindow ||
            _layer != layer ||
            _targetMonitorBounds != targetMonitorBounds;
        _useFloatingWindow = useFloatingWindow;
        _layer = layer;
        _targetMonitorBounds = targetMonitorBounds;
        if (settingsChanged)
        {
            _lastDecision = null;
        }

        UpdateTimerState();
        if (settingsChanged)
        {
            Reevaluate();
        }
    }

    internal void OnWindowVisibilityChanged(bool isVisible)
    {
        if (_isDisposed)
        {
            return;
        }

        if (!isVisible)
        {
            _timer.Stop();
            _lastDecision = null;
            return;
        }

        UpdateTimerState();
        Reevaluate();
    }

    internal void Reevaluate()
    {
        if (_isDisposed ||
            !_useFloatingWindow ||
            !_window.IsVisible ||
            _isApplying)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero || _targetMonitorBounds is not { } monitorBounds)
        {
            return;
        }

        _isApplying = true;
        try
        {
            SmartTopmostForegroundWindow? foregroundWindow = null;
            if (_layer == FloatingWindowLayer.Window &&
                _windowProbe.TryGetForegroundWindow(out var candidate))
            {
                foregroundWindow = candidate;
            }

            var decision = SmartTopmostPolicy.Evaluate(
                _layer,
                monitorBounds,
                foregroundWindow,
                _layer == FloatingWindowLayer.Desktop ? _windowProbe.GetDesktopPlacement() : default);
            // Avoid repeatedly raising the desktop card while Explorer restores application windows.
            if (_layer == FloatingWindowLayer.Desktop && decision.ShouldBeTopmost && _lastDecision == decision)
                return;
            if (!SmartTopmostPolicy.RequiresTopmostStateUpdate(_lastDecision, decision))
            {
                return;
            }

            var applied = _layer == FloatingWindowLayer.Desktop
                ? TaskbarPlacementService.ApplyDesktopLayerState(_window, decision.ShouldBeTopmost)
                : TaskbarPlacementService.ApplyTopmostState(
                    _window,
                    decision.ShouldBeTopmost,
                    decision.WindowToPlaceAfter);
            if (applied)
            {
                _lastDecision = decision;
            }
        }
        finally
        {
            _isApplying = false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _lastDecision = null;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        Reevaluate();
    }

    private void UpdateTimerState()
    {
        if (_useFloatingWindow && _window.IsVisible)
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }

            return;
        }

        _timer.Stop();
    }
}
