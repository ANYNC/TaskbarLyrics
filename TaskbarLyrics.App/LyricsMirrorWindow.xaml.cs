using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using TaskbarLyrics.Core.Utilities;
using Size = System.Windows.Size;

namespace TaskbarLyrics.App;

internal partial class LyricsMirrorWindow : Window, IDisposable
{
    private static readonly string[] InitialScriptSlots =
        ["style", "lyrics", "cover", "spectrumTuning", "spectrum", "panelTrack", "panelTimeline"];

    private readonly Dictionary<string, string> _pendingScripts = new(StringComparer.Ordinal);
    private readonly EmbeddedTaskbarAnchor _embeddedTaskbarAnchor = new();
    private readonly SmartTopmostController _smartTopmostController;
    private readonly WindowClickThroughController _clickThroughController;
    private readonly FloatingWindowHitSurfaceController _hitSurfaceController;
    private readonly FloatingPanelHost _floatingPanelHost;
    private readonly Action<Rect, DisplayMonitor, Action<bool>> _onCoverClicked;
    private DisplayMonitor _displayMonitor;
    private AppSettings _settings = new();
    private bool _isWebReady;
    private bool _isWebInitializationStarted;
    private bool _isSpectrumScriptPending;
    private bool _isDisposed;
    private bool _isContentVisible = true;

    public LyricsMirrorWindow(DisplayMonitor displayMonitor, Action<Rect, DisplayMonitor, Action<bool>> onCoverClicked)
    {
        InitializeComponent();
        _onCoverClicked = onCoverClicked;
        // A taskbar child may become visible before its first WebView document is ready.
        // Keep the uninitialized native surface out of the taskbar until it has painted.
        LyricsWebView.Visibility = Visibility.Hidden;
        _smartTopmostController = new SmartTopmostController(this);
        _clickThroughController = new WindowClickThroughController(this);
        _hitSurfaceController = new FloatingWindowHitSurfaceController(this, RootBorder);
        _hitSurfaceController.PointerInsideChanged += OnFloatingPointerInsideChanged;
        _floatingPanelHost = new FloatingPanelHost(this, ReportFloatingPanelProgress);
        _floatingPanelHost.Collapsed += OnFloatingPanelCollapsed;
        _displayMonitor = displayMonitor;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        IsVisibleChanged += OnIsVisibleChanged;
        Closed += OnClosed;
    }

    internal bool IsEmbeddedInTaskbar => _embeddedTaskbarAnchor.IsAttached;

    internal event EventHandler<FloatingWindowPositionChangedEventArgs>? FloatingWindowPositionChanged;

    internal event EventHandler<FloatingPanelActionRequestedEventArgs>? FloatingPanelActionRequested;

    public void SetDisplayMonitor(DisplayMonitor displayMonitor)
    {
        _displayMonitor = displayMonitor;
    }

