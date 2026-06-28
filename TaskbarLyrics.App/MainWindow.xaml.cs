using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Media = System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

public partial class MainWindow : Window
{
    private const int WmShowWindow = 0x0018;
    private readonly IMusicSessionProvider _musicSessionProvider;
    private readonly SystemAudioSpectrumService _audioSpectrumService = new();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _spectrumTimer;
    private readonly uint _taskbarCreatedMessage;
    private Media.Color _primaryTextColor = Media.Colors.White;
    private Media.Color _secondaryTextColor = Media.Color.FromArgb(190, 255, 255, 255);
    private LyricSyncService _lyricSyncService;
    private string _currentLine = "TaskbarLyrics 已启动";
    private string _nextLine = "等待歌词...";
    private string? _lastCoverTrackId;
    private string? _currentCoverVisualTrackId;
    private string? _currentCoverDataUri;
    private string _currentCoverFallbackText = "N";
    private string _currentCoverFallbackColorCss = "rgba(67, 160, 71, 1)";
    private bool _enableSmtcTimelineMonitor;
    private bool _enablePureMusicSpectrum = true;
    private SmtcTimelineMonitorWindow? _smtcTimelineMonitorWindow;
    private bool _isWebViewReady;
    private bool _isWebViewInitializing;
    private bool _isWebDocumentReady;
    private bool _isShowingWebErrorPage;
    private bool _isTimerTickRunning;
    private bool _isSpectrumScriptPending;
    private bool _isCurrentFramePureMusic;
    private bool _isCurrentPlaybackPlaying;
    private string? _pendingSpectrumValuesJson;
    private SpectrumTuningSettings _spectrumTuningSettings = SpectrumTuningSettings.CreateDefault();
    private int _lastWebCurrentLineIndex = -1;
    private string _lastWebTrackId = string.Empty;
    private FrameworkElement? _lyricsWebViewElement;
    private object? _lyricsWebViewControl;
    private EventInfo? _lyricsNavigationCompletedEvent;
    private Delegate? _lyricsNavigationCompletedHandler;
    private DateTimeOffset _nextTickDiagnosticsLogUtc;

    public MainWindow()
    {
        InitializeComponent();

        _musicSessionProvider = new SmtcMusicSessionProvider();
        _lyricSyncService = BuildLyricSyncService();
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(60)
        };

        _timer.Tick += OnTimerTick;
        _spectrumTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _spectrumTimer.Tick += OnSpectrumTimerTick;

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => AnchorToTaskbar();
        IsVisibleChanged += OnIsVisibleChanged;
        Closing += OnClosing;
        Closed += OnClosed;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        if (System.Windows.Application.Current is App app)
        {
            ApplySettings(app.Settings);
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        Log.SetVerboseEnabled(settings.EnableSmtcTimelineMonitor);

        if (_musicSessionProvider is SmtcMusicSessionProvider smtcProvider)
        {
            smtcProvider.SetRecognitionOrder(
                settings.SourceRecognitionOrder,
                BuildEnabledPlayerSources(settings));
        }

        Width = Math.Clamp(settings.WindowWidth, 320, 1400);
        try
        {
            var brush = (Media.Brush?)new Media.BrushConverter().ConvertFromString(settings.ForegroundColor);
            if (brush is Media.SolidColorBrush solid)
            {
                _primaryTextColor = solid.Color;
            }
            else
            {
                _primaryTextColor = Media.Colors.White;
            }
        }
        catch
        {
            _primaryTextColor = Media.Colors.White;
        }

        _secondaryTextColor = Media.Color.FromArgb(
            (byte)Math.Clamp((int)(_primaryTextColor.A * 0.76), 0, 255),
            _primaryTextColor.R,
            _primaryTextColor.G,
            _primaryTextColor.B);
        // Keep the WPF host transparent; the WebView draws the optional surface.
        RootBorder.Background = Media.Brushes.Transparent;
        RootBorder.BorderBrush = Media.Brushes.Transparent;
        RootBorder.BorderThickness = new Thickness(0);

        _lyricSyncService.Dispose();
        _lyricSyncService = BuildLyricSyncService(settings);
        _enablePureMusicSpectrum = settings.EnablePureMusicSpectrum;
        AnchorToTaskbar();
        AttachToTaskbarHost();
        PushStyleToWebView(settings);
        PushLyricsToWebView(_currentLine, _nextLine, 0, _lastWebCurrentLineIndex, _lastWebTrackId, false, false);
        _enableSmtcTimelineMonitor = settings.EnableSmtcTimelineMonitor;
        if (IsLoaded)
        {
            UpdateSmtcTimelineMonitorWindow();
        }
    }

