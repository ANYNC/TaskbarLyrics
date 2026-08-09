using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
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

public partial class MainWindow : Window, IDisposable
{
    private readonly IMusicSessionProvider _musicSessionProvider;
    private readonly IMediaPlaybackController _mediaPlaybackController;
    private readonly IPlayerRecognitionController _playerRecognitionController;
    private readonly IAppCompositionRoot _compositionRoot;
    private readonly TrackLyricOffsetStore _trackLyricOffsetStore;
    private readonly SystemAudioSpectrumService _audioSpectrumService = new();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _spectrumTimer;
    private readonly TaskbarPlacementService _taskbarPlacementService = new();
    private LocalMediaCoverProvider? _localMediaCoverProvider;
    private Media.Color _primaryTextColor = Media.Colors.White;
    private Media.Color _secondaryTextColor = ForegroundColorPolicy.CreateSecondaryColor(Media.Colors.White);
    private Media.Color _translationTextColor = ForegroundColorPolicy.CreateTranslationColor(Media.Colors.White);
    private LyricSyncService _lyricSyncService;
    private string _currentLine = "TaskbarLyrics 已启动";
    private string _nextLine = "等待歌词...";
    private string _currentTranslation = string.Empty;
    private string _nextTranslation = string.Empty;
    private bool _hasTrackTranslation;
    private double _lastLineProgress;
    private double? _lastWordScanProgress;
    private string? _lastCoverTrackId;
    private string? _currentCoverVisualTrackId;
    private string? _currentCoverDataUri;
    private string _currentCoverFallbackText = "N";
    private string _currentCoverFallbackColorCss = "rgba(67, 160, 71, 1)";
    private string? _lastLocalCoverLookupTrackId;
    private DateTimeOffset _nextLocalCoverLookupUtc;
    private SpectrumDisplayMode _spectrumDisplayMode = SpectrumDisplayMode.Disabled;
    private float[] _spectrumSilence = new float[SpectrumTuningSettings.DefaultBarCount];
    private bool _spectrumPreviewEnabled;
    private bool _isSpectrumCaptureRequested;
    private SmtcTimelineMonitorWindow? _smtcTimelineMonitorWindow;
    private bool _isWebViewReady;
    private bool _isWebViewInitializing;
    private bool _isWebDocumentReady;
    private bool _isShowingWebErrorPage;
    private bool _isTimerTickRunning;
    private bool _isSpectrumScriptPending;
    private bool _isCurrentFramePureMusic;
    private bool _isCurrentPlaybackPlaying;
    private bool _forceAlwaysOnTop = true;
    private string? _pendingSpectrumValuesJson;
    private SpectrumTuningSettings _spectrumTuningSettings = SpectrumTuningSettings.CreateDefault();
    private int _lastWebCurrentLineIndex = -1;
    private string _lastWebTrackId = string.Empty;
    private FrameworkElement? _lyricsWebViewElement;
    private object? _lyricsWebViewControl;
    private CoreWebView2? _lyricsCoreWebView2;
    private CoreWebView2Controller? _lyricsWebViewController;
    private EventInfo? _lyricsNavigationCompletedEvent;
    private Delegate? _lyricsNavigationCompletedHandler;
    private string _lastCoverVisualDiagnosticsKey = string.Empty;
    private string? _lastDiagnosticsTrackId;
    private bool? _lastDiagnosticsIsPlaying;
    private string? _lastDiagnosticsLyricSource;
    private DateTimeOffset _nextSpectrumDiagnosticsLogUtc;
    private string _lastSpectrumDiagnosticsKey = string.Empty;
    private AppSettings _currentSettings = new();
    private TrackInfo? _currentTrack;
    private bool _hasAppliedSettings;
    private bool _hasReportedWebViewControllerMonitoringFailure;
    private int _displayLayoutRefreshPending;
    private int _isDisposed;

