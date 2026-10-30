using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using TaskbarLyrics.Core.Utilities;
using Drawing = System.Drawing;

namespace TaskbarLyrics.App;

internal sealed record LyricsDisplaySnapshot(FrameworkElement Surface, FrameworkElement? WebElement,
    CoreWebView2? Core, CoreWebView2Controller? Controller, string State);

// Opt-in, bounded observations only: never change controller visibility, styles or layout.
internal sealed class LyricsDisplayDiagnostics : IDisposable
{
    internal static readonly bool Enabled = Environment.GetEnvironmentVariable("TASKBARLYRICS_DISPLAY_DIAGNOSTICS") == "1";
    private static readonly string Session = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}";
    private static int _nextId;
    private static int _imageSamples;
    private readonly Window _window;
    private readonly bool _enabled;
    private readonly bool _captureImages;
    private readonly CancellationTokenSource _shutdown = new();
    private Task _pending = Task.CompletedTask;
    private bool _disposed;
    internal int Id { get; } = Interlocked.Increment(ref _nextId);

    internal LyricsDisplayDiagnostics(Window window, bool? enabled = null, bool captureImages = true)
    {
        _window = window;
        _enabled = enabled ?? Enabled;
        _captureImages = captureImages;
    }

    internal void Record(string stage, string state)
    {
        if (!_enabled || _disposed) return;
        try
        {
            _window.Dispatcher.VerifyAccess();
            var hwnd = new WindowInteropHelper(_window).Handle;
            var valid = hwnd != IntPtr.Zero && IsWindow(hwnd);
            var hasRect = GetWindowRect(hwnd, out var rect);
            var cloakResult = DwmGetWindowAttribute(hwnd, 20, out var cloaked, sizeof(uint));
            var hasAlpha = GetLayeredWindowAttributes(hwnd, out _, out var alpha, out var alphaFlags);
            Log.Diagnostic("DISPLAY_WINDOW", $"Pid={Environment.ProcessId} Window={Id} Role={_window.GetType().Name} " +
                $"Stage={stage} Hwnd=0x{hwnd:X} Valid={valid} Parent=0x{GetAncestor(hwnd, 1):X} " +
                $"NativeVisible={IsWindowVisible(hwnd)} Rect={(hasRect ? rect.ToString() : "Unavailable")} " +
                $"Style=0x{TaskbarNativeMethods.GetWindowLongPtr(hwnd, -16).ToInt64():X} " +
                $"ExStyle=0x{TaskbarNativeMethods.GetWindowLongPtr(hwnd, -20).ToInt64():X} " +
                $"Cloaked={(cloakResult == 0 ? cloaked.ToString(System.Globalization.CultureInfo.InvariantCulture) : "Unavailable")} " +
                $"Alpha={(hasAlpha ? $"{alpha}/{alphaFlags}" : "Unavailable")} " +
                $"WpfVisible={_window.IsVisible} Opacity={_window.Opacity} {state}");
        }
        catch (Exception exception) { Log.Diagnostic("DISPLAY_WINDOW", $"Window={Id} Stage={stage} ReadFailed={exception.GetType().Name}"); }
    }

    internal Task CaptureAsync(string reason, Func<LyricsDisplaySnapshot> readSnapshot)
    {
        if (!_enabled || _disposed) return Task.CompletedTask;
        _window.Dispatcher.VerifyAccess();
        // Share one short observation sequence during notification storms.
        if (!_pending.IsCompleted) return _pending;
        return _pending = CaptureCoreAsync(reason, readSnapshot, _shutdown.Token);
    }

    private async Task CaptureCoreAsync(string reason, Func<LyricsDisplaySnapshot> readSnapshot, CancellationToken cancellationToken)
    {
        try
        {
            for (var pass = 0; pass < 2; pass++)
            {
                await Task.Delay(pass == 0 ? 250 : 1500, cancellationToken);
                try
                {
                    var snapshot = readSnapshot();
                    Record($"Snapshot-{pass}", $"Reason={reason} {snapshot.State} " +
                        $"Surface={DescribeElement(snapshot.Surface)} Web={DescribeElement(snapshot.WebElement)}");
                    if (snapshot.Controller is { } controller)
                        Log.Diagnostic("DISPLAY_CONTROLLER", $"Window={Id} Pass={pass} Visible={controller.IsVisible} " +
                            $"Parent=0x{controller.ParentWindow:X} Bounds={controller.Bounds} " +
                            $"Scale={controller.RasterizationScale} Zoom={controller.ZoomFactor}");
                    if (snapshot.Core is not { } core) continue;
                    var page = await core.ExecuteScriptAsync(ReadPageScript).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    Log.Diagnostic("DISPLAY_PAGE", $"Window={Id} Pass={pass} State={page}");
                    if (_captureImages && Interlocked.Increment(ref _imageSamples) <= 16)
                        await CaptureImagesAsync(core, pass, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    Log.Diagnostic("DISPLAY_SNAPSHOT", $"Window={Id} Pass={pass} Failed={exception.GetType().Name} HResult=0x{exception.HResult:X}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task CaptureImagesAsync(CoreWebView2 core, int pass, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Log.GetLogsDirectory(), "DisplayDiagnostics", Session);
        Directory.CreateDirectory(directory);
        var prefix = Path.Combine(directory, $"w{Id}-p{pass}-{DateTime.Now:HHmmssfff}");
        // CapturePreview owns its stream until the native callback finishes, including
        // when the diagnostic wait times out or the owning window closes first.
        var hwnd = new WindowInteropHelper(_window).Handle;
        var screenCaptured = false;
        try
        {
            if (IsWindowVisible(hwnd) && GetWindowRect(hwnd, out var rect) &&
                rect.Width > 0 && rect.Height > 0 && (long)rect.Width * rect.Height <= 4_000_000)
            {
                using var bitmap = new Drawing.Bitmap(rect.Width, rect.Height);
                using var graphics = Drawing.Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size,
                    Drawing.CopyPixelOperation.SourceCopy);
                bitmap.Save(prefix + "-screen.png", Drawing.Imaging.ImageFormat.Png);
                screenCaptured = true;
            }
        }
        catch (Exception exception)
        {
            // A monitor may vanish between reading its bounds and copying pixels.
            // Still collect the independent WebView output for this sample.
            Log.Diagnostic("DISPLAY_IMAGE", $"Window={Id} Pass={pass} ScreenFailed={exception.GetType().Name}");
        }
        Log.Diagnostic("DISPLAY_IMAGE", $"Window={Id} Pass={pass} ScreenCaptured={screenCaptured} Prefix='{prefix}'");
        var png = await CapturePreviewAsync(core).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await File.WriteAllBytesAsync(prefix + "-web.png", png, cancellationToken);
        Log.Diagnostic("DISPLAY_IMAGE", $"Window={Id} Pass={pass} WebBytes={png.Length} ScreenCaptured={screenCaptured} Prefix='{prefix}'");
    }

    private static async Task<byte[]> CapturePreviewAsync(CoreWebView2 core)
    {
        using var stream = new MemoryStream();
        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        return stream.ToArray();
    }

    private static string DescribeElement(FrameworkElement? element) => element is null ? "Missing" :
        $"{element.Visibility}/{element.IsVisible}/{element.Opacity}/{element.ActualWidth:F2}x{element.ActualHeight:F2}";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    internal const string ReadPageScript = """
        (() => {
          const selectors = ['html','body','#layout','.shell','.lyrics-pane','#lyricsLayer','#viewport','#track','#currentLine','#currentLineText','#cover','#coverImage'];
          return {
            visibility: document.visibilityState, hidden: document.hidden,
            viewport: [innerWidth, innerHeight, devicePixelRatio],
            paint: performance.getEntriesByType('paint').map(e => ({name:e.name, time:e.startTime})),
            elements: selectors.map(selector => {
              const el = document.querySelector(selector);
              if (!el) return {selector, missing:true};
              const r = el.getBoundingClientRect(), s = getComputedStyle(el);
              return {selector, rect:[r.x,r.y,r.width,r.height], display:s.display, visibility:s.visibility,
                opacity:s.opacity, transform:s.transform, clipPath:s.clipPath, overflow:s.overflow,
                color:s.color, fontSize:s.fontSize, textLength:el.textContent.length,
                imageComplete:el.complete, naturalWidth:el.naturalWidth,
                animations:typeof el.getAnimations === 'function' ? el.getAnimations().slice(0,8).map(a => ({state:a.playState,time:a.currentTime})) : []};
            })
          };
        })()
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        internal int Left, Top, Right, Bottom;
        internal readonly int Width => Right - Left;
        internal readonly int Height => Bottom - Top;
        public override readonly string ToString() => $"{Left},{Top},{Right},{Bottom}";
    }

    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out WindowRect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint colorKey, out byte alpha, out uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out uint value, int size);
}