    public void ApplySettings(AppSettings settings)
    {
        // Settings changes re-anchor the mirror, so an open panel collapses first and
        // reopens on the next hover instead of fighting the new bounds.
        RestoreFloatingPanelBounds();
        _settings = settings.Clone();
        _smartTopmostController.ApplySettings(
            _settings.UseFloatingWindow,
            _settings.ForceAlwaysOnTop,
            _displayMonitor.Bounds);
        ApplyWindowInputMode();
        var pixelsPerDip = _displayMonitor.PixelsPerDip;
        var metrics = LyricsLayoutMetrics.Create(_settings, pixelsPerDip);
        Width = !_settings.UseFloatingWindow
            ? AppSettings.ClampEffectiveWindowWidth(
                _settings.WindowWidth,
                _settings.LyricsLayoutScalePercent,
                TaskbarEmbeddingLayoutPolicy.FromDisplay(_displayMonitor).MaxWidth)
            : AppSettings.ClampEffectiveWindowWidth(
                _settings.WindowWidth,
                _settings.LyricsLayoutScalePercent,
                _displayMonitor.WorkAreaWidth / pixelsPerDip) + (2 * _settings.ResolveFloatingFrameInset());
        Height = metrics.DesiredWindowHeight + (2 * _settings.ResolveFloatingFrameInset());
        RootBorder.Padding = new Thickness(
            metrics.HostHorizontalPadding,
            metrics.HostVerticalPadding,
            metrics.HostHorizontalPadding,
            metrics.HostVerticalPadding);
        LyricsContentRoot.MinHeight = metrics.MinimumContentHeight;
        CoverHitSurface.Width = metrics.CoverSize;
        CoverHitSurface.Height = metrics.CoverSize;
        var coverOnRight = _settings.CoverPosition == CoverPosition.Right;
        CoverHitSurface.HorizontalAlignment = coverOnRight
            ? System.Windows.HorizontalAlignment.Right
            : System.Windows.HorizontalAlignment.Left;
        CoverHitSurface.Margin = coverOnRight
            ? new Thickness(0, 0, metrics.LayoutHorizontalPadding, 0)
            : new Thickness(metrics.LayoutHorizontalPadding, 0, 0, 0);
        CoverHitSurface.Visibility = _settings.ShowCover ? Visibility.Visible : Visibility.Collapsed;
        LyricsWebView.Margin = new Thickness(0, 0, 0, -metrics.ViewportDescenderBuffer);
        _pendingScripts["style"] = LyricsStyleScriptFactory.Create(_settings, pixelsPerDip);
        var attachResult = !_settings.UseFloatingWindow
            ? _embeddedTaskbarAnchor.Attach(this, _settings, _displayMonitor)
            : EmbeddedTaskbarAttachResult.Unavailable;
        if (EmbeddedTaskbarEmbeddingPolicy.ShouldKeepEmbedded(attachResult))
        {
            ExecutePendingScript("style");
            return;
        }

        _embeddedTaskbarAnchor.Detach();
        TaskbarPlacementService.Anchor(this, _settings, _displayMonitor);
        ExecutePendingScript("style");
        ApplyWindowInputMode();
    }

    private void ApplyWindowInputMode()
    {
        _clickThroughController.Apply(
            _settings.UseFloatingWindow && _settings.FloatingWindowClickThrough);
        _hitSurfaceController.Apply(FloatingWindowHitSurface.ShouldTrackPointer(
            _settings.UseFloatingWindow,
            _settings.FloatingWindowClickThrough));
    }

    private void OnWindowDragRequested()
    {
        if (!_settings.UseFloatingWindow ||
            !_settings.FloatingWindowFreeDrag ||
            _settings.FloatingWindowClickThrough)
        {
            return;
        }

        RestoreFloatingPanelBounds();
        NativeWindowInteraction.BeginDrag(this);
    }

    private void OnDragCompleted()
    {
        if (!_settings.UseFloatingWindow || !_settings.FloatingWindowFreeDrag)
        {
            return;
        }

        // See MainWindow: WPF does not observe the native move loop.
        TaskbarPlacementService.SyncBoundsFromNativeWindow(this, _displayMonitor.PixelsPerDip);
        var placement = TaskbarPlacementService.TryCaptureFloatingPlacement(
            this,
            _displayMonitor,
            _displayMonitor.PixelsPerDip);
        if (placement is not null)
        {
            _settings.FloatingWindowPlacement.Set(
                _displayMonitor.Id,
                placement.OffsetX,
                placement.OffsetY);
            FloatingWindowPositionChanged?.Invoke(
                this,
                new FloatingWindowPositionChangedEventArgs(_displayMonitor.Id, placement));
        }
    }