    private static IReadOnlyCollection<string> BuildEnabledPlayerSources(AppSettings settings)
    {
        var sources = new List<string>();
        if (settings.EnableQQMusic) sources.Add("QQMusic");
        if (settings.EnableNetease) sources.Add("Netease");
        if (settings.EnableKugou) sources.Add("Kugou");
        if (settings.EnableSpotify) sources.Add("Spotify");
        return sources;
    }

    private LyricSyncService BuildLyricSyncService(AppSettings? settings = null)
    {
        var providers = new List<ILyricProvider>
        {
            new GenericSmtcLyricProvider()
        };

        if (settings?.EnableNetease != false)
        {
            providers.Add(new LyricifyLyricProvider("Netease", Lyricify.Lyrics.Searchers.Searchers.Netease));
        }

        if (settings?.EnableQQMusic != false)
        {
            providers.Add(new LyricifyLyricProvider("QQMusic", Lyricify.Lyrics.Searchers.Searchers.QQMusic));
        }

        if (settings?.EnableKugou != false)
        {
            providers.Add(new LyricifyLyricProvider("Kugou", Lyricify.Lyrics.Searchers.Searchers.Kugou));
        }
        return new LyricSyncService(
            new LyricProviderRegistry(providers),
            _ => settings?.ShowLyricTranslation == true);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        AnchorToTaskbar();
        AttachToTaskbarHost();
        await EnsureLyricsWebViewReadyAsync();
        _audioSpectrumService.Start();
        ApplySpectrumTuning(_spectrumTuningSettings);
        PushLyricsToWebView(_currentLine, _nextLine, 0, _lastWebCurrentLineIndex, _lastWebTrackId, false, false);
        UpdateSmtcTimelineMonitorWindow();
        _timer.Start();
        _spectrumTimer.Start();
    }

private void OnSourceInitialized(object? sender, EventArgs e)
{
    if (PresentationSource.FromVisual(this) is HwndSource source)
    {
        source.AddHook(WndProc);

        var hwnd = source.Handle;
        var extendedStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)(extendedStyle.ToInt64() | NativeMethods.WS_EX_TOOLWINDOW));
    }
}

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
            if (!_spectrumTimer.IsEnabled)
            {
                _spectrumTimer.Start();
            }

            AnchorToTaskbar();
            AttachToTaskbarHost();
        }
        else
        {
            if (_timer.IsEnabled)
            {
                _timer.Stop();
            }
            if (_spectrumTimer.IsEnabled)
            {
                _spectrumTimer.Stop();
            }
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App app && !app.IsExiting)
        {
            e.Cancel = true;
            app.MarkLyricsHiddenByUser();
            Hide();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        CloseSmtcTimelineMonitorWindow();
        _timer.Stop();
        _spectrumTimer.Stop();
        _timer.Tick -= OnTimerTick;
        _spectrumTimer.Tick -= OnSpectrumTimerTick;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Loaded -= OnLoaded;
        SourceInitialized -= OnSourceInitialized;
        Closing -= OnClosing;
        Closed -= OnClosed;
        IsVisibleChanged -= OnIsVisibleChanged;

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.RemoveHook(WndProc);
        }

        DetachWebViewNavigationHandler();

        _lyricSyncService.Dispose();
        _audioSpectrumService.Dispose();
    }

    private void UpdateSmtcTimelineMonitorWindow()
    {
        if (_musicSessionProvider is not SmtcMusicSessionProvider smtcProvider || !_enableSmtcTimelineMonitor)
        {
            CloseSmtcTimelineMonitorWindow();
            return;
        }

        if (_smtcTimelineMonitorWindow is { IsVisible: true })
        {
            return;
        }

        var monitorWindow = new SmtcTimelineMonitorWindow(smtcProvider);
        if (IsLoaded && IsVisible)
        {
            monitorWindow.Owner = this;
        }

        monitorWindow.Closed += OnSmtcTimelineMonitorClosed;
        _smtcTimelineMonitorWindow = monitorWindow;
        monitorWindow.Show();
    }

    private void CloseSmtcTimelineMonitorWindow()
    {
        if (_smtcTimelineMonitorWindow is null)
        {
            return;
        }

        _smtcTimelineMonitorWindow.Closed -= OnSmtcTimelineMonitorClosed;
        _smtcTimelineMonitorWindow.Close();
        _smtcTimelineMonitorWindow = null;
    }

    private void OnSmtcTimelineMonitorClosed(object? sender, EventArgs e)
    {
        if (sender is SmtcTimelineMonitorWindow window)
        {
            window.Closed -= OnSmtcTimelineMonitorClosed;
        }

        _smtcTimelineMonitorWindow = null;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            AnchorToTaskbar();
            AttachToTaskbarHost();
        });
    }

    private static void LogToFile(string message)
    {
        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_debug.log");
            LogFileWriter.AppendLine(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
        }
        catch {}
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (_isTimerTickRunning)
        {
            return;
        }

        _isTimerTickRunning = true;
        try
        {
            EnsureVisibleIfExpected();

            var snapshot = await _musicSessionProvider.GetCurrentAsync();
            UpdateCover(snapshot);

            var frame = await _lyricSyncService.GetDisplayFrameAsync(snapshot);
            LogTickDiagnostics(snapshot, frame);

            if (_musicSessionProvider is SmtcMusicSessionProvider smtcProvider)
            {
                smtcProvider.SetCurrentLyricSource(_lyricSyncService.CurrentLyricSourceApp);
            }

            var current = string.IsNullOrWhiteSpace(frame.CurrentLine)
                ? "等待播放..."
                : frame.CurrentLine;

            var next = frame.NextLine;
            _lastWebCurrentLineIndex = frame.CurrentLineIndex;
            _lastWebTrackId = snapshot.Track is null
                ? string.Empty
                : LyricSyncService.BuildStableTrackIdentity(snapshot.Track);

            UpdateLyricLines(current, next, frame.LineProgress);
            _isCurrentFramePureMusic = frame.IsPureMusic && _enablePureMusicSpectrum;
            _isCurrentPlaybackPlaying = snapshot.IsPlaying;
            PushLyricsToWebView(current, next, frame.LineProgress, frame.CurrentLineIndex, _lastWebTrackId, _isCurrentFramePureMusic, snapshot.IsPlaying);
        }
        catch (Exception ex)
        {
            LogToFile($"EXCEPTION in OnTimerTick: {ex}");
            _currentLine = $"歌词服务异常: {ex.Message}";
            _nextLine = string.Empty;
            _isCurrentFramePureMusic = false;
            _isCurrentPlaybackPlaying = false;
            PushLyricsToWebView(_currentLine, _nextLine, 0, _lastWebCurrentLineIndex, _lastWebTrackId, false, false);
            Debug.WriteLine(ex);
        }
        finally
        {
            _isTimerTickRunning = false;
        }
    }

    public void ApplySpectrumTuning(SpectrumTuningSettings settings)
    {
        var snapshot = settings.Clone();
        _spectrumTuningSettings = snapshot;
        _audioSpectrumService.ApplyTuning(snapshot);
        _spectrumTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(snapshot.UpdateIntervalMs, 16, 100));
        PushSpectrumTuningToWebView(snapshot);
    }

    private void OnSpectrumTimerTick(object? sender, EventArgs e)
    {
        if (!_isCurrentFramePureMusic)
        {
            return;
        }

        PushSpectrumToWebView(_isCurrentPlaybackPlaying && _audioSpectrumService.IsAvailable
            ? _audioSpectrumService.GetSpectrum()
            : SystemAudioSpectrumService.Silence);
    }

    private void LogTickDiagnostics(PlaybackSnapshot snapshot, LyricDisplayFrame frame)
    {
        if (!_enableSmtcTimelineMonitor || DateTimeOffset.UtcNow < _nextTickDiagnosticsLogUtc)
        {
            return;
        }

        _nextTickDiagnosticsLogUtc = DateTimeOffset.UtcNow.AddSeconds(1);
        if (snapshot.Track is null)
        {
            LogToFile("SMTC: No active track found (Track is null)");
        }
        else
        {
            LogToFile($"SMTC: Title='{snapshot.Track.Title}', Artist='{snapshot.Track.Artist}', App='{snapshot.Track.SourceApp}', Playing={snapshot.IsPlaying}, Pos={snapshot.Position}, CoverLen={snapshot.CoverImageBytes?.Length ?? 0}");
        }

        LogToFile($"Sync: Current='{frame.CurrentLine}', Next='{frame.NextLine}', Prog={frame.LineProgress:F3}, SourceApp='{_lyricSyncService.CurrentLyricSourceApp}'");
    }

    private void UpdateLyricLines(string current, string next, double lineProgress)
    {
        _currentLine = current;
        _nextLine = next;
    }

    private void UpdateCover(PlaybackSnapshot snapshot)
    {
        var trackId = snapshot.Track?.Id;
        var isSameRequestedTrack = string.Equals(trackId, _lastCoverTrackId, StringComparison.Ordinal);
        var isCurrentTrackVisual = string.Equals(trackId, _currentCoverVisualTrackId, StringComparison.Ordinal);
        if (isSameRequestedTrack && isCurrentTrackVisual)
        {
            // Proceed only if we previously had no cover for this track but now we have bytes.
            if (_currentCoverDataUri != null || snapshot.CoverImageBytes == null)
            {
                return;
            }
        }

        _lastCoverTrackId = trackId;

        var sourceApp = snapshot.Track?.SourceApp ?? string.Empty;
        (_currentCoverFallbackText, var fallbackColor) = GetCoverFallback(sourceApp);
        _currentCoverFallbackColorCss = ToCssColor(fallbackColor);

        if (snapshot.CoverImageBytes is { Length: > 0 } bytes)
        {
            _currentCoverDataUri = BuildCoverDataUri(bytes);
            _currentCoverVisualTrackId = trackId;
            PushCoverToWebView();
            return;
        }

        if (snapshot.IsCoverLoading)
        {
            return;
        }

        _currentCoverDataUri = null;
        _currentCoverVisualTrackId = trackId;
        PushCoverToWebView();
    }

    private static (string Text, Media.Color Color) GetCoverFallback(string sourceApp)
    {
        if (sourceApp.Equals("QQMusic", StringComparison.OrdinalIgnoreCase))
        {
            return ("Q", Media.Color.FromRgb(41, 182, 246));
        }

        if (sourceApp.Equals("Spotify", StringComparison.OrdinalIgnoreCase))
        {
            return ("S", Media.Color.FromRgb(30, 215, 96));
        }

        if (sourceApp.Equals("Netease", StringComparison.OrdinalIgnoreCase))
        {
            return ("N", Media.Color.FromRgb(229, 57, 53));
        }

        if (sourceApp.Equals("Kugou", StringComparison.OrdinalIgnoreCase))
        {
            return ("K", Media.Color.FromRgb(52, 152, 219));
        }

        return ("♫", Media.Color.FromRgb(99, 102, 241));
    }

    private async Task EnsureLyricsWebViewReadyAsync()
    {
        if (_isWebViewReady || _isWebViewInitializing)
        {
            return;
        }

        _isWebViewInitializing = true;
        try
        {
            var webViewControl = EnsureWebViewControlCreated();
            var webViewUserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TaskbarLyrics",
                "WebView2");
            Directory.CreateDirectory(webViewUserDataFolder);
            var webViewEnvironment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: webViewUserDataFolder);

            await EnsureCoreWebView2Async(webViewControl, webViewEnvironment);
            TrySetDefaultBackgroundColor(webViewControl, System.Drawing.Color.Transparent);
            var coreWebView2 = TryGetCoreWebView2(webViewControl);
            if (coreWebView2 is not null)
            {
                coreWebView2.Settings.IsStatusBarEnabled = false;
                coreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                coreWebView2.Settings.AreDevToolsEnabled = false;
                coreWebView2.Settings.IsZoomControlEnabled = false;
                coreWebView2.Settings.IsBuiltInErrorPageEnabled = false;
            }

            AttachWebViewNavigationHandler(webViewControl);
            NavigateWebViewToString(GetLyricsWebUiHtml());
            _isWebViewReady = true;
            _isShowingWebErrorPage = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            _isWebViewReady = false;
            _isWebDocumentReady = false;
            _isShowingWebErrorPage = false;
        }
        finally
        {
            _isWebViewInitializing = false;
        }
    }

    private void PushLyricsToWebView(string current, string next, double lineProgress, int currentLineIndex, string? trackId, bool isPureMusic, bool isPlaying)
    {
        if (!_isWebViewReady || !_isWebDocumentReady || _isShowingWebErrorPage)
        {
            return;
        }

        var currentJson = JsonSerializer.Serialize(current);
        var nextJson = JsonSerializer.Serialize(next);
        var progressJson = JsonSerializer.Serialize(Math.Clamp(lineProgress, 0, 1));
        var lineIndexJson = JsonSerializer.Serialize(currentLineIndex);
        var trackIdJson = JsonSerializer.Serialize(trackId ?? string.Empty);
        var isPureMusicJson = JsonSerializer.Serialize(isPureMusic);
        var isPlayingJson = JsonSerializer.Serialize(isPlaying);
        var script = $"window.taskbarLyrics?.setLyrics({currentJson}, {nextJson}, {progressJson}, {lineIndexJson}, {trackIdJson}, {isPureMusicJson}, {isPlayingJson});";
        _ = ExecuteWebScriptAsync(script);
    }

    private void PushCoverToWebView()
    {
        if (!_isWebViewReady || !_isWebDocumentReady || _isShowingWebErrorPage)
        {
            return;
        }

        var dataUriJson = JsonSerializer.Serialize(_currentCoverDataUri ?? string.Empty);
        var fallbackTextJson = JsonSerializer.Serialize(_currentCoverFallbackText);
        var fallbackColorJson = JsonSerializer.Serialize(_currentCoverFallbackColorCss);
        var script = $"window.taskbarLyrics?.setCover({dataUriJson}, {fallbackTextJson}, {fallbackColorJson});";
        _ = ExecuteWebScriptAsync(script);
    }

    private void PushSpectrumToWebView(IReadOnlyList<float> bars)
    {
        if (!_isWebViewReady || !_isWebDocumentReady || _isShowingWebErrorPage)
        {
            return;
        }

        var valuesJson = JsonSerializer.Serialize(bars.Select(value => Math.Clamp(value, 0f, 1f)));
        if (_isSpectrumScriptPending)
        {
            _pendingSpectrumValuesJson = valuesJson;
            return;
        }

        SendSpectrumToWebView(valuesJson);
    }

    private void PushSpectrumTuningToWebView(SpectrumTuningSettings settings)
    {
        if (!_isWebViewReady || !_isWebDocumentReady || _isShowingWebErrorPage)
        {
            return;
        }

        var payload = new
        {
            rise = settings.FrontendRise,
            fall = settings.FrontendFall,
            minHeight = settings.MinBarHeight,
            heightRange = settings.BarHeightRange,
            opacity = settings.BarOpacity
        };
        var payloadJson = JsonSerializer.Serialize(payload);
        var script = $"window.taskbarLyrics?.setSpectrumTuning({payloadJson});";
        _ = ExecuteWebScriptAsync(script);
    }

    private void SendSpectrumToWebView(string valuesJson)
    {
        var script = $"window.taskbarLyrics?.setSpectrum({valuesJson});";
        var task = ExecuteWebScriptAsync(script);
        if (task is null)
        {
            return;
        }

        _isSpectrumScriptPending = true;
        _ = task.ContinueWith(_ =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _isSpectrumScriptPending = false;
                var pendingValuesJson = _pendingSpectrumValuesJson;
                _pendingSpectrumValuesJson = null;
                if (!string.IsNullOrEmpty(pendingValuesJson) &&
                    _isWebViewReady &&
                    _isWebDocumentReady &&
                    !_isShowingWebErrorPage)
                {
                    SendSpectrumToWebView(pendingValuesJson);
                }
            }));
        }, TaskScheduler.Default);
    }

    private static string BuildCoverDataUri(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var mime = DetectImageMimeType(bytes);
        var base64 = Convert.ToBase64String(bytes);
        return $"data:{mime};base64,{base64}";
    }

    private static string DetectImageMimeType(byte[] bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return "image/png";
        }

        if (bytes.Length >= 3 &&
            bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 6 &&
            bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 &&
            bytes[3] == 0x38 && (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61)
        {
            return "image/gif";
        }

        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return "image/webp";
        }

        if (bytes.Length >= 12 &&
            bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70)
        {
            return "image/avif";
        }

        return "image/jpeg";
    }

    private void OnLyricsWebViewNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            _isWebDocumentReady = false;
            if (!_isShowingWebErrorPage)
            {
                _isShowingWebErrorPage = true;
                NavigateWebViewToString(GetLyricsWebErrorHtml($"WebView navigation failed: {e.WebErrorStatus}"));
            }
            return;
        }
        _isWebDocumentReady = true;
        if (_isShowingWebErrorPage)
        {
            return;
        }

        if (System.Windows.Application.Current is App app)
        {
            PushStyleToWebView(app.Settings);
        }

        PushLyricsToWebView(_currentLine, _nextLine, 0, _lastWebCurrentLineIndex, _lastWebTrackId, false, false);
        PushCoverToWebView();
        PushSpectrumTuningToWebView(_spectrumTuningSettings);
    }

    private void PushStyleToWebView(AppSettings settings)
    {
        if (!_isWebViewReady || !_isWebDocumentReady || _isShowingWebErrorPage)
        {
            return;
        }

        var stylePayload = new
        {
            fontFamily = string.IsNullOrWhiteSpace(settings.FontFamily)
                ? AppSettings.DefaultFontFamily
                : settings.FontFamily,
            fontSize = Math.Clamp(settings.FontSize, 10, 40),
            fontWeight = settings.FontWeight,
            primaryColor = ToCssColor(_primaryTextColor),
            secondaryColor = ToCssColor(_secondaryTextColor),
            surfaceColor = settings.ShowBackground
                ? $"rgba(18, 18, 24, {Math.Clamp(settings.BackgroundOpacity, 0, 1).ToString("0.####", CultureInfo.InvariantCulture)})"
                : "transparent",
            surfaceShadow = settings.ShowBorder
                ? "inset 0 0 0 1px rgba(255, 255, 255, 0.16)"
                : "none",
            textShadow = settings.ShowTextShadow
                ? "0 1px 2px rgba(0, 0, 0, 0.36)"
                : "none"
        };

        var payloadJson = JsonSerializer.Serialize(stylePayload);
        var script = $"window.taskbarLyrics?.applyStyle({payloadJson});";
        _ = ExecuteWebScriptAsync(script);
    }

    private object EnsureWebViewControlCreated()
    {
        if (_lyricsWebViewControl is not null && _lyricsWebViewElement is not null)
        {
            return _lyricsWebViewControl;
        }

        object control = new WebView2();

        if (control is not FrameworkElement element || control is not UIElement uiElement)
        {
            throw new InvalidOperationException("WebView control is not a WPF element.");
        }

        element.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        element.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
        element.Focusable = false;
        element.IsHitTestVisible = false;

        LyricsWebHost.Children.Clear();
        LyricsWebHost.Children.Add(uiElement);

        _lyricsWebViewControl = control;
        _lyricsWebViewElement = element;
        return control;
    }

    private static async Task EnsureCoreWebView2Async(object webViewControl, CoreWebView2Environment environment)
    {
        var ensureMethod = webViewControl.GetType().GetMethod(
            "EnsureCoreWebView2Async",
            new[] { typeof(CoreWebView2Environment) });
        if (ensureMethod is null)
        {
            throw new MissingMethodException(
                webViewControl.GetType().FullName,
                "EnsureCoreWebView2Async");
        }

        var ensureTask = ensureMethod.Invoke(webViewControl, new object?[] { environment }) as Task;
        if (ensureTask is null)
        {
            throw new InvalidOperationException("EnsureCoreWebView2Async did not return Task.");
        }

        await ensureTask.ConfigureAwait(true);
    }

    private static void TrySetDefaultBackgroundColor(object webViewControl, System.Drawing.Color color)
    {
        var property = webViewControl.GetType().GetProperty("DefaultBackgroundColor");
        if (property is null || !property.CanWrite || property.PropertyType != typeof(System.Drawing.Color))
        {
            return;
        }

        property.SetValue(webViewControl, color);
    }

    private static CoreWebView2? TryGetCoreWebView2(object webViewControl)
    {
        var property = webViewControl.GetType().GetProperty("CoreWebView2");
        return property?.GetValue(webViewControl) as CoreWebView2;
    }

    private void AttachWebViewNavigationHandler(object webViewControl)
    {
        DetachWebViewNavigationHandler();
        var eventInfo = webViewControl.GetType().GetEvent("NavigationCompleted");
        if (eventInfo?.EventHandlerType is null)
        {
            return;
        }

        var handlerMethod = GetType().GetMethod(
            nameof(OnLyricsWebViewNavigationCompleted),
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (handlerMethod is null)
        {
            return;
        }

        var handler = Delegate.CreateDelegate(
            eventInfo.EventHandlerType,
            this,
            handlerMethod,
            throwOnBindFailure: false);
        if (handler is null)
        {
            return;
        }

        eventInfo.AddEventHandler(webViewControl, handler);
        _lyricsNavigationCompletedEvent = eventInfo;
        _lyricsNavigationCompletedHandler = handler;
    }

    private void DetachWebViewNavigationHandler()
    {
        if (_lyricsWebViewControl is null ||
            _lyricsNavigationCompletedEvent is null ||
            _lyricsNavigationCompletedHandler is null)
        {
            return;
        }

        _lyricsNavigationCompletedEvent.RemoveEventHandler(_lyricsWebViewControl, _lyricsNavigationCompletedHandler);
        _lyricsNavigationCompletedEvent = null;
        _lyricsNavigationCompletedHandler = null;
    }

    private void NavigateWebViewToString(string html)
    {
        if (_lyricsWebViewControl is null)
        {
            return;
        }

        var method = _lyricsWebViewControl.GetType().GetMethod("NavigateToString", new[] { typeof(string) });
        method?.Invoke(_lyricsWebViewControl, new object?[] { html });
    }

    private Task? ExecuteWebScriptAsync(string script)
    {
        if (_lyricsWebViewControl is null)
        {
            return null;
        }

        var method = _lyricsWebViewControl.GetType().GetMethod("ExecuteScriptAsync", new[] { typeof(string) });
        return method?.Invoke(_lyricsWebViewControl, new object?[] { script }) as Task;
    }

    private static string ToCssColor(Media.Color color)
    {
        var alpha = Math.Round(color.A / 255.0, 4, MidpointRounding.AwayFromZero);
        return $"rgba({color.R}, {color.G}, {color.B}, {alpha.ToString(CultureInfo.InvariantCulture)})";
    }

    private static string GetLyricsWebUiHtml()
    {
        try
        {
            var lyricsWebDir = Path.Combine(AppContext.BaseDirectory, "Web", "Lyrics");
            var template = File.ReadAllText(Path.Combine(lyricsWebDir, "index.html"));
            var style = File.ReadAllText(Path.Combine(lyricsWebDir, "style.css"));
            var script = File.ReadAllText(Path.Combine(lyricsWebDir, "app.js"));

            return template
                .Replace("{{STYLE_CSS}}", style, StringComparison.Ordinal)
                .Replace("{{APP_JS}}", script, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            return GetLyricsWebErrorHtml($"Failed to load lyrics web UI: {ex.Message}");
        }
    }

    private static string GetLyricsWebErrorHtml(string message)
    {
        var safeMessage = System.Net.WebUtility.HtmlEncode(message);
        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <style>
    html, body {
      width: 100%;
      height: 100%;
      margin: 0;
      padding: 0;
      background: #121212;
      color: #f8f8f8;
      font-family: "Segoe UI", "Microsoft YaHei UI", sans-serif;
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .error {
      padding: 8px 10px;
      font-size: 12px;
      line-height: 1.25;
      border-radius: 6px;
      border: 1px solid rgba(255, 255, 255, 0.2);
      background: rgba(255, 255, 255, 0.06);
      max-width: 100%;
      white-space: pre-wrap;
      word-break: break-word;
    }
  </style>
</head>
<body>
  <div class="error">{{safeMessage}}</div>
</body>
</html>
""";
    }

    private void AnchorToTaskbar()
    {
        var workArea = SystemParameters.WorkArea;
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        const double normalTaskbarHeight = 48;
        var taskbarHeight = Math.Max(normalTaskbarHeight, screenHeight - workArea.Height);
        var desiredHeight = Math.Max(36, taskbarHeight - 4);
        Height = Math.Min(desiredHeight, taskbarHeight);

        var settings = (System.Windows.Application.Current as App)?.Settings ?? new AppSettings();
        Left = settings.HorizontalAnchor switch
        {
            LyricsHorizontalAnchor.Left => Math.Max(0, settings.XOffset),
            LyricsHorizontalAnchor.Center => ((screenWidth - Width) / 2.0) + settings.XOffset,
            _ => Math.Max(0, screenWidth - Width - 230 + settings.XOffset)
        };

        Top = screenHeight - taskbarHeight + ((taskbarHeight - Height) / 2.0) + settings.YOffset;
    }

    private void AttachToTaskbarHost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
        return;
        }
        var settings = (System.Windows.Application.Current as App)?.Settings;
        var forceTopmost = settings?.ForceAlwaysOnTop == true;
        Topmost = forceTopmost;
        var hWndInsertAfter = forceTopmost
        ? NativeMethods.HWND_TOPMOST
        : NativeMethods.HWND_TOP;
        NativeMethods.SetWindowPos(
            hwnd,
            hWndInsertAfter,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE |
            NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_ASYNCWINDOWPOS |
            NativeMethods.SWP_SHOWWINDOW);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == _taskbarCreatedMessage)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                AnchorToTaskbar();
                AttachToTaskbarHost();
            }));
        }
        else if (msg == WmShowWindow)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                EnsureVisibleIfExpected();
                AnchorToTaskbar();
                AttachToTaskbarHost();
            }));
        }

        return IntPtr.Zero;
    }

    private void EnsureVisibleIfExpected()
    {
        if (System.Windows.Application.Current is not App app)
        {
            return;
        }

        if (app.IsExiting || !app.UserWantsLyricsVisible)
        {
            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        AnchorToTaskbar();
        AttachToTaskbarHost();
    }

}

internal static class NativeMethods
{
    internal static readonly IntPtr HWND_TOP = IntPtr.Zero;
    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_ASYNCWINDOWPOS = 0x4000;
    internal const uint SWP_NOSENDCHANGING = 0x0400;
    internal const uint SWP_NOOWNERZORDER = 0x0200;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const int SW_SHOWNOACTIVATE = 4;
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
