using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Media = System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Light.App;

public partial class MainWindow : Window
{
    private const int WmShowWindow = 0x0018;
    private const int FrameTimerMs = 16;
    private const int LyricsTickInterval = 4;

    private readonly IMusicSessionProvider _musicSessionProvider;
    private readonly SystemAudioSpectrumService _audioSpectrumService = new();
    private readonly DispatcherTimer _frameTimer;
    private readonly uint _taskbarCreatedMessage;
    private readonly DeferredLyricSyncService _lyricSyncService = new();
    private LocalMediaCoverProvider? _localMediaCoverProvider;
    private IReadOnlyList<string> _localMediaCoverFolders = Array.Empty<string>();
    private AppSettings _settings = new();

    private Media.Color _primaryTextColor = Media.Colors.White;
    private Media.Color _secondaryTextColor = Media.Color.FromArgb(190, 255, 255, 255);
    private Media.Color? _coverAccentColor;
    private string _currentLine = "TaskbarLyrics 已启动";
    private string _nextLine = "等待歌词...";
    private string? _currentTrackTitle;
    private string? _currentTrackArtist;
    private string? _lastCoverTrackId;
    private string? _currentCoverVisualTrackId;
    private byte[]? _lastDisplayedCoverBytes;
    private string? _lastRejectedCoverTrackId;
    private string _lastRejectedCoverSignature = string.Empty;
    private string? _lastLocalCoverLookupTrackId;
    private DateTimeOffset _nextLocalCoverLookupUtc;
    private CancellationTokenSource? _coverRefreshCts;
    private string? _coverRefreshTrackId;
    private bool _enableSmtcTimelineMonitor;
    private bool _enableSpectrum = true;
    private bool _enablePureMusicSpectrum = true;
    private bool _showSpectrumWhenLyricsNotFound;
    private bool _showSpectrumWhenLyricsAvailable;
    private LocalCoverSearchMode _localCoverSearchMode = LocalCoverSearchMode.OnlineFirst;
    private bool _showCoverImage = true;
    private bool _forceAlwaysOnTop = true;
    private SmtcTimelineMonitorWindow? _smtcTimelineMonitorWindow;
    private bool _isTimerTickRunning;
    private bool _isShowingSpectrum;
    private bool _isCurrentPlaybackPlaying;
    private SpectrumTuningSettings _spectrumTuningSettings = SpectrumTuningSettings.CreateDefault();
    private int _lastCurrentLineIndex = -1;
    private string _lastTrackId = string.Empty;
    private string? _lastDiagnosticsTrackId;
    private bool? _lastDiagnosticsIsPlaying;
    private string? _lastDiagnosticsLyricSource;
    private DateTimeOffset _nextSpectrumDiagnosticsLogUtc;
    private string _lastSpectrumDiagnosticsKey = string.Empty;
    private int _frameTickCounter;
    private int _spectrumTickInterval = 2;

    public MainWindow()
    {
        InitializeComponent();

        _musicSessionProvider = new SmtcMusicSessionProvider();
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        _frameTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(FrameTimerMs)
        };
        _frameTimer.Tick += OnFrameTimerTick;

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => AnchorToTaskbar();
        LyricsDisplay.PreferredHeightChanged += (_, _) =>
        {
            if (System.Windows.Application.Current is App { Settings.AutoAdjustWindowHeight: true })
            {
                AnchorToTaskbar();
            }
        };
        LyricsDisplay.PreferredWidthChanged += (_, _) =>
        {
            if (System.Windows.Application.Current is App { Settings.AutoAdjustWindowWidth: true })
            {
                AnchorToTaskbar();
            }
        };
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
        _settings = settings.Clone();
        Log.SetVerboseEnabled(settings.EnableSmtcTimelineMonitor);

        if (_musicSessionProvider is SmtcMusicSessionProvider smtcProvider)
        {
            smtcProvider.SetRecognitionOrder(
                settings.SourceRecognitionOrder,
                BuildEnabledPlayerSources(settings));
        }