    public void SetContentVisibility(bool isVisible)
    {
        if (_isContentVisible == isVisible)
        {
            return;
        }

        _isContentVisible = isVisible;
        RootBorder.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ApplyPresentationCommand(LyricsPresentationCommand command)
    {
        _pendingScripts[command.Slot] = command.Slot == "style"
            ? LyricsStyleScriptFactory.Create(_settings, _displayMonitor.PixelsPerDip)
            : command.Script;

        ExecutePendingScript(command.Slot);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplySettings(_settings);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Loaded -= OnLoaded;
        SourceInitialized -= OnSourceInitialized;
        IsVisibleChanged -= OnIsVisibleChanged;
        Closed -= OnClosed;
        LyricsWebView.NavigationCompleted -= OnNavigationCompleted;
        if (LyricsWebView.CoreWebView2 is { } core)
        {
            core.WebMessageReceived -= OnWebMessageReceived;
        }
        LyricsWebView.Dispose();
        _embeddedTaskbarAnchor.Dispose();
        _smartTopmostController.Dispose();
        _hitSurfaceController.Dispose();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TaskObserver.Observe(InitializeWebViewAsync(), "lyrics mirror initialization");
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
            TaskbarPlacementService.ApplyToolWindowStyle(source.Handle);
        }

        ApplySettings(_settings);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeWindowInteraction.WindowMessageExitSizeMove)
        {
            NativeWindowInteraction.EndNonClientInteraction();
            OnDragCompleted();
        }

