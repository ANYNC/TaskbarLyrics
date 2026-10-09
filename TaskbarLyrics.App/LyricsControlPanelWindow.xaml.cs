using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace TaskbarLyrics.App;

public partial class LyricsControlPanelWindow : Window
{
    private const double SlideDurationMs = 220;
    private const double SlideDistanceDip = 20;
    private readonly Action<MediaHotkeyAction> _executeAction;
    private readonly Action _openSettings;
    private readonly Action _toggleTranslation;
    private readonly Action<TimeSpan> _seek;
    private Task? _initialization;
    private bool _isWebReady;
    private bool _isDisposed;
    private bool _canDismissOnDeactivate;
    private int _showVersion;
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _source = string.Empty;
    private string _sourceIconDataUri = string.Empty;
    private bool _sourceUsesDefaultIcon;
    private string _coverDataUri = string.Empty;
    private string _fallbackIconDataUri = string.Empty;
    private bool _hasTrack;
    private bool _isPlaying;
    private bool _translationEnabled;
    private bool _canSeek;
    private TimeSpan _position;
    private TimeSpan _duration;
    private DateTimeOffset _lastTimelinePushUtc;
    private DateTimeOffset _lastVolumePushUtc;
    private bool _isSliding;
    private long _slideStartedAt;
    private int _slideX;
    private int _slideFromY;
    private int _slideToY;
    private int _slideLastY;

    internal event EventHandler? Hidden;

    internal LyricsControlPanelWindow(Action<MediaHotkeyAction> executeAction,
        Action openSettings, Action toggleTranslation, Action<TimeSpan> seek)
    {
        InitializeComponent();
        _executeAction = executeAction;
        _openSettings = openSettings;
        _toggleTranslation = toggleTranslation;
        _seek = seek;
        SourceInitialized += OnSourceInitialized;
        NativeWindowTheme.ThemeChanged += OnThemeChanged;
    }

    internal void UpdateTrack(TrackInfo? track, bool isPlaying, string? coverDataUri,
        string? fallbackIconDataUri, bool translationEnabled, PlayerSourceBadge sourceBadge)
    {
        var title = track?.Title ?? string.Empty;
        var artist = track?.Artist ?? string.Empty;
        var source = sourceBadge.DisplayName ?? string.Empty;
        var sourceIconDataUri = sourceBadge.IconDataUri ?? string.Empty;
        var cover = coverDataUri ?? string.Empty;
        var fallback = fallbackIconDataUri ?? string.Empty;
        var hasTrack = track is not null;
        var contentUnchanged = _title == title && _artist == artist && _source == source &&
            _sourceIconDataUri == sourceIconDataUri && _sourceUsesDefaultIcon == sourceBadge.UsesDefaultIcon &&
            _coverDataUri == cover &&
            _fallbackIconDataUri == fallback && _hasTrack == hasTrack &&
            _translationEnabled == translationEnabled;
        if (contentUnchanged)
        {
            if (_isPlaying != isPlaying)
            {
                _isPlaying = isPlaying;
                PushPlaybackState();
            }

            return;
        }

        _title = title;
        _artist = artist;
        _source = source;
        _sourceIconDataUri = sourceIconDataUri;
        _sourceUsesDefaultIcon = sourceBadge.UsesDefaultIcon;
        _coverDataUri = cover;
        _fallbackIconDataUri = fallback;
        _hasTrack = hasTrack;
        _isPlaying = isPlaying;
        _translationEnabled = translationEnabled;
        PushSnapshot();
    }

    internal void UpdateTimeline(TimeSpan position, TimeSpan duration, bool isPlaying, bool canSeek)
    {
        _position = position;
        _duration = duration;
        _canSeek = canSeek;
        var now = DateTimeOffset.UtcNow;
        if (!IsVisible || !_isWebReady || now - _lastTimelinePushUtc < TimeSpan.FromMilliseconds(250))
        {
            return;
        }

        _lastTimelinePushUtc = now;
        PushTimeline();
        if (now - _lastVolumePushUtc >= TimeSpan.FromMilliseconds(750))
        {
            _lastVolumePushUtc = now;
            PushVolume();
        }
    }

    internal async Task ShowNearAsync(Rect coverBoundsPx, DisplayMonitor display)
    {
        if (_isDisposed)
        {
            return;
        }

        var version = ++_showVersion;
        StopSlide();
        _canDismissOnDeactivate = false;
        var work = display.WorkArea;
        var workAreaPx = new Rect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
        var displayScale = display.PixelsPerDip;
        var approximate = LyricsControlPanelPlacement.Place(
            coverBoundsPx, workAreaPx, new Size(Width * displayScale, Height * displayScale), displayScale);
        try
        {
            if (!_isWebReady)
            {
                // WebView2 needs a shown host for its first initialization. Keep that host off-screen
                // until the document and track data are ready, so the first opening can slide too.
                Left = -32000;
                Top = -32000;
                Show();
                _initialization ??= InitializeWebViewAsync();
                await _initialization;
            }

            if (_isDisposed || version != _showVersion)
            {
                return;
            }

            ApplyTheme();
            await PanelWebView.ExecuteScriptAsync(CreateSnapshotScript());
            await PanelWebView.ExecuteScriptAsync(CreateTimelineScript());
            PushVolume();
            if (_isDisposed || version != _showVersion)
            {
                return;
            }

            if (IsVisible)
            {
                Hide();
            }

            var approximateSlideDistance = SlideDistanceDip * displayScale;
            Left = approximate.X / displayScale;
            Top = (approximate.Y + approximateSlideDistance) / displayScale;
            Show();
            var (target, slideDistance) = PositionNear(coverBoundsPx, workAreaPx, displayScale);
            StartSlide(target, slideDistance);
            _canDismissOnDeactivate = true;
            Activate();
            Log.Diagnostic("CONTROL_PANEL", "WebView panel shown");
        }
        catch
        {
            HidePanel();
            _initialization = null;
            throw;
        }
    }