    internal MainWindow(TrackLyricOffsetStore trackLyricOffsetStore, IAppCompositionRoot compositionRoot)
    {
        InitializeComponent();

        _trackLyricOffsetStore = trackLyricOffsetStore;
        _compositionRoot = compositionRoot;
        var musicServices = _compositionRoot.CreateMusicSessionServices();
        _musicSessionProvider = musicServices.SessionProvider;
        _mediaPlaybackController = musicServices.PlaybackController;
        _playerRecognitionController = musicServices.PlayerRecognitionController;
        _lyricSyncService = _compositionRoot.CreateLyricSyncService(new AppSettings(), _trackLyricOffsetStore);

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
        var snapshot = settings.Clone();
        var changes = AppSettingsChangeSet.Create(_currentSettings, snapshot, !_hasAppliedSettings);
        _currentSettings = snapshot;
        _hasAppliedSettings = true;

        if (changes.PlayerRecognitionChanged)
        {
            _playerRecognitionController.SetRecognitionOrder(
                snapshot.SourceRecognitionOrder,
                _compositionRoot.GetEnabledPlayerSources(snapshot));
        }

        if (changes.WindowLayoutChanged || changes.LyricsLayoutChanged)
        {
            Width = AppSettings.ClampEffectiveWindowWidth(
                snapshot.WindowWidth,
                snapshot.LyricsLayoutScalePercent,
                SystemParameters.WorkArea.Width);
        }

        if (changes.WindowLayoutChanged)
        {
            _forceAlwaysOnTop = snapshot.ForceAlwaysOnTop;
        }

        if (changes.LyricsLayoutChanged)
        {
            ApplyHostLayout(CreateLayoutMetrics(snapshot));
        }

        if (changes.LocalMediaLibraryChanged)
        {
            ReconfigureLocalMedia(snapshot);
        }

        if (changes.VisualStyleChanged)
        {
            ApplyVisualStyle(snapshot);
        }

        if (changes.LyricSyncServiceChanged)
        {
            RebuildLyricSyncService(snapshot);
        }

        if (changes.SpectrumDisplayChanged)
        {
            _spectrumDisplayMode = snapshot.SpectrumDisplayMode;
            if (_spectrumDisplayMode == SpectrumDisplayMode.Disabled)
            {
                _isCurrentFramePureMusic = false;
            }

            UpdateSpectrumCaptureState();
        }

        if (changes.WindowLayoutChanged || changes.LyricsLayoutChanged)
        {
            AnchorToTaskbar();
            AttachToTaskbarHost();
        }

        if (changes.VisualStyleChanged)
        {
            PushStyleToWebView(snapshot);
        }

        PushCurrentLyricsToWebView();
    }

    private void ReconfigureLocalMedia(AppSettings settings)
    {
        var previousProvider = _localMediaCoverProvider;
        _localMediaCoverProvider = _compositionRoot.CreateLocalMediaCoverProvider(settings);
        previousProvider?.Dispose();
        _lastLocalCoverLookupTrackId = null;
        _nextLocalCoverLookupUtc = default;
    }

    private void ApplyVisualStyle(AppSettings settings)
    {
        try
        {
            var brush = (Media.Brush?)new Media.BrushConverter().ConvertFromString(settings.ForegroundColor);
            _primaryTextColor = brush is Media.SolidColorBrush solid
                ? solid.Color
                : Media.Colors.White;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException)
        {
            _primaryTextColor = Media.Colors.White;
        }

        _secondaryTextColor = ForegroundColorPolicy.CreateSecondaryColor(_primaryTextColor);
        _translationTextColor = ForegroundColorPolicy.CreateTranslationColor(_primaryTextColor);
        // Keep the WPF host transparent; the WebView draws the optional surface.
        RootBorder.Background = Media.Brushes.Transparent;
        RootBorder.BorderBrush = Media.Brushes.Transparent;
        RootBorder.BorderThickness = new Thickness(0);
    }

    private void ApplyHostLayout(LyricsLayoutMetrics metrics)
    {
        RootBorder.Padding = new Thickness(
            metrics.HostHorizontalPadding,
            metrics.HostVerticalPadding,
            metrics.HostHorizontalPadding,
            metrics.HostVerticalPadding);
        LyricsContentRoot.MinHeight = metrics.MinimumContentHeight;
        LyricsWebHost.Margin = new Thickness(0, 0, 0, -metrics.ViewportDescenderBuffer);
    }

    private void RebuildLyricSyncService(AppSettings settings)
    {
        var nextService = _compositionRoot.CreateLyricSyncService(settings, _trackLyricOffsetStore);
        var currentService = _lyricSyncService;
        _lyricSyncService = nextService;
        currentService.Dispose();
    }

    internal CurrentTrackLyricsContext? GetCurrentTrackLyricsContextSnapshot()
    {
        var lyricSource = _lyricSyncService.CurrentLyricSourceApp;
        return _currentTrack is null
            ? null
            : new CurrentTrackLyricsContext(_currentTrack, lyricSource ?? string.Empty);
    }

    internal Task ExecuteMediaHotkeyAsync(MediaHotkeyAction action, CancellationToken cancellationToken)
    {
        return _mediaPlaybackController.ExecuteAsync(action, cancellationToken);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TaskObserver.Observe(InitializeLyricsWindowAsync(), "lyrics window initialization");
    }

