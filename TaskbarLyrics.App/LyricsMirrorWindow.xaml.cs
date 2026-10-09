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
        _displayMonitor = displayMonitor;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        IsVisibleChanged += OnIsVisibleChanged;
        Closed += OnClosed;
    }

    internal bool IsEmbeddedInTaskbar => _embeddedTaskbarAnchor.IsAttached;

    public void SetDisplayMonitor(DisplayMonitor displayMonitor)
    {
        _displayMonitor = displayMonitor;
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings.Clone();
        _smartTopmostController.ApplySettings(
            _settings.UseFloatingWindow,
            _settings.ForceAlwaysOnTop,
            _displayMonitor.Bounds);
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
                _displayMonitor.WorkAreaWidth / pixelsPerDip);
        Height = metrics.DesiredWindowHeight;
        RootBorder.Padding = new Thickness(
            metrics.HostHorizontalPadding,
            metrics.HostVerticalPadding,
            metrics.HostHorizontalPadding,
            metrics.HostVerticalPadding);
        LyricsContentRoot.MinHeight = metrics.MinimumContentHeight;
        CoverHitSurface.Width = metrics.CoverSize;
        CoverHitSurface.Height = metrics.CoverSize;
        CoverHitSurface.Margin = new Thickness(metrics.LayoutHorizontalPadding, 0, 0, 0);
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
        if (_isDisposed || !_isContentVisible || !_settings.EnableControlPanel || !_settings.ShowCover)
        {
            return;
        }

        try
        {
            if (!LyricsWebMessageRouter.TryGetCoverClick(
                    LyricsWebMessageRouter.Parse(e.TryGetWebMessageAsString()), out var click))
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