    internal void HidePanel()
    {
        ++_showVersion;
        StopSlide();
        _canDismissOnDeactivate = false;
        if (!IsVisible)
        {
            return;
        }

        Hide();
        Hidden?.Invoke(this, EventArgs.Empty);
    }

    private async Task InitializeWebViewAsync()
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskbarLyrics", "WebView2");
        Directory.CreateDirectory(userDataFolder);
        var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await PanelWebView.EnsureCoreWebView2Async(environment);
        var core = PanelWebView.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.WebMessageReceived += OnWebMessageReceived;

        var navigation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                navigation.TrySetResult();
            }
            else
            {
                navigation.TrySetException(new InvalidOperationException(
                    $"Control panel WebView navigation failed: {e.WebErrorStatus}"));
            }
        }

        PanelWebView.NavigationCompleted += OnNavigationCompleted;
        try
        {
            PanelWebView.NavigateToString(LoadHtml());
            await navigation.Task;
            _isWebReady = true;
        }
        finally
        {
            PanelWebView.NavigationCompleted -= OnNavigationCompleted;
        }
    }

    private static string LoadHtml()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Web", "ControlPanel");
        return File.ReadAllText(Path.Combine(folder, "index.html"))
            .Replace("{{STYLE_CSS}}", File.ReadAllText(Path.Combine(folder, "style.css")), StringComparison.Ordinal)
            .Replace("{{APP_JS}}", File.ReadAllText(Path.Combine(folder, "app.js")), StringComparison.Ordinal);
    }

    private (Point Target, int SlideDistance) PositionNear(
        Rect coverBoundsPx, Rect workAreaPx, double fallbackScale)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var pixelsPerDip = TaskbarNativeMethods.GetDpiForWindow(hwnd) / 96.0;
        if (pixelsPerDip <= 0)
        {
            pixelsPerDip = fallbackScale;
        }

        var widthPx = Math.Max(1, (int)Math.Round(ActualWidth * pixelsPerDip));
        var heightPx = Math.Max(1, (int)Math.Round(ActualHeight * pixelsPerDip));
        var position = LyricsControlPanelPlacement.Place(
            coverBoundsPx, workAreaPx, new Size(widthPx, heightPx), pixelsPerDip);
        var slideDistance = Math.Max(1, (int)Math.Round(SlideDistanceDip * pixelsPerDip));
        _ = TaskbarNativeMethods.SetWindowPos(
            hwnd, IntPtr.Zero,
            (int)Math.Round(position.X), (int)Math.Round(position.Y) + slideDistance,
            widthPx, heightPx,
            TaskbarNativeMethods.SWP_NOZORDER | TaskbarNativeMethods.SWP_NOACTIVATE);
        return (position, slideDistance);
    }

    private void StartSlide(Point target, int slideDistance)
    {
        _slideX = (int)Math.Round(target.X);
        _slideToY = (int)Math.Round(target.Y);
        _slideFromY = _slideToY + slideDistance;
        _slideLastY = _slideFromY;
        _slideStartedAt = Stopwatch.GetTimestamp();
        _isSliding = true;
        CompositionTarget.Rendering += OnSlideFrame;
    }

    private void OnSlideFrame(object? sender, EventArgs e)
    {
        if (_isDisposed || !IsVisible)
        {
            StopSlide();
            return;
        }

        var progress = Math.Clamp(Stopwatch.GetElapsedTime(_slideStartedAt).TotalMilliseconds / SlideDurationMs, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        var y = progress >= 1
            ? _slideToY
            : (int)Math.Round(_slideFromY + ((_slideToY - _slideFromY) * eased));
        if (y != _slideLastY)
        {
            _ = TaskbarNativeMethods.SetWindowPos(
                new WindowInteropHelper(this).Handle, IntPtr.Zero,
                _slideX, y, 0, 0,
                TaskbarNativeMethods.SWP_NOSIZE |
                TaskbarNativeMethods.SWP_NOZORDER |
                TaskbarNativeMethods.SWP_NOACTIVATE);
            _slideLastY = y;
        }

        if (progress >= 1)
        {
            StopSlide();
        }
    }

    private void StopSlide()
    {
        if (!_isSliding)
        {
            return;
        }

        CompositionTarget.Rendering -= OnSlideFrame;
        _isSliding = false;
    }

    private void PushSnapshot()
    {
        if (!_isWebReady || _isDisposed)
        {
            return;
        }

        TaskObserver.Observe(PanelWebView.ExecuteScriptAsync(CreateSnapshotScript()), "lyrics control panel snapshot");
    }

    private string CreateSnapshotScript() =>
        WebViewMessageScriptFactory.Dispatch("controlPanel", "snapshot", new
        {
            title = _title,
            artist = _artist,
            source = _source,
            sourceIconDataUri = _sourceIconDataUri,
            sourceUsesDefaultIcon = _sourceUsesDefaultIcon,
            coverDataUri = _coverDataUri,
            fallbackIconDataUri = _fallbackIconDataUri,
            hasTrack = _hasTrack,
            isPlaying = _isPlaying,
            translationEnabled = _translationEnabled,
            light = NativeWindowTheme.IsLight
        });

    private void PushPlaybackState()
    {
        if (!_isWebReady || _isDisposed)
        {
            return;
        }

        TaskObserver.Observe(PanelWebView.ExecuteScriptAsync(
            WebViewMessageScriptFactory.Dispatch("controlPanel", "playback", new { isPlaying = _isPlaying })),
            "lyrics control panel playback state");
    }

    private string CreateTimelineScript() =>
        WebViewMessageScriptFactory.Dispatch("controlPanel", "timeline", new
        {
            positionMs = Math.Max(0, _position.TotalMilliseconds),
            durationMs = Math.Max(0, _duration.TotalMilliseconds),
            isPlaying = _isPlaying,
            canSeek = _canSeek
        });

    private void PushTimeline() =>
        TaskObserver.Observe(PanelWebView.ExecuteScriptAsync(CreateTimelineScript()), "lyrics control panel timeline");

    private void PushVolume()
    {
        var available = SystemMasterVolume.TryRead(out var level, out var muted);
        TaskObserver.Observe(PanelWebView.ExecuteScriptAsync(
            WebViewMessageScriptFactory.Dispatch("controlPanel", "volume", new
            {
                level = available ? (int?)level : null,
                muted
            })), "lyrics control panel volume");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = WebViewMessageRouter.Parse(e.TryGetWebMessageAsString());
            if (!LyricsControlPanelMessageRouter.TryParse(message, out var command))
            {
                return;
            }

            switch (command.Kind)
            {
                case LyricsControlPanelCommandKind.Dismiss:
                    HidePanel();
                    break;
                case LyricsControlPanelCommandKind.OpenSettings:
                    HidePanel();
                    _openSettings();
                    break;
                case LyricsControlPanelCommandKind.ToggleTranslation:
                    _toggleTranslation();
                    break;
                case LyricsControlPanelCommandKind.Previous:
                    _executeAction(MediaHotkeyAction.PreviousTrack);
                    break;
                case LyricsControlPanelCommandKind.TogglePlayPause:
                    _executeAction(MediaHotkeyAction.TogglePlayPause);
                    break;
                case LyricsControlPanelCommandKind.Next:
                    _executeAction(MediaHotkeyAction.NextTrack);
                    break;
                case LyricsControlPanelCommandKind.Seek when _canSeek &&
                    _duration > TimeSpan.Zero && command.Value <= _duration.TotalMilliseconds:
                    _seek(TimeSpan.FromMilliseconds(command.Value));
                    break;
                case LyricsControlPanelCommandKind.SetVolume:
                    if (SystemMasterVolume.TrySetLevel((int)command.Value)) PushVolume();
                    break;
                case LyricsControlPanelCommandKind.ToggleMute:
                    if (SystemMasterVolume.TryToggleMute()) PushVolume();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Diagnostic("CONTROL_PANEL", $"Panel message ignored: {ex.GetType().Name}");
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e) =>
        TaskbarPlacementService.ApplyToolWindowStyle(new WindowInteropHelper(this).Handle);

    private void OnThemeChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(ApplyTheme);

    private void ApplyTheme()
    {
        if (!_isDisposed)
        {
            NativeWindowTheme.Apply(this, PanelWebView);
            var color = NativeWindowTheme.IsLight
                ? System.Windows.Media.Color.FromRgb(248, 249, 251)
                : System.Windows.Media.Color.FromRgb(32, 33, 36);
            Background = new System.Windows.Media.SolidColorBrush(color);
            PanelWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(
                color.A, color.R, color.G, color.B);
            if (PresentationSource.FromVisual(this) is HwndSource source && source.CompositionTarget is not null)
            {
                source.CompositionTarget.BackgroundColor = color;
            }
            PushSnapshot();
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_canDismissOnDeactivate)
        {
            HidePanel();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HidePanel();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _isDisposed = true;
        StopSlide();
        SourceInitialized -= OnSourceInitialized;
        NativeWindowTheme.ThemeChanged -= OnThemeChanged;
        if (PanelWebView.CoreWebView2 is { } core)
        {
            core.WebMessageReceived -= OnWebMessageReceived;
        }
        PanelWebView.Dispose();
        base.OnClosed(e);
    }
}
