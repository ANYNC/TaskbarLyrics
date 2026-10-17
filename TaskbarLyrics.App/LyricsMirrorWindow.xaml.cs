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
    private static readonly string[] InitialScriptSlots = ["style", "lyrics", "cover", "spectrumTuning", "spectrum"];
    private readonly Dictionary<string, string> _pendingScripts = new(StringComparer.Ordinal);
    private readonly EmbeddedTaskbarAnchor _embeddedTaskbarAnchor = new();
    private readonly SmartTopmostController _smartTopmostController;
    private readonly Action<Window, Rect, DisplayMonitor, Action<bool>, ControlPanelTriggerArea, ControlPanelActivation> _onCoverClicked;
    private readonly FloatingWindowPlacement _floatingPlacement;
    private DisplayMonitor _displayMonitor;
    private AppSettings _settings = new();
    private bool _isWebReady;
    private bool _isWebInitializationStarted;
    private bool _isSpectrumScriptPending;
    private bool _isDisposed;
    private bool _isContentVisible = true;

    public LyricsMirrorWindow(DisplayMonitor displayMonitor, Action<Window, Rect, DisplayMonitor, Action<bool>, ControlPanelTriggerArea, ControlPanelActivation> onCoverClicked,
        IAppCompositionRoot compositionRoot)
    {
        InitializeComponent();
        _floatingPlacement = compositionRoot.CreateFloatingWindowPlacement(this);
        _floatingPlacement.Refresh(displayMonitor);
        _onCoverClicked = onCoverClicked;
        // A taskbar child may become visible before its first WebView document is ready.
        // Keep the uninitialized native surface out of the taskbar until it has painted.
        LyricsWebView.Visibility = Visibility.Hidden;
        _smartTopmostController = new SmartTopmostController(this);
        _displayMonitor = displayMonitor;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        IsVisibleChanged += OnIsVisibleChanged;
        Closed += OnClosed;
    }

    internal bool IsEmbeddedInTaskbar => _embeddedTaskbarAnchor.IsAttached;
    internal DisplayMonitor DisplayMonitor => _settings.UseFloatingWindow ? _floatingPlacement.Display ?? _displayMonitor : _displayMonitor;
    internal bool IsContentVisible => _isContentVisible && _isWebReady;

    internal Rect GetCoverBounds() => NativeWindowInteraction.GetScreenBounds(CoverHitSurface);
    internal FloatingWindowSize GetFloatingSize() => FloatingWindowSize.FromSettings(_settings);
    internal void RememberFloatingPosition() => _floatingPlacement.RememberCurrentPosition();

    internal void SetFloatingLockState(bool locked, bool hovered)
    {
        if (!_isDisposed && _isWebReady)
            TaskObserver.Observe(LyricsWebView.ExecuteScriptAsync(LyricsWebViewScriptFactory.SetFloatingLockState(locked, hovered)), "floating mirror lock state");
    }

    internal void SetFloatingControlSurface(bool open, bool above, bool light)
    {
        if (_isDisposed) return;
        RootBorder.Background = _settings.UseFloatingWindow
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0)) : System.Windows.Media.Brushes.Transparent;
        RootBorder.CornerRadius = new CornerRadius(8);
        if (_isWebReady) TaskObserver.Observe(LyricsWebView.ExecuteScriptAsync(
            LyricsWebViewScriptFactory.SetFloatingControlSurface(open, light)), "floating mirror surface");
        SetControlPanelOpen(open);
    }

    public void SetDisplayMonitor(DisplayMonitor displayMonitor)
    {
        _displayMonitor = displayMonitor;
        _floatingPlacement.Refresh(displayMonitor);
    }

    internal void DragFloatingWindow()
    {
        _floatingPlacement.Drag();
        if (!_isDisposed) ApplySettings(_settings, keepControlPanelOpen: true);
    }

    public void ApplySettings(AppSettings settings, bool keepControlPanelOpen = false, bool preserveFloatingPosition = false)
    {
        _settings = settings.Clone();
        _floatingPlacement.Configure(_settings);
        if (_floatingPlacement.IsDragging) return;
        var display = DisplayMonitor;
        if (!keepControlPanelOpen) SetFloatingControlSurface(false, false, false);
        _smartTopmostController.ApplySettings(
            _settings.UseFloatingWindow,
            _settings.ForceAlwaysOnTop,
            display.Bounds);
        var pixelsPerDip = display.PixelsPerDip;
        var taskbar = TaskbarPlacementService.GetTaskbarBounds(_displayMonitor, pixelsPerDip);
        var metrics = LyricsLayoutMetrics.Create(_settings, pixelsPerDip, taskbar);
        Height = Math.Min(metrics.DesiredWindowHeight, display.Height);
        Width = !_settings.UseFloatingWindow
            ? AppSettings.ClampEffectiveWindowWidth(
                _settings.GetWindowWidth(),
                TaskbarEmbeddingLayoutPolicy.FromDisplay(_displayMonitor).MaxWidth)
            : AppSettings.ClampEffectiveWindowWidth(
                _settings.GetWindowWidth(),
                display.WorkAreaWidth / pixelsPerDip);
        Height = metrics.DesiredWindowHeight;
        RootBorder.Padding = new Thickness(
            metrics.HostHorizontalPadding,
            metrics.HostVerticalPadding,
            metrics.HostHorizontalPadding,
            metrics.HostVerticalPadding);
        LyricsContentRoot.MinHeight = metrics.MinimumContentHeight;
        CoverHitSurface.Width = metrics.CoverSize;
        CoverHitSurface.Height = metrics.CoverSize;
        var coverOnRight = _settings.CoverPosition == CoverPosition.Right;
        CoverHitSurface.VerticalAlignment = metrics.IsVertical
            ? coverOnRight ? VerticalAlignment.Bottom : VerticalAlignment.Top : VerticalAlignment.Center;
        CoverHitSurface.HorizontalAlignment = metrics.IsVertical ? System.Windows.HorizontalAlignment.Center : coverOnRight
            ? System.Windows.HorizontalAlignment.Right
            : System.Windows.HorizontalAlignment.Left;
        CoverHitSurface.Margin = metrics.IsVertical ? new Thickness(0) : coverOnRight
            ? new Thickness(0, 0, metrics.LayoutHorizontalPadding, 0)
            : new Thickness(metrics.LayoutHorizontalPadding, 0, 0, 0);
        CoverHitSurface.Visibility = _settings.ShowCover ? Visibility.Visible : Visibility.Collapsed;
        LyricsControlPanelInputSurface.Apply(LyricsHitSurface, _settings, metrics);
        LyricsWebView.Margin = new Thickness(0, 0, 0, -metrics.ViewportDescenderBuffer);
        _pendingScripts["style"] = LyricsStyleScriptFactory.Create(_settings, pixelsPerDip, taskbar);
        var attachResult = !_settings.UseFloatingWindow
            ? _embeddedTaskbarAnchor.Attach(this, _settings, _displayMonitor)
            : EmbeddedTaskbarAttachResult.Unavailable;
        if (EmbeddedTaskbarEmbeddingPolicy.ShouldKeepEmbedded(attachResult))
        {
            ExecutePendingScript("style");
            return;
        }

        _embeddedTaskbarAnchor.Detach();
        if (!_settings.UseFloatingWindow || (!preserveFloatingPosition && !_floatingPlacement.TryRestore()))
            TaskbarPlacementService.Anchor(this, _settings, display);
        ExecutePendingScript("style");
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
            ? LyricsStyleScriptFactory.Create(_settings, DisplayMonitor.PixelsPerDip,
                TaskbarPlacementService.GetTaskbarBounds(_displayMonitor, DisplayMonitor.PixelsPerDip))
            : command.Script;

        ExecutePendingScript(command.Slot);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (!_floatingPlacement.IsDragging) ApplySettings(_settings);
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
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TaskObserver.Observe(InitializeWebViewAsync(), "lyrics mirror initialization");
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            TaskbarPlacementService.ApplyToolWindowStyle(source.Handle);
        }

        ApplySettings(_settings);
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

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_isDisposed || !_isContentVisible || !(_settings.UseFloatingWindow || _settings.GetEnableControlPanel()))
        {
            return;
        }

        try
        {
            if (!LyricsWebMessageRouter.TryGetControlPanelTrigger(
                    LyricsWebMessageRouter.Parse(e.TryGetWebMessageAsString()), out var click, out var area, out var activation) ||
                !_settings.CanTriggerControlPanel(area, activation))
            {
                return;
            }

            var inHost = click.InHost(new Size(LyricsWebView.ActualWidth, LyricsWebView.ActualHeight));
            var topLeft = LyricsWebView.PointToScreen(inHost.TopLeft);
            var bottomRight = LyricsWebView.PointToScreen(inHost.BottomRight);
            Log.Diagnostic("CONTROL_PANEL", "Mirror cover click received by WebView");
            _onCoverClicked(this, new Rect(topLeft, bottomRight), DisplayMonitor, SetControlPanelOpen, area, activation);
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
