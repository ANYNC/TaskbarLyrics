using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

internal sealed record LyricsLayoutDiagnosticContext(
    FrameworkElement WebHost, CoreWebView2 Core, AppSettings Settings, DisplayMonitor? Display,
    bool Embedded, bool SurfaceHidden, bool Interactive, bool? PanelVisible);

internal sealed class LyricsLayoutDiagnostics : IDisposable
{
    private static readonly string[] WpfElementNames = ["RootBorder", "LyricsContentRoot", "CoverHitSurface", "LyricsHitSurface"];
    private static readonly Lazy<string> SnapshotScript = new(() =>
    {
        using var stream = typeof(LyricsLayoutDiagnostics).Assembly.GetManifestResourceStream(
            "TaskbarLyrics.App.Diagnostics.lyrics-layout-snapshot.js")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
    private readonly Window _window;
    private readonly Func<LyricsLayoutDiagnosticContext?> _getContext;
    private readonly LyricsLayoutDiagnosticLog _log;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _duration = Stopwatch.StartNew();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _capturing;
    private Task<string>? _scriptTask;
    private bool _disposed;
    private int _sequence;

    internal LyricsLayoutDiagnostics(Window window, Func<LyricsLayoutDiagnosticContext?> getContext, LyricsLayoutDiagnosticLog log)
    {
        _window = window;
        _getContext = getContext;
        _log = log;
        _timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    internal static LyricsLayoutDiagnostics? Start(Window window, Func<LyricsLayoutDiagnosticContext?> getContext) =>
        LyricsLayoutDiagnosticLog.Current is { } log ? new(window, getContext, log) : null;

    private bool IsCollecting => !_disposed && _duration.Elapsed < TimeSpan.FromMinutes(10);

    internal void RecordTrigger(WebViewMessage? message)
    {
        if (!IsCollecting || message?.Type is not ("coverClick" or "controlPanelTrigger")) return;
        var accepted = LyricsWebMessageRouter.TryGetControlPanelTrigger(message, out var bounds, out var area, out var activation);
        var numbers = new Dictionary<string, double?>();
        foreach (var key in new[] { "x", "y", "width", "height", "viewportWidth", "viewportHeight" })
        {
            numbers[key] = message.Payload is { ValueKind: JsonValueKind.Object } payload &&
                payload.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
        }
        _log.Write("trigger", new
        {
            window = WindowId,
            message.Type,
            message.Version,
            accepted,
            boundsFailure = message.Payload is { ValueKind: JsonValueKind.Object } p
                ? LyricsWebMessageRouter.ReadBounds(p, out _) : "InvalidPayload",
            area = accepted ? area.ToString() : null,
            activation = accepted ? activation.ToString() : null,
            numbers,
            acceptedBounds = accepted ? (object)bounds : null
        });
    }

    internal void RecordPanelDecision(string decision, Window source)
    {
        if (IsCollecting) _log.Write("panel", new
        {
            window = new WindowInteropHelper(source).Handle.ToInt64(),
            decision
        });
    }

    private long WindowId => new WindowInteropHelper(_window).Handle.ToInt64();

    private void OnTick(object? sender, EventArgs e)
    {
        if (!IsCollecting)
        {
            _timer.Stop();
            _log.Write("windowCollectionStopped", new { window = WindowId, sequence = _sequence });
            return;
        }
        // A timed-out ExecuteScriptAsync cannot be cancelled. Wait for its actual
        // completion before submitting another query to an unresponsive WebView.
        if (!_capturing && _scriptTask is not { IsCompleted: false })
            TaskObserver.Observe(CaptureAsync(), "lyrics layout diagnostics");
    }

    private async Task CaptureAsync()
    {
        _capturing = true;
        try
        {
            if (_getContext() is not { } context) return;
            var capturedAt = DateTimeOffset.UtcNow;
            var host = CaptureHost(context);
            _scriptTask = context.Core.ExecuteScriptAsync(SnapshotScript.Value);
            TaskObserver.Observe(_scriptTask, "layout diagnostic WebView query completion");
            var json = await _scriptTask.WaitAsync(TimeSpan.FromSeconds(3), _lifetime.Token);
            if (_disposed) return;
            using var document = JsonDocument.Parse(json);
            if (!_log.Write("snapshot", new
            {
                window = WindowId,
                sequence = ++_sequence,
                capturedAt,
                host,
                page = document.RootElement
            })) _timer.Stop();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or ArgumentException or
            JsonException or NotSupportedException or TimeoutException)
        {
            if (!_disposed) _log.Write("snapshotFailed", new { window = WindowId, error = ex.GetType().Name });
        }
        finally { _capturing = false; }
    }

    private object CaptureHost(LyricsLayoutDiagnosticContext context)
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        var parent = DiagnosticNativeMethods.GetParent(hwnd);
        var dpi = VisualTreeHelper.GetDpi(_window);
        var settings = context.Settings;
        var display = context.Display;
        var targetScale = display?.PixelsPerDip ?? TaskbarPlacementService.GetPixelsPerDip(_window);
        var taskbar = TaskbarPlacementService.GetTaskbarBounds(display, targetScale);
        var cursor = NativeWindowInteraction.GetCursorPosition();
        var underCursor = cursor is { } point ? DiagnosticNativeMethods.WindowFromPoint(
            new TaskbarNativeMethods.NativePoint((int)point.X, (int)point.Y)) : IntPtr.Zero;
        var cursorClass = new char[128];
        var classLength = DiagnosticNativeMethods.GetClassName(underCursor, cursorClass, cursorClass.Length);
        MainWindow.TryGetLyricsWebViewController(context.WebHost, out var controller, out var controllerFailure);
        return new
        {
            context.Embedded,
            context.SurfaceHidden,
            context.Interactive,
            context.PanelVisible,
            visible = _window.IsVisible,
            widthDip = _window.ActualWidth,
            heightDip = _window.ActualHeight,
            visualDpiX = dpi.DpiScaleX,
            visualDpiY = dpi.DpiScaleY,
            windowDpi = TaskbarNativeMethods.GetDpiForWindow(hwnd),
            targetScale,
            awareness = DiagnosticNativeMethods.GetAwarenessFromDpiAwarenessContext(
                DiagnosticNativeMethods.GetWindowDpiAwarenessContext(hwnd)),
            textScaleFactor = ReadTextScaleFactor(),
            windowRectPx = GetWindowBounds(hwnd),
            parentRectPx = GetWindowBounds(parent),
            parent = parent.ToInt64(),
            clientRectPx = DiagnosticNativeMethods.GetClientRect(hwnd, out var client) ? RectData(client) : null,
            cursorPx = cursor is { } c ? new { c.X, c.Y } : null,
            windowUnderCursor = underCursor.ToInt64(),
            windowUnderCursorClass = new string(cursorClass, 0, classLength),
            display = display is null ? null : new { display.Bounds, display.WorkArea, display.PixelsPerDip, display.IsPrimary },
            taskbar,
            metrics = LyricsLayoutMetrics.Create(settings, targetScale, taskbar),
            settings = new
            {
                settings.UseFloatingWindow,
                scalePercent = settings.GetLyricsLayoutScalePercent(),
                settings.FontSize,
                settings.CoverSize,
                settings.CoverGap,
                settings.WindowWidth,
                settings.XOffset,
                settings.YOffset,
                settings.SingleLineLyrics,
                settings.ShowCover,
                settings.FontFamily,
                settings.FontWeight,
                settings.EnableControlPanel,
                settings.ControlPanelTriggerCover,
                settings.ControlPanelTriggerLyrics,
                triggerMethod = settings.ControlPanelTriggerMethod.ToString(),
                coverPosition = settings.CoverPosition.ToString()
            },
            webHost = new
            {
                context.WebHost.ActualWidth,
                context.WebHost.ActualHeight,
                context.WebHost.IsVisible,
                context.WebHost.IsHitTestVisible,
                context.WebHost.MinHeight,
                margin = context.WebHost.Margin.ToString(),
                boundsPx = ScreenBounds(context.WebHost),
                hostType = context.WebHost.GetType().Name
            },
            wpfElements = WpfElementNames
                .ToDictionary(name => name, name => _window.FindName(name) is FrameworkElement element ? new
                {
                    element.ActualWidth,
                    element.ActualHeight,
                    element.MinHeight,
                    element.IsVisible,
                    element.IsHitTestVisible,
                    element.Opacity,
                    margin = element.Margin.ToString(),
                    boundsPx = ScreenBounds(element),
                    padding = (element as System.Windows.Controls.Border)?.Padding.ToString()
                } : null),
            browserVersion = context.Core.Environment.BrowserVersionString,
            controllerFailure,
            controller = controller is null ? null : new
            {
                controller.RasterizationScale,
                controller.ZoomFactor,
                controller.ShouldDetectMonitorScaleChanges,
                boundsMode = controller.BoundsMode.ToString(),
                controller.IsVisible,
                boundsPx = new { controller.Bounds.X, controller.Bounds.Y, controller.Bounds.Width, controller.Bounds.Height }
            }
        };
    }

    private static double? ReadTextScaleFactor()
    {
        try { return new Windows.UI.ViewManagement.UISettings().TextScaleFactor; }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or TypeLoadException) { return null; }
    }

    private static object? ScreenBounds(FrameworkElement element)
    {
        var bounds = NativeWindowInteraction.GetScreenBounds(element);
        return bounds.IsEmpty ? null : new { bounds.X, bounds.Y, bounds.Width, bounds.Height };
    }

    private static object? GetWindowBounds(IntPtr hwnd) =>
        DiagnosticNativeMethods.GetWindowRect(hwnd, out var bounds) ? RectData(bounds) : null;

    private static object RectData(TaskbarNativeMethods.NativeRect r) =>
        new { r.Left, r.Top, r.Right, r.Bottom, width = r.Right - r.Left, height = r.Bottom - r.Top };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _log.Write("windowClosed", new { window = WindowId, sequence = _sequence });
    }

    private static class DiagnosticNativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hwnd, out TaskbarNativeMethods.NativeRect rect);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClientRect(IntPtr hwnd, out TaskbarNativeMethods.NativeRect rect);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
        [DllImport("user32.dll")]
        internal static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")]
        internal static extern IntPtr WindowFromPoint(TaskbarNativeMethods.NativePoint point);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr hwnd, [Out] char[] className, int maximumCount);
    }
}