    private async Task InitializeLyricsWindowAsync()
    {
        AnchorToTaskbar();
        AttachToTaskbarHost();
        await EnsureLyricsWebViewReadyAsync();
        ApplySpectrumTuning(_spectrumTuningSettings);
        PushCurrentLyricsToWebView();
        _timer.Start();
        UpdateSpectrumCaptureState();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
            TaskbarPlacementService.ApplyToolWindowStyle(source.Handle);
            AttachToTaskbarHost();
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
            AnchorToTaskbar();
            AttachToTaskbarHost();
        }
        else
        {
            if (_timer.IsEnabled)
            {
                _timer.Stop();
            }
        }

        UpdateSpectrumCaptureState();
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
        Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

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

        DetachWebViewMessageHandler();
        DetachLyricsWebViewController();
        DetachWebViewNavigationHandler();

        _lyricSyncService.Dispose();
        _localMediaCoverProvider?.Dispose();
        _audioSpectrumService.Dispose();
        (_musicSessionProvider as IDisposable)?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void OpenSmtcTimelineMonitorWindow()
    {
        if (_musicSessionProvider is not SmtcMusicSessionProvider smtcProvider)
        {
            return;
        }

        if (_smtcTimelineMonitorWindow is { IsVisible: true })
        {
            if (_smtcTimelineMonitorWindow.WindowState == WindowState.Minimized)
            {
                _smtcTimelineMonitorWindow.WindowState = WindowState.Normal;
            }

            _smtcTimelineMonitorWindow.Activate();
            return;
        }

        Log.SetVerboseEnabled(true);
        var monitorWindow = new SmtcTimelineMonitorWindow(smtcProvider, _lyricSyncService);
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
        Log.SetVerboseEnabled(false);
    }