        try
        {
            var brush = (Media.Brush?)new Media.BrushConverter().ConvertFromString(settings.ForegroundColor);
            _primaryTextColor = brush is Media.SolidColorBrush solid ? solid.Color : Media.Colors.White;
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

        _lyricSyncService.UpdateSettings(settings);
        ApplyLocalCoverSettings(settings);
        _forceAlwaysOnTop = settings.ForceAlwaysOnTop;
        _enableSpectrum = settings.EnableSpectrum;
        _enablePureMusicSpectrum = settings.EnablePureMusicSpectrum;
        _showSpectrumWhenLyricsNotFound = settings.ShowSpectrumWhenLyricsNotFound;
        _showSpectrumWhenLyricsAvailable = settings.ShowSpectrumWhenLyricsAvailable;
        if (_localCoverSearchMode != settings.LocalCoverSearchMode ||
            _showCoverImage != settings.ShowCoverImage)
        {
            InvalidateCoverDisplayState();
        }

        _localCoverSearchMode = settings.LocalCoverSearchMode;
        _showCoverImage = settings.ShowCoverImage;
        ApplyCurrentVisualStyle();
        AnchorToTaskbar();
        AttachToTaskbarHost();
        PushLyricsToDisplay(_currentLine, _nextLine, 0, _lastCurrentLineIndex, _lastTrackId, false, false);
        _enableSmtcTimelineMonitor = settings.EnableSmtcTimelineMonitor;

        if (IsLoaded)
        {
            UpdateSmtcTimelineMonitorWindow();
        }
    }

    private void ApplyCurrentVisualStyle()
    {
        var primary = _primaryTextColor;
        if (_settings.UseCoverAccentColor && _coverAccentColor is { } accent)
        {
            primary = CreateReadableAccentColor(accent);
        }

        var secondary = Media.Color.FromArgb(
            (byte)Math.Clamp((int)(primary.A * 0.76), 0, 255),
            primary.R,
            primary.G,
            primary.B);

        LyricsDisplay.ApplyStyle(_settings, primary, secondary, _coverAccentColor);
        LyricsDisplay.SetTrackInfo(_currentTrackTitle, _currentTrackArtist);
    }

    private static Media.Color CreateReadableAccentColor(Media.Color accent)
    {
        var luminance = ((0.2126 * accent.R) + (0.7152 * accent.G) + (0.0722 * accent.B)) / 255.0;
        if (luminance < 0.38)
        {
            return Mix(accent, Media.Colors.White, 0.58);
        }

        if (luminance > 0.78)
        {
            return Mix(accent, Media.Color.FromRgb(20, 24, 32), 0.42);
        }

        return Mix(accent, Media.Colors.White, 0.18);
    }

    private static Media.Color Mix(Media.Color a, Media.Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        var inverse = 1 - amount;
        return Media.Color.FromArgb(
            255,
            (byte)Math.Clamp(Math.Round((a.R * inverse) + (b.R * amount)), 0, 255),
            (byte)Math.Clamp(Math.Round((a.G * inverse) + (b.G * amount)), 0, 255),
            (byte)Math.Clamp(Math.Round((a.B * inverse) + (b.B * amount)), 0, 255));
    }

    private void ApplyLocalCoverSettings(AppSettings settings)
    {
        var canUseLocalCover = settings.ShowCoverImage &&
            settings.LocalCoverSearchMode != LocalCoverSearchMode.OnlineOnly;
        var requestedFolders = settings.EnableLocalLyrics && canUseLocalCover
            ? settings.LocalMusicFolders
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim().Trim('"'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();

        if (requestedFolders.SequenceEqual(_localMediaCoverFolders, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _localMediaCoverFolders = requestedFolders;
        _localMediaCoverProvider = requestedFolders.Length > 0
            ? new LocalMediaCoverProvider(requestedFolders)
            : null;
        InvalidateCoverDisplayState();
        _lastLocalCoverLookupTrackId = null;
        _nextLocalCoverLookupUtc = default;
    }

    public void ApplySpectrumTuning(SpectrumTuningSettings settings)
    {
        var snapshot = settings.Clone();
        _spectrumTuningSettings = snapshot;
        _audioSpectrumService.ApplyTuning(snapshot);
        _spectrumTickInterval = Math.Max(1, (int)Math.Round(
            Math.Clamp(snapshot.UpdateIntervalMs, 16, 100) / (double)FrameTimerMs,
            MidpointRounding.AwayFromZero));
        LyricsDisplay.SetSpectrumTuning(snapshot);
    }

    public void RematchCurrentLyrics()
    {
        _lyricSyncService.Reset();
        _lastCurrentLineIndex = -1;
        _ = OnLyricsTickAsync();
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AnchorToTaskbar();
        AttachToTaskbarHost();
        ApplySpectrumTuning(_spectrumTuningSettings);
        PushLyricsToDisplay(_currentLine, _nextLine, 0, _lastCurrentLineIndex, _lastTrackId, false, false);
        UpdateSmtcTimelineMonitorWindow();
        _frameTimer.Start();
        _ = OnLyricsTickAsync();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
            ApplyToolWindowStyle(source.Handle);
            AttachToTaskbarHost();
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            InvalidateCoverDisplayState();
            if (!_frameTimer.IsEnabled) _frameTimer.Start();
            AnchorToTaskbar();
            AttachToTaskbarHost();
            _ = OnLyricsTickAsync();
        }
        else
        {
            if (_frameTimer.IsEnabled) _frameTimer.Stop();
            UpdateSpectrumCaptureState(false);
        }
    }

    private void InvalidateCoverDisplayState()
    {
        CancelCoverRefresh();
        _currentCoverVisualTrackId = null;
        _lastDisplayedCoverBytes = null;
        ClearRejectedCover();
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
        CancelCoverRefresh();
        CloseSmtcTimelineMonitorWindow();
        _frameTimer.Stop();
        _frameTimer.Tick -= OnFrameTimerTick;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        UpdateSpectrumCaptureState(false);
        _lyricSyncService.Dispose();
        _audioSpectrumService.Dispose();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            LyricsDisplay.NotifyScreenMetricsChanged();
            AnchorToTaskbar();
            AttachToTaskbarHost();
        });
    }

