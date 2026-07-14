using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace TaskbarLyrics.App;

public partial class SpectrumTuningWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Action<SpectrumTuningSettings> _apply;
    private readonly DispatcherTimer _diagnosticsTimer;
    private bool _isWebReady;

    public SpectrumTuningSettings Settings { get; private set; }

    public SpectrumTuningWindow(SpectrumTuningSettings settings, Action<SpectrumTuningSettings> apply)
    {
        InitializeComponent();
        AppIconProvider.ApplyWindowIcon(this);
        Settings = settings.Clone();
        _apply = apply;

        _diagnosticsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _diagnosticsTimer.Tick += (_, _) => PushDiagnostics();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public void ApplyExternalSettings(SpectrumTuningSettings settings)
    {
        Settings = settings.Clone();
        PushSettings();
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
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _diagnosticsTimer.Stop();
        _diagnosticsTimer.Tick -= (_, _) => { };

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && HwndSource.FromHwnd(hwnd) is { } source)
        {
            source.RemoveHook(WndProc);
        }

        EdgeHitThrough.Detach(new WindowInteropHelper(this).Handle);

        if (TuningWebView.CoreWebView2 is not null)
        {
            TuningWebView.CoreWebView2.WebMessageReceived -= WebMessageReceived;
            TuningWebView.CoreWebView2.Navigate("about:blank");
        }

        TuningWebView.Dispose();
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
            "SpectrumTuning");
        var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await TuningWebView.EnsureCoreWebView2Async(environment);

        var core = TuningWebView.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.WebMessageReceived += WebMessageReceived;

        var htmlPath = Path.Combine(AppContext.BaseDirectory, "Web", "SpectrumTuning", "index.html");
        TuningWebView.Source = new Uri(htmlPath);
        _isWebReady = true;
        EdgeHitThrough.Attach(new WindowInteropHelper(this).Handle);
    }

    private void WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var messageJson = e.TryGetWebMessageAsString();
        if (string.IsNullOrWhiteSpace(messageJson))
        {
            messageJson = e.WebMessageAsJson;
        }

        var message = JsonSerializer.Deserialize<SpectrumMessage>(messageJson, JsonOptions);
        if (message?.Type is null)
        {
            return;
        }

        switch (message.Type)
        {
            case "ready":
                PushSettings();
                PushDiagnostics();
                _diagnosticsTimer.Start();
                break;
            case "update":
                if (message.Key is not null && message.Value is not null)
                {
                    SetParam(message.Key, message.Value.Value);
                    _apply(Settings.Clone());
                }
                break;
            case "updateAll":
                if (message.Values is not null)
                {
                    foreach (var (key, value) in message.Values)
                    {
                        SetParam(key, value);
                    }
                    _apply(Settings.Clone());
                }
                break;
            case "reset":
                Settings = SpectrumTuningSettings.CreateDefault();
                _apply(Settings.Clone());
                PushSettings();
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

    private void SetParam(string key, double value)
    {
        switch (key)
        {
            case "SampleWindow": Settings.SampleWindow = CoerceSampleWindow((int)Math.Round(value)); break;
            case "UpdateIntervalMs": Settings.UpdateIntervalMs = (int)Math.Round(value); break;
            case "MinFrequency": Settings.MinFrequency = value; break;
            case "MaxFrequency": Settings.MaxFrequency = value; break;
            case "PeakInitial": Settings.PeakInitial = value; break;
            case "PeakDecay": Settings.PeakDecay = value; break;
            case "PeakFloor": Settings.PeakFloor = value; break;
            case "PeakCeiling": Settings.PeakCeiling = value; break;
            case "NoiseFloor": Settings.NoiseFloor = value; break;
            case "OutputCurve": Settings.OutputCurve = value; break;
            case "LowBandGain": Settings.LowBandGain = value; break;
            case "BandGainStep": Settings.BandGainStep = value; break;
            case "FrequencyWeightBase": Settings.FrequencyWeightBase = value; break;
            case "FrequencyWeightSlope": Settings.FrequencyWeightSlope = value; break;
            case "BackendAttack": Settings.BackendAttack = value; break;
            case "BackendRelease": Settings.BackendRelease = value; break;
            case "FrontendRise": Settings.FrontendRise = value; break;
            case "FrontendFall": Settings.FrontendFall = value; break;
            case "MinBarHeight": Settings.MinBarHeight = value; break;
            case "BarHeightRange": Settings.BarHeightRange = value; break;
            case "BarOpacity": Settings.BarOpacity = value; break;
        }
    }

    private static int CoerceSampleWindow(double value)
    {
        return value switch
        {
            <= 768 => 512,
            <= 1536 => 1024,
            _ => 2048
        };
    }

    private void PushSettings()
    {
        if (!_isWebReady || TuningWebView.CoreWebView2 is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(Settings, JsonOptions);
        _ = TuningWebView.ExecuteScriptAsync($"window.spectrumTuning?.setSettings({json});");
    }

    private void PushDiagnostics()
    {
        if (!_isWebReady || TuningWebView.CoreWebView2 is null)
        {
            return;
        }

        var snap = SpectrumDiagnosticsState.Current;
        var payload = new
        {
            snap.IsPlaying,
            snap.IsPureMusicMode,
            snap.InputPeak,
            snap.OutputPeak,
            snap.Format
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        _ = TuningWebView.ExecuteScriptAsync($"window.spectrumTuning?.setDiagnostics({json});");
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

    private const int DwmWindowAttributeUseImmersiveDarkMode = 20;
    private const int DwmWindowAttributeWindowCornerPreference = 33;
    private const int DwmWindowCornerPreferenceRound = 2;
    private const int WindowMessageNonClientLeftButtonDown = 0x00A1;
    private const int HitTestCaption = 2;
    private const int WindowMessageNonClientHitTest = 0x0084;
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

    [DllImport("user32.dll", PreserveSig = true)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", PreserveSig = true)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, int wParam, int lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

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

    private sealed class SpectrumMessage
    {
        public string? Type { get; set; }
        public string? Key { get; set; }
        public double? Value { get; set; }
        public Dictionary<string, double>? Values { get; set; }
    }
}
