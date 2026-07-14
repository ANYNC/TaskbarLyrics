using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace TaskbarLyrics.App;

public partial class SmtcTimelineMonitorWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SmtcMusicSessionProvider _provider;
    private readonly DispatcherTimer _timer;
    private bool _isWebReady;

    public SmtcTimelineMonitorWindow(SmtcMusicSessionProvider provider)
    {
        InitializeComponent();
        AppIconProvider.ApplyWindowIcon(this);
        _provider = provider;

        WindowStartupLocation = WindowStartupLocation.Manual;
        var work = SystemParameters.WorkArea;
        Left = work.Left + 1;
        Top = Math.Max(work.Top, work.Bottom - Height - 1);

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _timer.Tick += (_, _) => PushDiagnostics();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyWindowChromeAttributes();
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        await InitializeWebViewAsync();
        _timer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();

        if (MonitorWebView.CoreWebView2 is not null)
        {
            MonitorWebView.CoreWebView2.WebMessageReceived -= WebMessageReceived;
            MonitorWebView.CoreWebView2.Navigate("about:blank");
        }

        MonitorWebView.Dispose();
    }

    private async Task InitializeWebViewAsync()
    {
        if (_isWebReady)
        {
            return;
        }

        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskbarLyrics",
            "WebView2",
            "SmtcMonitor");
        var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await MonitorWebView.EnsureCoreWebView2Async(environment);

        var core = MonitorWebView.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.WebMessageReceived += WebMessageReceived;

        var htmlPath = Path.Combine(AppContext.BaseDirectory, "Web", "SmtcMonitor", "index.html");
        MonitorWebView.Source = new Uri(htmlPath);
        _isWebReady = true;
    }

    private void PushDiagnostics()
    {
        if (!_isWebReady || MonitorWebView.CoreWebView2 is null)
        {
            return;
        }

        var diagnostics = _provider.GetLastTimelineDiagnostics();
        if (diagnostics is null)
        {
            _ = MonitorWebView.ExecuteScriptAsync("window.smtcMonitor?.setData(null);");
            return;
        }

        var drift = diagnostics.ExtrapolatedPosition - diagnostics.RawPosition;
        var payload = new
        {
            capturedAtUtc = diagnostics.CapturedAtUtc.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            sourceAppUserModelId = diagnostics.SourceAppUserModelId,
            normalizedSource = diagnostics.NormalizedSource,
            resolvedSource = diagnostics.ResolvedSource,
            lyricSource = _provider.GetCurrentLyricSource(),
            isPlaying = diagnostics.IsPlaying,
            isFallback = diagnostics.IsFallbackSnapshot,
            rawMs = diagnostics.RawPosition.TotalMilliseconds,
            lastUpdatedUtc = diagnostics.LastUpdatedTimeUtc.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            lastUpdateAgeMs = diagnostics.LastUpdateAge.TotalMilliseconds,
            extrapolatedMs = diagnostics.ExtrapolatedPosition.TotalMilliseconds,
            driftMs = drift.TotalMilliseconds,
            selectedMs = diagnostics.SelectedPosition.TotalMilliseconds,
            strategy = diagnostics.StrategyName,
            title = diagnostics.Title,
            artist = diagnostics.Artist
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        _ = MonitorWebView.ExecuteScriptAsync($"window.smtcMonitor?.setData({json});");
    }

    private void WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var messageJson = e.TryGetWebMessageAsString();
        if (string.IsNullOrWhiteSpace(messageJson))
        {
            messageJson = e.WebMessageAsJson;
        }

        var message = JsonSerializer.Deserialize<MonitorMessage>(messageJson, JsonOptions);
        if (message?.Type is null)
        {
            return;
        }

        switch (message.Type)
        {
            case "ready":
                PushDiagnostics();
                _ = MonitorWebView.ExecuteScriptAsync($"window.smtcMonitor?.setTopmost({(Topmost ? "true" : "false")});");
                break;
            case "copy":
                if (!string.IsNullOrWhiteSpace(message.Text))
                {
                    try { System.Windows.Clipboard.SetText(message.Text); } catch { }
                }
                break;
            case "toggleTopmost":
                Topmost = !Topmost;
                _ = MonitorWebView.ExecuteScriptAsync($"window.smtcMonitor?.setTopmost({(Topmost ? "true" : "false")});");
                break;
            case "pause":
                _timer.Stop();
                _ = MonitorWebView.ExecuteScriptAsync("window.smtcMonitor?.setPaused(true);");
                break;
            case "resume":
                _timer.Start();
                _ = MonitorWebView.ExecuteScriptAsync("window.smtcMonitor?.setPaused(false);");
                break;
            case "windowDrag":
                NativeWindowInteraction.BeginDrag(this);
                break;
            case "windowResizeStart":
                NativeWindowInteraction.BeginResize(this, message.Edge);
                break;
            case "windowMinimize":
                WindowState = WindowState.Minimized;
                break;
            case "windowClose":
                Close();
                break;
        }
    }

    private void ApplyWindowChromeAttributes()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } compositionTarget })
        {
            compositionTarget.BackgroundColor = System.Windows.Media.Color.FromRgb(10, 10, 10);
        }

        var darkMode = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmWindowAttributeUseImmersiveDarkMode, ref darkMode, Marshal.SizeOf<int>());

        var cornerPreference = DwmWindowCornerPreferenceRound;
        _ = DwmSetWindowAttribute(hwnd, DwmWindowAttributeWindowCornerPreference, ref cornerPreference, Marshal.SizeOf<int>());
    }

    private const int DwmWindowAttributeUseImmersiveDarkMode = 20;
    private const int DwmWindowAttributeWindowCornerPreference = 33;
    private const int DwmWindowCornerPreferenceRound = 2;
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    private sealed class MonitorMessage
    {
        public string? Type { get; set; }
        public string? Text { get; set; }
        public string? Edge { get; set; }
    }
}