    private void OnFrameTimerTick(object? sender, EventArgs e)
    {
        _frameTickCounter++;
        if (_frameTickCounter % LyricsTickInterval == 0)
        {
            _ = OnLyricsTickAsync();
        }

        if (_isShowingSpectrum &&
            _frameTickCounter % _spectrumTickInterval == 0)
        {
            OnSpectrumTick();
        }
    }

    private async Task OnLyricsTickAsync()
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

            var lyricSnapshot = ApplyLyricOffset(snapshot, out var appliedOffsetMs);
            var frame = await _lyricSyncService.GetDisplayFrameAsync(lyricSnapshot);
            PublishLyricDiagnostics(lyricSnapshot, frame, appliedOffsetMs);
            LogTickDiagnostics(lyricSnapshot, frame);
            PushTrackInfoToDisplay(snapshot.Track);

            if (_musicSessionProvider is SmtcMusicSessionProvider smtcProvider)
            {
                smtcProvider.SetCurrentLyricSource(_lyricSyncService.CurrentLyricSourceApp);
            }

            var current = string.IsNullOrWhiteSpace(frame.CurrentLine) ? "等待播放..." : frame.CurrentLine;
            var next = frame.NextLine;
            _lastCurrentLineIndex = frame.CurrentLineIndex;
            _lastTrackId = ResolveDisplayTrackId(snapshot.Track);

