using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace TaskbarLyrics.App;

internal partial class LyricsMirrorWindow : Window, IDisposable
{
    private readonly Dictionary<string, string> _pendingScripts = new(StringComparer.Ordinal);
    private readonly EmbeddedTaskbarAnchor _embeddedTaskbarAnchor = new();
    private DisplayMonitor _displayMonitor;
    private AppSettings _settings = new();
    private bool _isWebReady;
    private bool _isWebInitializationStarted;
    private bool _isSpectrumScriptPending;
    private bool _isDisposed;
    private bool _isContentVisible = true;

    public LyricsMirrorWindow(DisplayMonitor displayMonitor)
    {
        InitializeComponent();
        _displayMonitor = displayMonitor;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    public void SetDisplayMonitor(DisplayMonitor displayMonitor)
    {
        _displayMonitor = displayMonitor;
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings.Clone();
        var pixelsPerDip = _displayMonitor.PixelsPerDip;
        var metrics = LyricsLayoutMetrics.Create(_settings, pixelsPerDip);
        Width = _settings.TaskbarEmbeddingEnabled
            ? AppSettings.ClampEmbeddedTaskbarWidth(_settings.EmbeddedTaskbarWidth)
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
        LyricsWebView.Margin = new Thickness(0, 0, 0, -metrics.ViewportDescenderBuffer);
        _pendingScripts["style"] = LyricsStyleScriptFactory.Create(_settings, pixelsPerDip);
        if (_settings.TaskbarEmbeddingEnabled &&
            _embeddedTaskbarAnchor.Attach(this, _settings, _displayMonitor))
        {
            ExecutePendingScript("style");
            return;
        }

        _embeddedTaskbarAnchor.Detach();
        TaskbarPlacementService.Anchor(this, _settings, _displayMonitor);
        TaskbarPlacementService.Attach(this, _settings.ForceAlwaysOnTop);
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
        Closed -= OnClosed;
        LyricsWebView.NavigationCompleted -= OnNavigationCompleted;
        LyricsWebView.Dispose();
        _embeddedTaskbarAnchor.Dispose();
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

    private void OnClosed(object? sender, EventArgs e)
    {
        Dispose();
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
            await LyricsWebView.EnsureCoreWebView2Async(environment);
            LyricsWebView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            var core = LyricsWebView.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsBuiltInErrorPageEnabled = false;
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
        foreach (var slot in new[] { "style", "lyrics", "cover", "spectrumTuning", "spectrum" })
        {
            ExecutePendingScript(slot);
        }
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
