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
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && HwndSource.FromHwnd(hwnd) is { } source)
        {
            source.AddHook(WndProc);
        }
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        await InitializeWebViewAsync();
        _timer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && HwndSource.FromHwnd(hwnd) is { } source)
        {
            source.RemoveHook(WndProc);
        }

        EdgeHitThrough.Detach(new WindowInteropHelper(this).Handle);

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
        EdgeHitThrough.Attach(new WindowInteropHelper(this).Handle);
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
                BeginNativeWindowDrag();
                break;
            case "windowMinimize":
                WindowState = WindowState.Minimized;
                break;
            case "windowClose":
                Close();
                break;
        }
    }

    private void BeginNativeWindowDrag()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        _ = ReleaseCapture();
        _ = SendMessage(hwnd, WindowMessageNonClientLeftButtonDown, HitTestCaption, 0);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WindowMessageNonClientHitTest || WindowState == WindowState.Maximized)
        {
            return IntPtr.Zero;
        }

        if (!GetWindowRect(hwnd, out var rect))
        {
            return IntPtr.Zero;
        }

        var x = GetSignedLowWord(lParam);
        var y = GetSignedHighWord(lParam);
        const int border = 8;

        var left = x >= rect.Left && x < rect.Left + border;
        var right = x <= rect.Right && x > rect.Right - border;
        var top = y >= rect.Top && y < rect.Top + border;
        var bottom = y <= rect.Bottom && y > rect.Bottom - border;

        handled = true;
        if (top && left) return new IntPtr(HitTestTopLeft);
        if (top && right) return new IntPtr(HitTestTopRight);
        if (bottom && left) return new IntPtr(HitTestBottomLeft);
        if (bottom && right) return new IntPtr(HitTestBottomRight);
        if (left) return new IntPtr(HitTestLeft);
        if (right) return new IntPtr(HitTestRight);
        if (top) return new IntPtr(HitTestTop);
        if (bottom) return new IntPtr(HitTestBottom);

        handled = false;
        return IntPtr.Zero;
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
    private const int WindowMessageNonClientHitTest = 0x0084;
    private const int WindowMessageNonClientLeftButtonDown = 0x00A1;
    private const int HitTestCaption = 2;
    private const int HitTestLeft = 10;
    private const int HitTestRight = 11;
    private const int HitTestTop = 12;
    private const int HitTestTopLeft = 13;
    private const int HitTestTopRight = 14;
    private const int HitTestBottom = 15;
    private const int HitTestBottomLeft = 16;
    private const int HitTestBottomRight = 17;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", PreserveSig = true)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", PreserveSig = true)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, int wParam, int lParam);

    private static int GetSignedLowWord(IntPtr value) => unchecked((short)((long)value & 0xFFFF));
    private static int GetSignedHighWord(IntPtr value) => unchecked((short)(((long)value >> 16) & 0xFFFF));

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }

    private sealed class MonitorMessage
    {
        public string? Type { get; set; }
        public string? Text { get; set; }
    }
}
