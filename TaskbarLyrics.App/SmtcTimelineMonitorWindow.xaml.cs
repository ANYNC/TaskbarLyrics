using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using TaskbarLyrics.Core.Services;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

public partial class SmtcTimelineMonitorWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly SmtcMusicSessionProvider _provider;
    private readonly LyricSyncService _lyricSyncService;
    private readonly DispatcherTimer _timer;
    private bool _isWebReady;

    public SmtcTimelineMonitorWindow(
        SmtcMusicSessionProvider provider,
        LyricSyncService lyricSyncService)
    {
        InitializeComponent();
        AppIconProvider.ApplyWindowIcon(this);
        _provider = provider;
        _lyricSyncService = lyricSyncService;
        ApplyWindowTheme();

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
        NativeWindowTheme.ThemeChanged += OnWindowThemeChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyWindowTheme();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        TaskObserver.Observe(InitializeWebViewAndStartTimerAsync(), "SMTC timeline monitor initialization");
    }

    private async Task InitializeWebViewAndStartTimerAsync()
    {
        await InitializeWebViewAsync();
        _timer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        NativeWindowTheme.ThemeChanged -= OnWindowThemeChanged;
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
        ApplyWindowTheme();

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
            DispatchToWeb("setData", null, "SMTC timeline monitor update");
            return;
        }

        var drift = diagnostics.ExtrapolatedPosition - diagnostics.RawPosition;
        var payload = new
        {
            capturedAtUtc = diagnostics.CapturedAtUtc.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            sourceAppUserModelId = diagnostics.SourceAppUserModelId,
            normalizedSource = diagnostics.NormalizedSource,
            resolvedSource = diagnostics.ResolvedSource,
            lyricSource = _provider.GetCurrentLyricSource(),
            lyricAcquisition = _lyricSyncService.CurrentLyricAcquisition,
            lyricFetchElapsedMs = _lyricSyncService.CurrentLyricFetchElapsedMilliseconds,
            lyricResolvedAtUtc = _lyricSyncService.CurrentLyricResolvedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            isPlaying = diagnostics.IsPlaying,
            isFallback = diagnostics.IsFallbackSnapshot,
            rawMs = diagnostics.RawPosition.TotalMilliseconds,
            lastUpdatedUtc = diagnostics.LastUpdatedTimeUtc.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            lastUpdateAgeMs = diagnostics.LastUpdateAge.TotalMilliseconds,
            extrapolatedMs = diagnostics.ExtrapolatedPosition.TotalMilliseconds,
            driftMs = drift.TotalMilliseconds,
            selectedMs = diagnostics.SelectedPosition.TotalMilliseconds,
            strategy = diagnostics.StrategyName,
            title = diagnostics.Title,
            artist = diagnostics.Artist
        };

        DispatchToWeb("setData", payload, "SMTC timeline monitor update");
    }

    private void WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var messageJson = e.TryGetWebMessageAsString();
        if (string.IsNullOrWhiteSpace(messageJson))
        {
            messageJson = e.WebMessageAsJson;
        }

        var message = WebViewMessageRouter.Parse(messageJson);
        if (message is null)
        {
            Log.Diagnostic("SMTC-WEB", "MessageRejected Reason='InvalidV1Envelope'");
            return;
        }

        if (!IsKnownMessageType(message.Type))
        {
            Log.Diagnostic("SMTC-WEB", "MessageRejected Reason='UnknownType'");
            return;
        }

        if (message.Payload is not { ValueKind: JsonValueKind.Object } payload)
        {
            Log.Diagnostic("SMTC-WEB", "MessageRejected Reason='PayloadNotObject'");
            return;
        }

        switch (message.Type)
        {
            case "ready":
                PushDiagnostics();
                DispatchToWeb("setTopmost", Topmost, "SMTC timeline monitor topmost update");
                break;
            case "copy":
                HandleCopyRequest(payload);
                break;
            case "toggleTopmost":
                Topmost = !Topmost;
                DispatchToWeb("setTopmost", Topmost, "SMTC timeline monitor topmost update");
                break;
            case "pause":
                _timer.Stop();
                DispatchToWeb("setPaused", true, "SMTC timeline monitor pause update");
                break;
            case "resume":
                _timer.Start();
                DispatchToWeb("setPaused", false, "SMTC timeline monitor resume update");
                break;
            case "windowDrag":
                NativeWindowInteraction.BeginDrag(this);
                break;
            case "windowResizeStart":
                if (TryReadResizeEdge(payload, out var edge))
                {
                    NativeWindowInteraction.BeginResize(this, edge);
                }
                else
                {
                    Log.Diagnostic("SMTC-WEB", "MessageRejected Reason='InvalidResizeEdge'");
                }
                break;
            case "windowMinimize":
                WindowState = WindowState.Minimized;
                break;
            case "windowClose":
                Close();
                break;
        }
    }

    private void HandleCopyRequest(JsonElement payload)
    {
        if (!payload.TryGetProperty("text", out var textElement) ||
            textElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(textElement.GetString()))
        {
            Log.Diagnostic("SMTC-WEB", "CopyFailed Reason='InvalidTextPayload'");
            DispatchToWeb("copyResult", new { success = false, message = "没有可复制的诊断数据。" }, "SMTC copy result");
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(textElement.GetString()!);
            DispatchToWeb("copyResult", new { success = true }, "SMTC copy result");
        }
        catch (Exception exception)
        {
            Log.Diagnostic(
                "SMTC-WEB",
                $"CopyFailed Reason='ClipboardException' Exception='{exception.GetType().Name}' HResult=0x{exception.HResult:X8}");
            DispatchToWeb("copyResult", new { success = false, message = "复制失败，请重试。" }, "SMTC copy result");
        }
    }

    private void DispatchToWeb(string type, object? payload, string operation)
    {
        if (!_isWebReady || MonitorWebView.CoreWebView2 is null)
        {
            return;
        }

        TaskObserver.Observe(
            MonitorWebView.ExecuteScriptAsync(
                WebViewMessageScriptFactory.Dispatch("smtcMonitor", type, payload)),
            operation);
    }

    private static bool IsKnownMessageType(string? type)
    {
        return type is "ready" or
            "copy" or
            "toggleTopmost" or
            "pause" or
            "resume" or
            "windowDrag" or
            "windowResizeStart" or
            "windowMinimize" or
            "windowClose";
    }

    private static bool TryReadResizeEdge(JsonElement payload, out string edge)
    {
        edge = string.Empty;
        if (!payload.TryGetProperty("edge", out var edgeElement) ||
            edgeElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        edge = edgeElement.GetString() ?? string.Empty;
        return edge is "left" or
            "right" or
            "top" or
            "topLeft" or
            "topRight" or
            "bottom" or
            "bottomLeft" or
            "bottomRight";
    }

    private void OnWindowThemeChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(ApplyWindowTheme);
    }

    private void ApplyWindowTheme()
    {
        NativeWindowTheme.Apply(this, MonitorWebView);
    }

}