        return IntPtr.Zero;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _smartTopmostController.OnWindowVisibilityChanged(IsVisible);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Dispose();
    }

    internal void SetControlPanelOpen(bool open)
    {
        if (!_isDisposed && _isWebReady && LyricsWebView.CoreWebView2 is not null)
        {
            TaskObserver.Observe(
                LyricsWebView.ExecuteScriptAsync(LyricsWebViewScriptFactory.SetControlPanelOpen(open)),
                "lyrics mirror cover panel state");
        }
    }

    /// <summary>
    /// Grows the window around the floating control panel exactly like the primary
    /// window does; the lyrics keep their screen position.
    /// </summary>
    private void ApplyFloatingPanelToggle(bool open, double panelHeight, double inset, double gap)
    {
        if (!_settings.UseFloatingWindow)
        {
            return;
        }

        if (!open)
        {
            _floatingPanelHost.Collapse(_displayMonitor);
            return;
        }

        if (_floatingPanelHost.TryExpand(_displayMonitor, panelHeight, inset, gap))
        {
            PushFloatingPanelLayout(open: true);
        }
    }

    private void RestoreFloatingPanelBounds()
    {
        if (_floatingPanelHost.Reset())
        {
            PushFloatingPanelLayout(open: false);
        }
    }

    private void OnFloatingPanelCollapsed(object? sender, EventArgs e)
    {
        PushFloatingPanelLayout(open: false);
    }

    private void ReportFloatingPanelProgress(double progress)
    {
        if (_isDisposed || !_isWebReady || LyricsWebView.CoreWebView2 is null)
        {
            return;
        }

        TaskObserver.Observe(
            LyricsWebView.ExecuteScriptAsync(LyricsWebViewScriptFactory.SetFloatingPanelProgress(progress)),
            "lyrics mirror floating panel progress");
    }

    /// <summary>
    /// Reports the pointer to the page: the fill that makes the surface hit-testable is only
    /// applied while the cursor is inside it, so an unmoved cursor produces no enter event.
    /// </summary>
    private void OnFloatingPointerInsideChanged(object? sender, bool inside)
    {
        if (_isDisposed || !_isWebReady || LyricsWebView.CoreWebView2 is null)
        {
            return;
        }

        TaskObserver.Observe(
            LyricsWebView.ExecuteScriptAsync(LyricsWebViewScriptFactory.SetFloatingPointer(inside)),
            "lyrics mirror floating pointer");
    }


    private void PushFloatingPanelLayout(bool open)
    {
        if (_isDisposed || !_isWebReady || LyricsWebView.CoreWebView2 is null)
        {
            return;
        }

        TaskObserver.Observe(
            LyricsWebView.ExecuteScriptAsync(LyricsWebViewScriptFactory.SetFloatingPanelLayout(open)),
            "lyrics mirror floating panel layout");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            var message = LyricsWebMessageRouter.Parse(e.TryGetWebMessageAsString());
            if (LyricsWebMessageRouter.IsWindowDragRequest(message))
            {
                OnWindowDragRequested();
                return;
            }

            if (LyricsWebMessageRouter.IsFloatingPanelLayoutApplied(message))
            {
                _floatingPanelHost.AcknowledgeLayout();
                return;
            }

            if (LyricsWebMessageRouter.TryGetFloatingPanelToggle(
                message, out var panelOpen, out var panelHeight, out var panelInset, out var panelGap))
            {
                ApplyFloatingPanelToggle(panelOpen, panelHeight, panelInset, panelGap);
                return;
            }

            if (LyricsWebMessageRouter.TryGetFloatingPanelAction(message, out var panelAction))
            {
                FloatingPanelActionRequested?.Invoke(this, new FloatingPanelActionRequestedEventArgs(panelAction));
                return;
            }

            if (LyricsWebMessageRouter.TryGetFloatingPanelSeek(message, out var panelSeek))
            {
                FloatingPanelActionRequested?.Invoke(this, new FloatingPanelActionRequestedEventArgs("seek", panelSeek));
                return;
            }

            if (!_isContentVisible || _settings.UseFloatingWindow || !_settings.EnableControlPanel || !_settings.ShowCover)
            {
                return;
            }

            if (!LyricsWebMessageRouter.TryGetCoverClick(message, out var click))
            {
                return;
            }

            var inHost = click.InHost(new Size(LyricsWebView.ActualWidth, LyricsWebView.ActualHeight));
            var topLeft = LyricsWebView.PointToScreen(inHost.TopLeft);
            var bottomRight = LyricsWebView.PointToScreen(inHost.BottomRight);
            Log.Diagnostic("CONTROL_PANEL", "Mirror cover click received by WebView");
            _onCoverClicked(new Rect(topLeft, bottomRight), _displayMonitor, SetControlPanelOpen);
        }
        catch (Exception ex)
        {
            Log.Diagnostic("CONTROL_PANEL", $"Mirror cover click ignored: {ex.GetType().Name}");
        }
    }

    private async Task InitializeWebViewAsync()
    {
        if (_isWebReady || _isWebInitializationStarted || _isDisposed)
        {
            return;
        }

        _isWebInitializationStarted = true;
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TaskbarLyrics",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            LyricsWebView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            await LyricsWebView.EnsureCoreWebView2Async(environment);
            var core = LyricsWebView.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsBuiltInErrorPageEnabled = false;
            core.WebMessageReceived += OnWebMessageReceived;
            LyricsWebView.NavigationCompleted += OnNavigationCompleted;
            LyricsWebView.NavigateToString(MainWindow.GetLyricsWebUiHtml());
        }
        catch
        {
            _isWebInitializationStarted = false;
            throw;
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || _isDisposed)
        {
            return;
        }

        _isWebReady = true;
        LyricsWebView.Visibility = Visibility.Visible;
        // The WebView child HWND only exists once the first document is loading.
        ApplyWindowInputMode();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_isDisposed || !_isWebReady)
            {
                return;
            }

            LyricsWebView.InvalidateArrange();
            UpdateLayout();
            foreach (var slot in InitialScriptSlots)
            {
                ExecutePendingScript(slot);
            }
        }), DispatcherPriority.Loaded);
    }

    private void ExecutePendingScript(string slot)
    {
        if (!_isWebReady ||
            LyricsWebView.CoreWebView2 is null ||
            !_pendingScripts.TryGetValue(slot, out var script))
        {
            return;
        }

        if (slot == "spectrum")
        {
            if (_isSpectrumScriptPending)
            {
                return;
            }

            _isSpectrumScriptPending = true;
            TaskObserver.Observe(CompleteSpectrumScriptAsync(script), "lyrics mirror spectrum update");
            return;
        }

        TaskObserver.Observe(LyricsWebView.ExecuteScriptAsync(script), $"lyrics mirror {slot} update");
    }

    private async Task CompleteSpectrumScriptAsync(string executedScript)
    {
        try
        {
            await LyricsWebView.ExecuteScriptAsync(executedScript);
        }
        finally
        {
            _isSpectrumScriptPending = false;
            if (!_isDisposed &&
                _pendingScripts.TryGetValue("spectrum", out var latestScript) &&
                !string.Equals(latestScript, executedScript, StringComparison.Ordinal))
            {
                ExecutePendingScript("spectrum");
            }
        }
    }
}