            _currentLine = current;
            _nextLine = next;
            _isShowingSpectrum = ShouldShowSpectrum(frame);
            _isCurrentPlaybackPlaying = snapshot.IsPlaying;
            UpdateSpectrumCaptureState(_isShowingSpectrum);
            PushLyricsToDisplay(current, next, frame.LineProgress, frame.CurrentLineIndex, _lastTrackId, _isShowingSpectrum, snapshot.IsPlaying);
        }
        catch (Exception ex)
        {
            _currentLine = $"歌词服务异常: {ex.Message}";
            _nextLine = string.Empty;
            _isShowingSpectrum = false;
            _isCurrentPlaybackPlaying = false;
            UpdateSpectrumCaptureState(false);
            PushLyricsToDisplay(_currentLine, _nextLine, 0, _lastCurrentLineIndex, _lastTrackId, false, false);
            Log.Error($"歌词 tick 异常: {ex}");
            Debug.WriteLine(ex);
        }
        finally
        {
            _isTimerTickRunning = false;
        }
    }

    private void OnSpectrumTick()
    {
        if (!_isShowingSpectrum)
        {
            PublishSpectrumDiagnostics(SystemAudioSpectrumService.Silence, _audioSpectrumService.GetDiagnostics());
            return;
        }

        var captureDiagnostics = _audioSpectrumService.GetDiagnostics();
        var bars = _isCurrentPlaybackPlaying && captureDiagnostics.IsAvailable
            ? _audioSpectrumService.GetSpectrum()
            : SystemAudioSpectrumService.Silence;

        PublishSpectrumDiagnostics(bars, captureDiagnostics);
        LyricsDisplay.SetSpectrum(bars);
    }

    private bool ShouldShowSpectrum(LyricDisplayFrame frame)
    {
        if (!_enableSpectrum)
        {
            return false;
        }

        return (frame.IsPureMusic && _enablePureMusicSpectrum) ||
            (_showSpectrumWhenLyricsAvailable && IsLyricsAvailableFrame(frame)) ||
            (_showSpectrumWhenLyricsNotFound && IsLyricsNotFoundFrame(frame));
    }

    private static bool IsLyricsAvailableFrame(LyricDisplayFrame frame)
    {
        return frame.CurrentLineIndex >= 0 &&
            !frame.IsPureMusic &&
            !string.IsNullOrWhiteSpace(frame.CurrentLine);
    }

    private static bool IsLyricsNotFoundFrame(LyricDisplayFrame frame)
    {
        return frame.CurrentLineIndex < 0 &&
            string.Equals(frame.CurrentLine, LyricSyncService.NoLyricsText, StringComparison.Ordinal);
    }

    private PlaybackSnapshot ApplyLyricOffset(PlaybackSnapshot snapshot, out int appliedOffsetMs)
    {
        appliedOffsetMs = GetLyricOffsetMs(snapshot.Track);
        if (appliedOffsetMs == 0)
        {
            return snapshot;
        }

        var position = snapshot.Position + TimeSpan.FromMilliseconds(appliedOffsetMs);
        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        if (snapshot.Track?.Duration is { } duration && duration > TimeSpan.Zero && position > duration)
        {
            position = duration;
        }

        return snapshot with { Position = position };
    }

    private int GetLyricOffsetMs(TrackInfo? track)
    {
        var offset = _settings.LyricOffsetMs;
        var sourceApp = track?.SourceApp ?? string.Empty;
        if (sourceApp.Equals("QQMusic", StringComparison.OrdinalIgnoreCase))
        {
            offset += _settings.QqMusicLyricOffsetMs;
        }
        else if (sourceApp.Equals("Netease", StringComparison.OrdinalIgnoreCase))
        {
            offset += _settings.NeteaseLyricOffsetMs;
        }
        else if (sourceApp.Equals("Kugou", StringComparison.OrdinalIgnoreCase))
        {
            offset += _settings.KugouLyricOffsetMs;
        }
        else if (sourceApp.Equals("Spotify", StringComparison.OrdinalIgnoreCase))
        {
            offset += _settings.SpotifyLyricOffsetMs;
        }

        return offset;
    }

    private static void PublishLyricDiagnostics(
        PlaybackSnapshot snapshot,
        LyricDisplayFrame frame,
        int appliedOffsetMs)
    {
        var previous = LyricResolveDiagnosticsState.Current;
        LyricResolveDiagnosticsState.Update(previous with
        {
            AppliedOffsetMs = appliedOffsetMs,
            PlaybackPosition = snapshot.Position,
            CurrentLineIndex = frame.CurrentLineIndex,
            LineProgress = frame.LineProgress,
            TrackTitle = snapshot.Track?.Title ?? previous.TrackTitle,
            TrackArtist = snapshot.Track?.Artist ?? previous.TrackArtist,
            TrackSourceApp = snapshot.Track?.SourceApp ?? previous.TrackSourceApp
        });
    }

    private void PublishSpectrumDiagnostics(IReadOnlyList<float> bars, SpectrumCaptureDiagnostics capture)
    {
        var outputPeak = bars.Count == 0 ? 0f : bars.Max();
        var snapshot = new SpectrumDiagnosticsSnapshot(
            DateTimeOffset.UtcNow,
            _isShowingSpectrum,
            _isCurrentPlaybackPlaying,
            capture.IsAvailable,
            capture.SampleRate,
            capture.Channels,
            capture.Format,
            capture.InputPeak,
            outputPeak,
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
        Log.Debug(
            $"Spectrum: PureMusic={snapshot.IsPureMusicMode}, Playing={snapshot.IsPlaying}, CaptureAvailable={snapshot.IsCaptureAvailable}, " +
            $"InputPeak={snapshot.InputPeak:0.0000}, OutputPeak={snapshot.OutputPeak:0.0000}, Format='{snapshot.Format}', " +
            $"LastAudioUtc='{snapshot.LastAudioUtc:yyyy-MM-dd HH:mm:ss.fff}', Error='{snapshot.LastError}'");
    }

    private void UpdateSpectrumCaptureState(bool shouldCapture)
    {
        if (shouldCapture)
        {
            if (!_audioSpectrumService.IsStarted)
            {
                _audioSpectrumService.Start();
            }

            return;
        }

        if (_audioSpectrumService.IsStarted)
        {
            _audioSpectrumService.Stop();
        }
    }

    private void PushLyricsToDisplay(string current, string next, double progress, int lineIndex, string trackId, bool isPureMusic, bool isPlaying)
    {
        LyricsDisplay.SetLyrics(current, next, progress, lineIndex, trackId, isPureMusic, isPlaying);
    }

    private void PushTrackInfoToDisplay(TrackInfo? track)
    {
        _currentTrackTitle = track?.Title;
        _currentTrackArtist = track?.Artist;
        LyricsDisplay.SetTrackInfo(_currentTrackTitle, _currentTrackArtist);
    }

    private string ResolveDisplayTrackId(TrackInfo? track)
    {
        if (track is null)
        {
            return string.Empty;
        }

        var candidate = LyricSyncService.BuildStableTrackIdentity(track);
        var title = track.Title?.Trim() ?? string.Empty;
        var isWeakMetadata = title.Length == 0 ||
                             title.Equals("Unknown Title", StringComparison.OrdinalIgnoreCase);

        // 网易云等播放器在切歌间隙会短暂上报弱元数据，沿用上一首展示键避免滚动动画被反复重置。
        if (isWeakMetadata &&
            _lastTrackId.Length > 0 &&
            string.Equals(track.SourceApp, ExtractSourceAppFromTrackId(_lastTrackId), StringComparison.Ordinal))
        {
            return _lastTrackId;
        }

        return candidate;
    }

    private static string ExtractSourceAppFromTrackId(string trackId)
    {
        var separator = trackId.IndexOf('|');
        return separator > 0 ? trackId[..separator] : trackId;
    }

    private void UpdateCover(PlaybackSnapshot snapshot)
    {
        var trackId = snapshot.Track?.Id;
        var sourceApp = snapshot.Track?.SourceApp ?? string.Empty;
        var (fallbackText, fallbackColor) = GetCoverFallback(sourceApp);
        if (!_showCoverImage)
        {
            CancelCoverRefresh();
            _lastCoverTrackId = trackId;
            _lastDisplayedCoverBytes = null;
            _currentCoverVisualTrackId = trackId;
            ClearRejectedCover();
            UpdateCoverAccent(null);
            LyricsDisplay.SetCover(null, fallbackText, fallbackColor);
            return;
        }

        var isSameRequestedTrack = string.Equals(trackId, _lastCoverTrackId, StringComparison.Ordinal);
        var isCurrentTrackVisual = string.Equals(trackId, _currentCoverVisualTrackId, StringComparison.Ordinal);
        if (isSameRequestedTrack && isCurrentTrackVisual)
        {
            // 与原版一致：同一首歌且已展示过封面时，仅在没有封面但现在有新字节（或可走本地回退）时再刷新。
            if (_lastDisplayedCoverBytes is { Length: > 0 } ||
                (snapshot.CoverImageBytes == null && _localMediaCoverProvider is null && !snapshot.IsCoverLoading))
            {
                return;
            }
        }

        _lastCoverTrackId = trackId;

        if (_localCoverSearchMode is LocalCoverSearchMode.LocalFirst or LocalCoverSearchMode.LocalOnly)
        {
            var localCoverBytes = TryGetThrottledLocalCover(snapshot.Track, trackId);
            if (TryDisplayCoverBytes(trackId, localCoverBytes, fallbackText, fallbackColor))
            {
                return;
            }
        }

        if (_localCoverSearchMode != LocalCoverSearchMode.LocalOnly &&
            TryDisplayCoverBytes(trackId, snapshot.CoverImageBytes, fallbackText, fallbackColor))
        {
            return;
        }

        if (snapshot.IsCoverLoading && _localCoverSearchMode == LocalCoverSearchMode.OnlineFirst)
        {
            if (!isCurrentTrackVisual)
            {
                _lastDisplayedCoverBytes = null;
                _currentCoverVisualTrackId = trackId;
                UpdateCoverAccent(null);
                LyricsDisplay.SetCover(null, fallbackText, fallbackColor);
            }

            ScheduleCoverRefresh(trackId);
            return;
        }

        if (_localCoverSearchMode is LocalCoverSearchMode.OnlineFirst &&
            TryDisplayCoverBytes(trackId, TryGetThrottledLocalCover(snapshot.Track, trackId), fallbackText, fallbackColor))
        {
            return;
        }

        _lastDisplayedCoverBytes = null;
        _currentCoverVisualTrackId = trackId;
        var shouldRefreshMissingCover = trackId is not null && _localCoverSearchMode != LocalCoverSearchMode.LocalOnly;
        if (!shouldRefreshMissingCover)
        {
            CancelCoverRefresh();
        }

        UpdateCoverAccent(null);
        LyricsDisplay.SetCover(null, fallbackText, fallbackColor);
        if (shouldRefreshMissingCover)
        {
            ScheduleCoverRefresh(trackId);
        }
    }

    private bool TryDisplayCoverBytes(
        string? trackId,
        byte[]? bytes,
        string fallbackText,
        Media.Color fallbackColor)
    {
        if (bytes is not { Length: > 0 })
        {
            return false;
        }

        if (!IsRejectedCover(trackId, bytes) &&
            LyricsDisplay.SetCover(bytes, fallbackText, fallbackColor))
        {
            _currentCoverVisualTrackId = trackId;
            _lastDisplayedCoverBytes = bytes;
            CancelCoverRefresh();
            UpdateCoverAccent(bytes);
            ClearRejectedCover();
            return true;
        }

        RememberRejectedCover(trackId, bytes);
        return false;
    }

    private void ScheduleCoverRefresh(string? trackId)
    {
        if (string.Equals(_coverRefreshTrackId, trackId, StringComparison.Ordinal) &&
            _coverRefreshCts is { IsCancellationRequested: false })
        {
            return;
        }

        _coverRefreshCts?.Cancel();
        _coverRefreshCts?.Dispose();
        var cts = new CancellationTokenSource();
        _coverRefreshCts = cts;
        _coverRefreshTrackId = trackId;
        _ = RefreshCoverWhenReadyAsync(trackId, cts);
    }

    private void CancelCoverRefresh()
    {
        _coverRefreshCts?.Cancel();
        _coverRefreshCts?.Dispose();
        _coverRefreshCts = null;
        _coverRefreshTrackId = null;
    }

    private async Task RefreshCoverWhenReadyAsync(string? expectedTrackId, CancellationTokenSource owner)
    {
        var cancellationToken = owner.Token;
        try
        {
            var delays = new[] { 120, 180, 260, 360, 520, 760, 1000, 1500, 2200, 3200, 4500 };
            foreach (var delay in delays)
            {
                await Task.Delay(delay, cancellationToken);
                var snapshot = await _musicSessionProvider.GetCurrentAsync(cancellationToken);
                if (!string.Equals(snapshot.Track?.Id, expectedTrackId, StringComparison.Ordinal))
                {
                    return;
                }

                if (snapshot.CoverImageBytes is { Length: > 0 })
                {
                    var (fallbackText, fallbackColor) = GetCoverFallback(snapshot.Track?.SourceApp ?? string.Empty);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            TryDisplayCoverBytes(expectedTrackId, snapshot.CoverImageBytes, fallbackText, fallbackColor);
                        }
                    });
                    return;
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        UpdateCover(snapshot);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            return;
        }
        finally
        {
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (ReferenceEquals(_coverRefreshCts, owner))
                    {
                        _coverRefreshCts = null;
                        _coverRefreshTrackId = null;
                        owner.Dispose();
                    }
                });
            }
            catch
            {
                // App is shutting down; the owner is disposed by window cleanup.
            }
        }
    }

    private void UpdateCoverAccent(byte[]? bytes)
    {
        var accent = _settings.UseCoverAccentColor || _settings.BackgroundMaterial == LyricsBackgroundMaterial.CoverTint
            ? CoverAccentExtractor.TryExtract(bytes)
            : null;

        if (accent == _coverAccentColor)
        {
            return;
        }

        _coverAccentColor = accent;
        ApplyCurrentVisualStyle();
    }

    private bool IsRejectedCover(string? trackId, byte[] bytes)
    {
        return string.Equals(trackId, _lastRejectedCoverTrackId, StringComparison.Ordinal) &&
            string.Equals(BuildCoverSignature(bytes), _lastRejectedCoverSignature, StringComparison.Ordinal);
    }

    private void RememberRejectedCover(string? trackId, byte[] bytes)
    {
        _lastRejectedCoverTrackId = trackId;
        _lastRejectedCoverSignature = BuildCoverSignature(bytes);
    }

    private void ClearRejectedCover()
    {
        _lastRejectedCoverTrackId = null;
        _lastRejectedCoverSignature = string.Empty;
    }

    private static string BuildCoverSignature(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var middle = bytes[bytes.Length / 2];
        return $"{bytes.Length}:{bytes[0]:X2}:{middle:X2}:{bytes[^1]:X2}";
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

    private void LogTickDiagnostics(PlaybackSnapshot snapshot, LyricDisplayFrame frame)
    {
        if (!_enableSmtcTimelineMonitor)
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
            Log.Debug("SMTC: No active track found (Track is null)");
            return;
        }

        Log.Debug(
            $"SMTC: Title='{snapshot.Track.Title}', Artist='{snapshot.Track.Artist}', App='{snapshot.Track.SourceApp}', " +
            $"Playing={snapshot.IsPlaying}, Pos={snapshot.Position}, CoverLen={snapshot.CoverImageBytes?.Length ?? 0}, LyricSource='{lyricSource}'");
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

    private void AnchorToTaskbar()
    {
        var workArea = SystemParameters.WorkArea;
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        const double normalTaskbarHeight = 48;
        var taskbarHeight = Math.Max(normalTaskbarHeight, screenHeight - workArea.Height);
        var settings = (System.Windows.Application.Current as App)?.Settings ?? new AppSettings();
        var desiredWidth = settings.AutoAdjustWindowWidth
            ? WindowWidthLimits.Clamp(LyricsDisplay.PreferredWindowWidth + settings.WindowWidthOffset)
            : WindowWidthLimits.Clamp(settings.WindowWidth);
        var desiredHeight = settings.AutoAdjustWindowHeight
            ? Math.Max(36, LyricsDisplay.PreferredWindowHeight + settings.WindowHeightOffset)
            : Math.Max(36, settings.WindowHeight);
        var bottomAnchorHeight = settings.AutoAdjustWindowHeight
            ? Math.Max(36, LyricsDisplay.PreferredWindowBottomAnchorHeight + settings.WindowHeightOffset)
            : desiredHeight;
        Width = desiredWidth;
        Height = desiredHeight;

        Left = settings.HorizontalAnchor switch
        {
            LyricsHorizontalAnchor.Left => Math.Max(0, settings.XOffset),
            LyricsHorizontalAnchor.Center => ((screenWidth - Width) / 2.0) + settings.XOffset,
            _ => Math.Max(0, screenWidth - Width - 230 + settings.XOffset)
        };

        var verticalGrowth = Math.Max(0, desiredHeight - bottomAnchorHeight);
        Top = screenHeight - taskbarHeight + ((taskbarHeight - bottomAnchorHeight) / 2.0) + settings.YOffset - verticalGrowth;
    }

    private void AttachToTaskbarHost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        Topmost = _forceAlwaysOnTop;
        var hWndInsertAfter = _forceAlwaysOnTop
            ? NativeMethods.HWND_TOPMOST
            : NativeMethods.HWND_NOTOPMOST;

        NativeMethods.SetWindowPos(
            hwnd,
            hWndInsertAfter,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_ASYNCWINDOWPOS | NativeMethods.SWP_SHOWWINDOW);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);
    }

    private static void ApplyToolWindowStyle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var extendedStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        var nextStyle = new IntPtr(extendedStyle.ToInt64() | NativeMethods.WS_EX_TOOLWINDOW);
        if (nextStyle != extendedStyle)
        {
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, nextStyle);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == _taskbarCreatedMessage || msg == WmShowWindow)
        {
            Dispatcher.BeginInvoke(() =>
            {
                EnsureVisibleIfExpected();
                AnchorToTaskbar();
                AttachToTaskbarHost();
            });
        }

        return IntPtr.Zero;
    }

    private void EnsureVisibleIfExpected()
    {
        if (System.Windows.Application.Current is not App app || app.IsExiting || !app.UserWantsLyricsVisible)
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
    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal static readonly IntPtr HWND_NOTOPMOST = new(-2);
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_ASYNCWINDOWPOS = 0x4000;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const int SW_SHOWNOACTIVATE = 4;
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