    private void OnSmtcTimelineMonitorClosed(object? sender, EventArgs e)
    {
        if (sender is SmtcTimelineMonitorWindow window)
        {
            window.Closed -= OnSmtcTimelineMonitorClosed;
        }

        _smtcTimelineMonitorWindow = null;
        Log.SetVerboseEnabled(false);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        QueueDisplayLayoutRefresh();
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
            _currentTrack = snapshot.Track;
            UpdateCover(snapshot);

            var frame = await _lyricSyncService.GetDisplayFrameAsync(snapshot);
            LogTickDiagnostics(snapshot, frame);

            if (_musicSessionProvider is SmtcMusicSessionProvider smtcProvider)
            {
                smtcProvider.SetCurrentLyricSource(_lyricSyncService.CurrentLyricSourceApp);
                var playerOffset = _currentSettings.GetPlayerLyricOffsetMilliseconds(snapshot.Track?.SourceApp);
                var trackOffset = _trackLyricOffsetStore.GetOffsetMilliseconds(
                    snapshot.Track,
                    _lyricSyncService.CurrentLyricSourceApp);
                smtcProvider.SetCurrentLyricOffsets(playerOffset, trackOffset);
            }

            var current = string.IsNullOrWhiteSpace(frame.CurrentLine)
                ? "等待播放..."
                : frame.CurrentLine;

            var next = frame.NextLine;
            _lastWebCurrentLineIndex = frame.CurrentLineIndex;
            _lastWebTrackId = snapshot.Track is null
                ? string.Empty
                : LyricSyncService.BuildStableTrackIdentity(snapshot.Track);

            _isCurrentFramePureMusic = ShouldShowSpectrum(frame);
            _isCurrentPlaybackPlaying = snapshot.IsPlaying;
            UpdateSpectrumCaptureState();
            var wordScanProgress = _currentSettings.EnableWordScanning
                ? frame.WordScanProgress
                : null;
            UpdateLyricLines(
                current,
                next,
                frame.CurrentTranslation,
                frame.NextTranslation,
                frame.HasTrackTranslation,
                frame.LineProgress,
                wordScanProgress);
            PushCurrentLyricsToWebView();
        }
        catch (Exception ex)
        {
            Log.Error($"Lyrics timer tick failed: {ex}");
            _currentLine = $"歌词服务异常: {ex.Message}";
            _nextLine = string.Empty;
            _currentTranslation = string.Empty;
            _nextTranslation = string.Empty;
            _hasTrackTranslation = false;
            _lastLineProgress = 0;
            _lastWordScanProgress = null;
            _isCurrentFramePureMusic = false;
            _isCurrentPlaybackPlaying = false;
            UpdateSpectrumCaptureState();
            PushCurrentLyricsToWebView();
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
        snapshot.BarCount = Math.Clamp(
            snapshot.BarCount,
            SpectrumTuningSettings.MinBarCount,
            SpectrumTuningSettings.MaxBarCount);
        _spectrumTuningSettings = snapshot;
        if (_spectrumSilence.Length != snapshot.BarCount)
        {
            _spectrumSilence = new float[snapshot.BarCount];
        }
        _audioSpectrumService.ApplyTuning(snapshot);
        _spectrumTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(snapshot.UpdateIntervalMs, 16, 100));
        PushSpectrumTuningToWebView(snapshot);
    }

    public void SetSpectrumPreviewEnabled(bool enabled)
    {
        _spectrumPreviewEnabled = enabled;
        UpdateSpectrumCaptureState();
    }

    public void RetrySpectrumCapture()
    {
        if (!_isSpectrumCaptureRequested)
        {
            return;
        }

        _audioSpectrumService.Stop();
        PublishSpectrumDiagnostics(_spectrumSilence, _audioSpectrumService.GetDiagnostics());
        _audioSpectrumService.Start();
    }

    private bool ShouldShowSpectrum(LyricDisplayFrame frame)
    {
        return _spectrumDisplayMode switch
        {
            SpectrumDisplayMode.Disabled => false,
            SpectrumDisplayMode.Always => true,
            SpectrumDisplayMode.PureMusicOrNoLyrics => frame.IsPureMusic || IsLyricsNotFoundFrame(frame),
            _ => frame.IsPureMusic
        };
    }

    private static bool IsLyricsNotFoundFrame(LyricDisplayFrame frame)
    {
        return frame.CurrentLineIndex < 0 &&
            string.Equals(frame.CurrentLine, LyricSyncService.NoLyricsText, StringComparison.Ordinal);
    }

    private void UpdateSpectrumCaptureState()
    {
        var shouldCapture = SpectrumCapturePolicy.ShouldCapture(
            _currentSettings.SpectrumAudioAccessGranted,
            _spectrumPreviewEnabled,
            IsVisible,
            _isCurrentFramePureMusic);
        if (shouldCapture == _isSpectrumCaptureRequested)
        {
            return;
        }

        _isSpectrumCaptureRequested = shouldCapture;
        if (shouldCapture)
        {
            _audioSpectrumService.Start();
            if (!_spectrumTimer.IsEnabled)
            {
                _spectrumTimer.Start();
            }

            return;
        }

        if (_spectrumTimer.IsEnabled)
        {
            _spectrumTimer.Stop();
        }

        _audioSpectrumService.Stop();
        PublishSpectrumDiagnostics(_spectrumSilence, _audioSpectrumService.GetDiagnostics());
    }

    private void OnSpectrumTimerTick(object? sender, EventArgs e)
    {
        var shouldRenderLyricsSpectrum = _isCurrentFramePureMusic;
        if (!shouldRenderLyricsSpectrum && !_spectrumPreviewEnabled)
        {
            PublishSpectrumDiagnostics(_spectrumSilence, _audioSpectrumService.GetDiagnostics());
            return;
        }

        var captureDiagnostics = _audioSpectrumService.GetDiagnostics();
        var bars = captureDiagnostics.IsAvailable && (_isCurrentPlaybackPlaying || _spectrumPreviewEnabled)
            ? _audioSpectrumService.GetSpectrum()
            : _spectrumSilence;

        PublishSpectrumDiagnostics(bars, captureDiagnostics);
        if (shouldRenderLyricsSpectrum)
        {
            PushSpectrumToWebView(bars);
        }
    }

    private void PublishSpectrumDiagnostics(float[] bars, SpectrumCaptureDiagnostics capture)
    {
        var outputPeak = bars.Length == 0 ? 0f : bars.Max();
        var snapshot = new SpectrumDiagnosticsSnapshot(
            DateTimeOffset.UtcNow,
            _isCurrentFramePureMusic,
            _isCurrentPlaybackPlaying,
            capture.IsAvailable,
            capture.SampleRate,
            capture.Channels,
            capture.Format,
            capture.InputPeak,
            outputPeak,
            bars,
            capture.LastAudioUtc,
            capture.LastError);

        SpectrumDiagnosticsState.Update(snapshot);
        LogSpectrumDiagnostics(snapshot);
    }

    private void LogSpectrumDiagnostics(SpectrumDiagnosticsSnapshot snapshot)
    {
        if (!snapshot.IsPureMusicMode && string.IsNullOrWhiteSpace(snapshot.LastError))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var key = string.Join("|",
            snapshot.IsPureMusicMode,
            snapshot.IsPlaying,
            snapshot.IsCaptureAvailable,
            snapshot.SampleRate,
            snapshot.Channels,
            snapshot.Format,
            snapshot.LastError);

        var shouldLog = !string.Equals(key, _lastSpectrumDiagnosticsKey, StringComparison.Ordinal) ||
                        now >= _nextSpectrumDiagnosticsLogUtc;
        if (!shouldLog)
        {
            return;
        }

        _lastSpectrumDiagnosticsKey = key;
        _nextSpectrumDiagnosticsLogUtc = now.AddSeconds(5);
        Log.Diagnostic(
            "SPECTRUM",
            $"Spectrum: PureMusic={snapshot.IsPureMusicMode}, Playing={snapshot.IsPlaying}, CaptureAvailable={snapshot.IsCaptureAvailable}, " +
            $"InputPeak={snapshot.InputPeak:0.0000}, OutputPeak={snapshot.OutputPeak:0.0000}, Format='{snapshot.Format}', " +
            $"LastAudioUtc='{snapshot.LastAudioUtc:yyyy-MM-dd HH:mm:ss.fff}', Error='{snapshot.LastError}'");
    }

    private void LogTickDiagnostics(PlaybackSnapshot snapshot, LyricDisplayFrame frame)
    {
        if (_smtcTimelineMonitorWindow is not { IsVisible: true })
        {
            return;
        }

        var trackId = snapshot.Track?.Id;
        var lyricSource = _lyricSyncService.CurrentLyricSourceApp;
        var shouldLog =
            !string.Equals(trackId, _lastDiagnosticsTrackId, StringComparison.Ordinal) ||
            snapshot.IsPlaying != _lastDiagnosticsIsPlaying ||
            !string.Equals(lyricSource, _lastDiagnosticsLyricSource, StringComparison.Ordinal);
        if (!shouldLog)
        {
            return;
        }

        _lastDiagnosticsTrackId = trackId;
        _lastDiagnosticsIsPlaying = snapshot.IsPlaying;
        _lastDiagnosticsLyricSource = lyricSource;
        if (snapshot.Track is null)
        {
            Log.Diagnostic("SMTC", "No active track found (Track is null)");
            return;
        }

        Log.Diagnostic(
            "SMTC",
            $"Title='{snapshot.Track.Title}', Artist='{snapshot.Track.Artist}', App='{snapshot.Track.SourceApp}', Playing={snapshot.IsPlaying}, Pos={snapshot.Position}, CoverLen={snapshot.CoverImageBytes?.Length ?? 0}, LyricSource='{lyricSource}'");
    }

    private void UpdateLyricLines(
        string current,
        string next,
        string? currentTranslation,
        string? nextTranslation,
        bool hasTrackTranslation,
        double lineProgress,
        double? wordScanProgress)
    {
        _currentLine = current;
        _nextLine = next;
        _currentTranslation = currentTranslation ?? string.Empty;
        _nextTranslation = nextTranslation ?? string.Empty;
        _hasTrackTranslation = hasTrackTranslation;
        _lastLineProgress = lineProgress;
        _lastWordScanProgress = wordScanProgress;
    }

    private void UpdateCover(PlaybackSnapshot snapshot)
    {
        var trackId = snapshot.Track?.Id;
        var isSameRequestedTrack = string.Equals(trackId, _lastCoverTrackId, StringComparison.Ordinal);
        var isCurrentTrackVisual = string.Equals(trackId, _currentCoverVisualTrackId, StringComparison.Ordinal);
        if (isSameRequestedTrack && isCurrentTrackVisual)
        {
            // Proceed only if we previously had no cover for this track but now we have bytes.
            if (_currentCoverDataUri != null ||
                (snapshot.CoverImageBytes == null && _localMediaCoverProvider is null))
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
            LogCoverVisualState(trackId, "SMTC", bytes.Length, DetectImageMimeType(bytes));
            PushCoverToWebView();
            return;
        }

        if (snapshot.IsCoverLoading)
        {
            return;
        }

        var localCoverBytes = TryGetThrottledLocalCover(snapshot.Track, trackId);
        if (localCoverBytes is { Length: > 0 })
        {
            _currentCoverDataUri = BuildCoverDataUri(localCoverBytes);
            _currentCoverVisualTrackId = trackId;
            LogCoverVisualState(trackId, "Local", localCoverBytes.Length, DetectImageMimeType(localCoverBytes));
            PushCoverToWebView();
            return;
        }

        _currentCoverDataUri = null;
        _currentCoverVisualTrackId = trackId;
        LogCoverVisualState(trackId, "Fallback", 0, string.Empty);
        PushCoverToWebView();
    }

    private void LogCoverVisualState(string? trackId, string visualSource, int byteLength, string mime)
    {
        var diagnosticsKey = $"{trackId}|{visualSource}|{byteLength}|{mime}";
        if (string.Equals(diagnosticsKey, _lastCoverVisualDiagnosticsKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastCoverVisualDiagnosticsKey = diagnosticsKey;
        var track = _currentTrack;
        Log.Diagnostic(
            "COVER-UI",
            $"VisualChanged Track='{ToDiagnosticLogValue(trackId)}' Source='{ToDiagnosticLogValue(track?.SourceApp)}' " +
            $"Title='{ToDiagnosticLogValue(track?.Title)}' Visual='{visualSource}' Bytes={byteLength} " +
            $"Mime='{ToDiagnosticLogValue(mime)}'");
    }

    private static string ToDiagnosticLogValue(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return normalized.Length <= 240 ? normalized : normalized[..240];
    }

    private byte[]? TryGetThrottledLocalCover(TrackInfo? track, string? trackId)
    {
        if (_localMediaCoverProvider is null || track is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (string.Equals(trackId, _lastLocalCoverLookupTrackId, StringComparison.Ordinal) &&
            now < _nextLocalCoverLookupUtc)
        {
            return null;
        }

        _lastLocalCoverLookupTrackId = trackId;
        _nextLocalCoverLookupUtc = now.AddSeconds(5);
        var cover = _localMediaCoverProvider.TryGetCover(track);
        if (cover is { Length: > 0 })
        {
            _nextLocalCoverLookupUtc = DateTimeOffset.MaxValue;
        }

        return cover;
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
                AttachWebViewMessageHandler(coreWebView2);
                AttachLyricsWebViewController(webViewControl);
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

    private void PushCurrentLyricsToWebView()
    {
        if (!_isWebViewReady || !_isWebDocumentReady || _isShowingWebErrorPage)
        {
            return;
        }

        var script = LyricsWebViewScriptFactory.SetLyrics(
            _currentLine,
            _nextLine,
            _lastLineProgress,
            _lastWebCurrentLineIndex,
            _lastWebTrackId,
            _isCurrentFramePureMusic,
            _isCurrentPlaybackPlaying,
            _lastWordScanProgress,
            _currentTranslation,
            _nextTranslation,
            _currentSettings.ShowLyricTranslation && _hasTrackTranslation);
        TaskObserver.Observe(ExecuteWebScriptAsync(script), "lyrics web view update");
    }

    private void PushCoverToWebView()
    {
        if (!_isWebViewReady || !_isWebDocumentReady || _isShowingWebErrorPage)
        {
            return;
        }

        var script = LyricsWebViewScriptFactory.SetCover(
            _currentCoverDataUri,
            _currentCoverFallbackText,
            _currentCoverFallbackColorCss,
            _currentCoverVisualTrackId);
        TaskObserver.Observe(ExecuteWebScriptAsync(script), "lyrics cover update");
    }

    private void PushSpectrumToWebView(IReadOnlyList<float> bars)
    {
        if (!_isWebViewReady || !_isWebDocumentReady || _isShowingWebErrorPage)
        {
            return;
        }

        var script = LyricsWebViewScriptFactory.SetSpectrum(bars);
        if (_isSpectrumScriptPending)
        {
            _pendingSpectrumValuesJson = script;
            return;
        }

        SendSpectrumToWebView(script);
    }

    private void PushSpectrumTuningToWebView(SpectrumTuningSettings settings)
    {
        if (!_isWebViewReady || !_isWebDocumentReady || _isShowingWebErrorPage)
        {
            return;
        }

        var script = LyricsWebViewScriptFactory.SetSpectrumTuning(settings);
        TaskObserver.Observe(ExecuteWebScriptAsync(script), "lyrics spectrum tuning update");
    }

    private void SendSpectrumToWebView(string script)
    {
        var task = ExecuteWebScriptAsync(script);
        if (task is null)
        {
            return;
        }

        _isSpectrumScriptPending = true;
        TaskObserver.Observe(CompleteSpectrumScriptAsync(task), "lyrics spectrum update");
    }

    private async Task CompleteSpectrumScriptAsync(Task scriptTask)
    {
        try
        {
            await scriptTask.ConfigureAwait(false);
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
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
            });
        }
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
            PushStyleToWebView(_currentSettings);
        }

        PushCurrentLyricsToWebView();
        PushCoverToWebView();
        PushSpectrumTuningToWebView(_spectrumTuningSettings);
    }

    private void AttachWebViewMessageHandler(CoreWebView2 coreWebView2)
    {
        DetachWebViewMessageHandler();
        _lyricsCoreWebView2 = coreWebView2;
        coreWebView2.WebMessageReceived += OnLyricsWebViewMessageReceived;
    }

    private void DetachWebViewMessageHandler()
    {
        if (_lyricsCoreWebView2 is null)
        {
            return;
        }

        _lyricsCoreWebView2.WebMessageReceived -= OnLyricsWebViewMessageReceived;
        _lyricsCoreWebView2 = null;
    }

    private void AttachLyricsWebViewController(object webViewControl)
    {
        DetachLyricsWebViewController();

        if (!TryGetLyricsWebViewController(webViewControl, out var controller, out var failureReason))
        {
            ReportWebViewControllerMonitoringFailure(webViewControl, failureReason);
            return;
        }

        try
        {
            controller.ShouldDetectMonitorScaleChanges = true;
            controller.RasterizationScaleChanged += OnLyricsWebViewRasterizationScaleChanged;
            _lyricsWebViewController = controller;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            ReportWebViewControllerMonitoringFailure(webViewControl, ex.GetType().Name);
        }
    }

    private static bool TryGetLyricsWebViewController(
        object webViewControl,
        out CoreWebView2Controller controller,
        out string failureReason)
    {
        controller = null!;
        failureReason = string.Empty;
        try
        {
            var webViewBaseField = webViewControl.GetType().GetField(
                "m_webview2Base",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var webViewBase = webViewBaseField?.GetValue(webViewControl);
            var controllerProperty = webViewBase?.GetType().GetProperty(
                "CoreWebView2Controller",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (controllerProperty?.GetValue(webViewBase) is CoreWebView2Controller resolvedController)
            {
                controller = resolvedController;
                return true;
            }

            failureReason = "ControllerNotExposedByWpfHost";
            return false;
        }
        catch (Exception ex) when (ex is TargetInvocationException or MemberAccessException or InvalidOperationException)
        {
            failureReason = ex.GetType().Name;
            return false;
        }
    }

    private void ReportWebViewControllerMonitoringFailure(object webViewControl, string failureReason)
    {
        if (_hasReportedWebViewControllerMonitoringFailure)
        {
            return;
        }

        _hasReportedWebViewControllerMonitoringFailure = true;
        var assemblyVersion = webViewControl.GetType().Assembly.GetName().Version?.ToString() ?? "Unknown";
        Log.Diagnostic(
            "DPI-WEBVIEW",
            $"ControllerMonitoringUnavailable Reason='{failureReason}' WebViewAssemblyVersion='{assemblyVersion}' " +
            "Fallback='WpfDpiChangedAndDisplaySettingsRefresh'");
    }

    private void DetachLyricsWebViewController()
    {
        if (_lyricsWebViewController is null)
        {
            return;
        }

        _lyricsWebViewController.RasterizationScaleChanged -= OnLyricsWebViewRasterizationScaleChanged;
        _lyricsWebViewController = null;
    }

    private void OnLyricsWebViewRasterizationScaleChanged(object? sender, object e)
    {
        QueueDisplayLayoutRefresh();
    }

    private void OnLyricsWebViewMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = LyricsWebMessageRouter.Parse(e.TryGetWebMessageAsString());
            if (message?.Payload is not { ValueKind: JsonValueKind.Object } payload ||
                !string.Equals(message.Type, "coverDecodeError", StringComparison.Ordinal))
            {
                return;
            }

            var messageTrackId = payload.TryGetProperty("trackId", out var trackElement)
                ? trackElement.GetString() ?? string.Empty
                : string.Empty;
            var mime = payload.TryGetProperty("mime", out var mimeElement)
                ? mimeElement.GetString() ?? string.Empty
                : string.Empty;
            var uriLength = payload.TryGetProperty("uriLength", out var lengthElement) && lengthElement.TryGetInt32(out var length)
                ? length
                : 0;
            var generation = payload.TryGetProperty("generation", out var generationElement) && generationElement.TryGetInt32(out var value)
                ? value
                : 0;
            var activeTrack = _currentTrack;

            Log.Diagnostic(
                "COVER-WEB",
                $"DecodeFailed MessageTrack='{ToDiagnosticLogValue(messageTrackId)}' " +
                $"ActiveTrack='{ToDiagnosticLogValue(activeTrack?.Id)}' Source='{ToDiagnosticLogValue(activeTrack?.SourceApp)}' " +
                $"Title='{ToDiagnosticLogValue(activeTrack?.Title)}' Mime='{ToDiagnosticLogValue(mime)}' " +
                $"DataUriLength={uriLength} Generation={generation}");
        }
        catch (Exception ex)
        {
            Log.Diagnostic(
                "COVER-WEB",
                $"MessageParseFailed Exception='{ex.GetType().Name}' HResult=0x{ex.HResult:X8} " +
                $"Message='{ToDiagnosticLogValue(ex.Message)}'");
        }
    }

    private void PushStyleToWebView(AppSettings settings)
    {
        if (!_isWebViewReady || !_isWebDocumentReady || _isShowingWebErrorPage)
        {
            return;
        }

        var metrics = CreateLayoutMetrics(settings);
        var stylePayload = new
        {
            fontFamily = AppSettings.NormalizeFontFamily(settings.FontFamily),
            layoutScalePercent = metrics.ScalePercent,
            fontSize = metrics.FontSize,
            showCover = settings.ShowCover,
            coverSize = metrics.CoverSize,
            coverGap = metrics.CoverGap,
            coverCornerRadius = metrics.CoverCornerRadius,
            viewportDescenderBuffer = metrics.ViewportDescenderBuffer,
            layoutHorizontalPadding = metrics.LayoutHorizontalPadding,
            lyricsPaneTopPadding = metrics.LyricsPaneTopPadding,
            lyricsPaneRightPadding = metrics.LyricsPaneRightPadding,
            lyricsPaneLeftPadding = metrics.LyricsPaneLeftPadding,
            primaryOffsetY = metrics.PrimaryOffsetY,
            secondaryOffsetY = metrics.SecondaryOffsetY,
            lineTextBottomPadding = metrics.LineTextBottomPadding,
            surfaceRadius = metrics.SurfaceRadius,
            layerTransitionOffset = metrics.LayerTransitionOffset,
            coverFallbackFontSize = metrics.CoverFallbackFontSize,
            spectrumWidth = metrics.SpectrumWidth,
            spectrumHeight = metrics.SpectrumHeight,
            spectrumGap = metrics.SpectrumGap,
            spectrumBarWidth = metrics.SpectrumBarWidth,
            spectrumBarHeight = metrics.SpectrumBarHeight,
            spectrumLowHeight = metrics.SpectrumLowHeight,
            spectrumHighHeight = metrics.SpectrumHighHeight,
            spectrumMiddleHeight = metrics.SpectrumMiddleHeight,
            fontWeight = settings.FontWeight,
            primaryColor = ToCssColor(_primaryTextColor),
            secondaryColor = ToCssColor(_secondaryTextColor),
            translationColor = ToCssColor(_translationTextColor),
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

        var script = WebViewMessageScriptFactory.Dispatch("taskbarLyrics", "style", stylePayload);
        TaskObserver.Observe(ExecuteWebScriptAsync(script), "lyrics style update");
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
        try
        {
            if (_lyricsWebViewControl is null)
            {
                return null;
            }

            var method = _lyricsWebViewControl.GetType().GetMethod("ExecuteScriptAsync", new[] { typeof(string) });
            return method?.Invoke(_lyricsWebViewControl, new object?[] { script }) as Task;
        }
        catch (Exception exception)
        {
            Log.Error($"Lyrics web script dispatch failed: {exception}");
            return null;
        }
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
            var script = string.Join(Environment.NewLine, [
                File.ReadAllText(Path.Combine(lyricsWebDir, "bridge.js")),
                File.ReadAllText(Path.Combine(lyricsWebDir, "state.js")),
                File.ReadAllText(Path.Combine(lyricsWebDir, "app.js"))
            ]);

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
        TaskbarPlacementService.Anchor(this, _currentSettings);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        QueueDisplayLayoutRefresh();
    }

    private void QueueDisplayLayoutRefresh()
    {
        if (Volatile.Read(ref _isDisposed) != 0 || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _displayLayoutRefreshPending, 1, 0) != 0)
        {
            return;
        }

        // PMv2 must finish notifying the child HWND tree and WPF must finish its render layout first.
        Dispatcher.BeginInvoke(new Action(ApplyDisplayLayoutRefresh), DispatcherPriority.ContextIdle);
    }

    private void ApplyDisplayLayoutRefresh()
    {
        Interlocked.Exchange(ref _displayLayoutRefreshPending, 0);
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return;
        }

        ApplyHostLayout(CreateLayoutMetrics(_currentSettings));
        AnchorToTaskbar();
        RefreshLyricsWebViewLayout();
        AttachToTaskbarHost();
        PushStyleToWebView(_currentSettings);
    }

    private void RefreshLyricsWebViewLayout()
    {
        if (_lyricsWebViewControl is null || _lyricsWebViewElement is null)
        {
            return;
        }

        _lyricsWebViewElement.InvalidateArrange();
        UpdateLayout();
    }

    private LyricsLayoutMetrics CreateLayoutMetrics(AppSettings settings)
    {
        return LyricsLayoutMetrics.Create(settings, TaskbarPlacementService.GetPixelsPerDip(this));
    }

    private void AttachToTaskbarHost()
    {
        TaskbarPlacementService.Attach(this, _forceAlwaysOnTop);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarPlacementService.RequiresReattach(msg))
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (TaskbarPlacementService.IsShowWindowMessage(msg))
                {
                    EnsureVisibleIfExpected();
                }

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
