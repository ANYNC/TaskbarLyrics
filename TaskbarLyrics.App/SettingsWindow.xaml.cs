using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using TaskbarLyrics.Core.Utilities;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TaskbarLyrics.App;

public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly AppSettings _settings;
    private readonly TrackLyricOffsetStore _trackLyricOffsetStore;
    private readonly Func<Task<CurrentTrackLyricsContext?>> _getCurrentTrackLyricsContext;
    private readonly Func<LyricDiagnosticRunner> _createLyricDiagnosticRunner;
    private readonly Func<TrackInfo, ResolvedLyrics, CancellationToken, Task<bool>> _tryApplyResolvedLyrics;
    private readonly Func<TrackInfo, ResolvedLyrics, CancellationToken, Task<bool>> _rememberResolvedLyrics;
    private readonly Action _clearLyricCache;
    private readonly DispatcherTimer _trackOffsetRefreshTimer;
    private bool _isWebReady;
    private bool _isTrackOffsetRefreshRunning;
    private bool _isRuntimeStateRefreshRunning;
    private bool _isTrackOffsetsPageActive;
    private string _lastCurrentTrackOffsetPayloadJson = string.Empty;
    private TrackOffsetQueryPayload _trackOffsetQuery = new();
    private string? _pendingPage;
    private bool _pendingFocusCurrentTrack;
    private bool _hasPendingPreviewChanges;
    private SpectrumDisplayMode? _pendingSpectrumDisplayMode;
    private string _lastSpectrumCaptureStateJson = string.Empty;
    private CancellationTokenSource? _lyricDiagnosticsCancellation;
    private CancellationTokenSource? _lyricDiagnosticsApplyCancellation;
    private LyricDiagnosticRunner? _lyricDiagnosticRunner;

    internal SettingsWindow(
        AppSettings settings,
        TrackLyricOffsetStore trackLyricOffsetStore,
        Func<Task<CurrentTrackLyricsContext?>> getCurrentTrackLyricsContext,
        Func<LyricDiagnosticRunner> createLyricDiagnosticRunner,
        Func<TrackInfo, ResolvedLyrics, CancellationToken, Task<bool>> tryApplyResolvedLyrics,
        Func<TrackInfo, ResolvedLyrics, CancellationToken, Task<bool>> rememberResolvedLyrics,
        Action clearLyricCache)
    {
        InitializeComponent();
        AppIconProvider.ApplyWindowIcon(this);
        _settings = settings;
        _trackLyricOffsetStore = trackLyricOffsetStore;
        _getCurrentTrackLyricsContext = getCurrentTrackLyricsContext;
        _createLyricDiagnosticRunner = createLyricDiagnosticRunner;
        _tryApplyResolvedLyrics = tryApplyResolvedLyrics;
        _rememberResolvedLyrics = rememberResolvedLyrics;
        _clearLyricCache = clearLyricCache;
        _trackOffsetRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(600)
        };
        _trackOffsetRefreshTimer.Tick += TrackOffsetRefreshTimer_Tick;
        ApplyWindowTheme();

        SourceInitialized += SettingsWindow_SourceInitialized;
        Loaded += SettingsWindow_Loaded;
        Activated += SettingsWindow_Activated;
        StateChanged += SettingsWindow_StateChanged;
        Closed += SettingsWindow_Closed;
        NativeWindowTheme.ThemeChanged += OnWindowThemeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void SettingsWindow_SourceInitialized(object? sender, EventArgs e)
    {
        ApplyInitialWindowBounds();
        ApplyWindowTheme();
    }

    private void ApplyInitialWindowBounds()
    {
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);
        Width = Math.Clamp(Width, MinWidth, workArea.Width);
        Height = Math.Clamp(Height, MinHeight, workArea.Height);
    }

    private void SettingsWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        TaskObserver.Observe(InitializeSettingsWebViewAndStartRefreshAsync(), "settings web view initialization");
    }

    private async Task InitializeSettingsWebViewAndStartRefreshAsync()
    {
        await InitializeSettingsWebViewAsync();
        _trackOffsetRefreshTimer.Start();
    }

    private void SettingsWindow_Activated(object? sender, EventArgs e)
    {
        ApplyWindowTheme();
    }

    private void SettingsWindow_StateChanged(object? sender, EventArgs e)
    {
        TaskObserver.Observe(PushWindowStateToWebAsync(), "settings window state update");
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        _isWebReady = false;
        if (_hasPendingPreviewChanges)
        {
            SaveSettings();
            _hasPendingPreviewChanges = false;
        }

        NativeWindowTheme.ThemeChanged -= OnWindowThemeChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        var diagnosticsCancellation = Interlocked.Exchange(ref _lyricDiagnosticsCancellation, null);
        diagnosticsCancellation?.Cancel();
        diagnosticsCancellation?.Dispose();
        var diagnosticsApplyCancellation = Interlocked.Exchange(ref _lyricDiagnosticsApplyCancellation, null);
        diagnosticsApplyCancellation?.Cancel();
        diagnosticsApplyCancellation?.Dispose();
        var diagnosticRunner = Interlocked.Exchange(ref _lyricDiagnosticRunner, null);
        diagnosticRunner?.Dispose();
        _trackOffsetRefreshTimer.Stop();
        _trackOffsetRefreshTimer.Tick -= TrackOffsetRefreshTimer_Tick;

        if (SettingsWebView.CoreWebView2 is not null)
        {
            SettingsWebView.CoreWebView2.WebMessageReceived -= SettingsWebView_WebMessageReceived;
            SettingsWebView.CoreWebView2.Navigate("about:blank");
        }

        SettingsWebView.Dispose();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            () => TaskObserver.Observe(PushSettingsToWebAsync(), "settings display list update"),
            DispatcherPriority.Background);
    }

    private async Task InitializeSettingsWebViewAsync()
    {
        if (_isWebReady)
        {
            return;
        }

        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskbarLyrics",
            "WebView2",
            "Settings");
        var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await SettingsWebView.EnsureCoreWebView2Async(environment);
        ApplyWindowTheme();

        var core = SettingsWebView.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.WebMessageReceived += SettingsWebView_WebMessageReceived;

        var htmlPath = Path.Combine(AppContext.BaseDirectory, "Web", "Settings", "settings.html");
        SettingsWebView.Source = new Uri(htmlPath);
        _isWebReady = true;
    }

    private void SettingsWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        TaskObserver.Observe(HandleSettingsWebMessageAsync(e), "settings web message");
    }

    private async Task HandleSettingsWebMessageAsync(CoreWebView2WebMessageReceivedEventArgs e)
    {
        var messageJson = e.TryGetWebMessageAsString();
        if (string.IsNullOrWhiteSpace(messageJson))
        {
            messageJson = e.WebMessageAsJson;
        }

        var message = SettingsWebMessageRouter.Parse(messageJson);
        if (message?.Type is null)
        {
            return;
        }

        switch (message.Type)
        {
            case "ready":
                await PushSettingsToWebAsync();
                await PushWindowStateToWebAsync();
                await PushPendingNavigationAsync();
                await PushPendingSpectrumDisplayModeAsync();
                await PushSpectrumCaptureStateAsync(force: true);
                break;
            case "update":
                ApplyWebSettingUpdate(message.Key, message.Value);
                await SaveSettingsAndNotifyWebAsync();
                _hasPendingPreviewChanges = false;
                if (RequiresSettingsStateRefresh(message.Key))
                {
                    await PushSettingsToWebAsync();
                }
                else if (IsLyricsLayoutSetting(message.Key))
                {
                    await PushLyricsLayoutPreviewAsync();
                }
                break;
            case "previewUpdate":
                if (!IsPreviewableSetting(message.Key))
                {
                    break;
                }

                ApplyWebSettingUpdate(message.Key, message.Value);
                _hasPendingPreviewChanges = true;
                if (System.Windows.Application.Current is App previewApp)
                {
                    previewApp.PreviewSettings(_settings);
                }

                if (IsLyricsLayoutSetting(message.Key))
                {
                    await PushLyricsLayoutPreviewAsync();
                }
                break;
            case "reorderSources":
                ApplySourceOrder(message.Value);
                await SaveSettingsAndNotifyWebAsync();
                break;
            case "resetDefaults":
                var defaultSettings = new AppSettings();
                App.ApplyStartupForegroundColor(defaultSettings);
                CopySettings(defaultSettings, _settings);
                await SaveSettingsAndNotifyWebAsync();
                await PushSettingsToWebAsync();
                break;
            case "resetLyricsLayoutBase":
                _settings.FontSize = AppSettings.DefaultFontSize;
                _settings.CoverSize = AppSettings.DefaultCoverSize;
                _settings.CoverGap = AppSettings.DefaultCoverGap;
                _settings.CoverCornerRadius = AppSettings.DefaultCoverCornerRadius;
                _settings.WindowWidth = AppSettings.DefaultWindowWidth;
                await SaveSettingsAndNotifyWebAsync();
                await PushSettingsToWebAsync();
                break;
            case "resetMediaHotkey":
                ResetMediaHotkey(message.Value);
                await SaveSettingsAndNotifyWebAsync();
                await PushSettingsToWebAsync();
                break;
            case "clearCache":
                _clearLyricCache();
                break;
            case "openSmtcMonitor":
                if (System.Windows.Application.Current is App smtcApp)
                {
                    smtcApp.OpenSmtcTimelineMonitorWindow();
                }
                break;
            case "openSpectrumTuning":
                if (System.Windows.Application.Current is App app)
                {
                    app.OpenSpectrumTuningWindow();
                }
                break;
            case "confirmSpectrumAudioAccess":
                await ConfirmSpectrumAudioAccessAsync(message.Value);
                break;
            case "revokeSpectrumAudioAccess":
                await RevokeSpectrumAudioAccessAsync();
                break;
            case "retrySpectrumCapture":
                if (System.Windows.Application.Current is App retryApp)
                {
                    retryApp.RetrySpectrumCapture();
                    await PushSpectrumRetryingStateAsync();
                }
                break;
            case "disableSpectrum":
                await DisableSpectrumAsync();
                break;
            case "runLyricDiagnostics":
                await RunLyricDiagnosticsAsync();
                break;
            case "applyLyricDiagnosticCandidate":
                await ApplyLyricDiagnosticCandidateAsync(message.Value);
                break;
            case "trackOffsetsPageActivated":
                _isTrackOffsetsPageActive = true;
                await PushCurrentTrackOffsetDataToWebAsync(force: true);
                break;
            case "queryTrackOffsets":
                _trackOffsetQuery = DeserializeTrackOffsetQuery(message.Value) ?? new TrackOffsetQueryPayload();
                await PushTrackOffsetEntriesToWebAsync(_trackOffsetQuery);
                break;
            case "settingsPageChanged":
                _isTrackOffsetsPageActive = message.Value.HasValue &&
                    string.Equals(
                        ReadString(message.Value.Value, string.Empty),
                        "trackOffsets",
                        StringComparison.Ordinal);
                break;
            case "setCurrentTrackOffset":
                await SetCurrentTrackOffsetAsync(message.Value);
                break;
            case "setStoredTrackOffset":
                await SetStoredTrackOffsetAsync(message.Value);
                break;
            case "deleteTrackOffset":
                await DeleteTrackOffsetAsync(message.Value);
                break;
            case "clearTrackOffsets":
                await ClearTrackOffsetsAsync();
                break;
            case "pickColor":
                await PickForegroundColorAsync();
                break;
            case "pickLocalFolder":
                await PickLocalFolderAsync();
                break;
            case "showLyricsWindow":
                if (System.Windows.Application.Current is App lyricsApp)
                {
                    lyricsApp.ShowLyricsWindow();
                }
                break;
            case "checkForUpdates":
                await CheckForUpdatesAsync();
                break;
            case "openExternalLink":
                OpenExternalLink(message.Value.HasValue
                    ? ReadString(message.Value.Value, UpdateChecker.RepositoryUrl)
                    : UpdateChecker.RepositoryUrl);
                break;
            case "windowDrag":
                NativeWindowInteraction.BeginDrag(this);
                break;
            case "windowResizeStart":
                NativeWindowInteraction.BeginResize(this, message.Value.HasValue
                    ? ReadString(message.Value.Value, string.Empty)
                    : string.Empty);
                break;
            case "windowMinimize":
                WindowState = WindowState.Minimized;
                break;
            case "windowMaximize":
                ToggleMaximizeRestore();
                break;
            case "windowClose":
                Close();
                break;
        }
    }

    private async Task PushSettingsToWebAsync()
    {
        if (!_isWebReady || SettingsWebView.CoreWebView2 is null)
        {
            return;
        }

        var payload = CreateSettingsPayload();
        await SettingsWebView.ExecuteScriptAsync(
            WebViewMessageScriptFactory.Dispatch("settingsApp", "settingsState", new
            {
                settings = payload,
                fonts = FontCatalogService.GetOptions()
            }));
    }

    private async Task PushLyricsLayoutPreviewAsync()
    {
        if (!_isWebReady || SettingsWebView.CoreWebView2 is null)
        {
            return;
        }

        await SettingsWebView.ExecuteScriptAsync(
            WebViewMessageScriptFactory.Dispatch(
                "settingsApp",
                "lyricsLayoutPreview",
                CreateLyricsLayoutPreview()));
    }

    private async Task RunLyricDiagnosticsAsync()
    {
        var cancellation = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(
            ref _lyricDiagnosticsCancellation,
            cancellation);
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();
        var previousApplyCancellation = Interlocked.Exchange(
            ref _lyricDiagnosticsApplyCancellation,
            null);
        previousApplyCancellation?.Cancel();
        previousApplyCancellation?.Dispose();
        var previousRunner = Interlocked.Exchange(ref _lyricDiagnosticRunner, null);
        previousRunner?.Dispose();
        LyricDiagnosticRunner? runner = null;

        try
        {
            var context = await _getCurrentTrackLyricsContext();
            cancellation.Token.ThrowIfCancellationRequested();
            if (context is null ||
                string.IsNullOrWhiteSpace(context.Track.Title) ||
                string.Equals(context.Track.Title, "Unknown Title", StringComparison.OrdinalIgnoreCase))
            {
                await PushLyricDiagnosticsStateAsync(new
                {
                    status = "empty",
                    message = "未检测到可检索的当前歌曲，请先开始播放。"
                });
                return;
            }

            var track = context.Track;
            await PushLyricDiagnosticsStateAsync(new
            {
                status = "running",
                track = new
                {
                    track.Title,
                    track.Artist,
                    track.Album,
                    track.SourceApp,
                    durationSeconds = track.Duration.TotalSeconds,
                    track.SongId
                }
            });
            runner = _createLyricDiagnosticRunner();
            Interlocked.Exchange(ref _lyricDiagnosticRunner, runner)?.Dispose();
            var report = await runner.RunAsync(track, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            await PushLyricDiagnosticsStateAsync(new
            {
                status = "success",
                report
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (runner is not null &&
                !ReferenceEquals(Volatile.Read(ref _lyricDiagnosticRunner), runner))
            {
                return;
            }

            Log.Warn($"Lyric diagnostics failed: {exception.Message}");
            await PushLyricDiagnosticsStateAsync(new
            {
                status = "error",
                message = "歌词匹配失败，请稍后重试。"
            });
        }
        finally
        {
            if (runner is not null &&
                ReferenceEquals(Volatile.Read(ref _lyricDiagnosticRunner), runner) &&
                cancellation.IsCancellationRequested)
            {
                Interlocked.CompareExchange(ref _lyricDiagnosticRunner, null, runner);
                runner.Dispose();
            }

            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _lyricDiagnosticsCancellation, null, cancellation),
                    cancellation))
            {
                cancellation.Dispose();
            }
        }
    }

    private async Task ApplyLyricDiagnosticCandidateAsync(JsonElement? value)
    {
        if (!SettingsWebMessageRouter.TryParseLyricDiagnosticCandidateApplyRequest(
                value,
                out var request))
        {
            return;
        }

        var providerId = request.ProviderId;
        var candidateId = request.CandidateId;
        var mode = request.Mode;

        var runner = Volatile.Read(ref _lyricDiagnosticRunner);
        if (runner is null)
        {
            await PushLyricDiagnosticsApplyStateAsync(
                "error",
                providerId,
                candidateId,
                mode,
                "匹配候选已失效，请重新查找。");
            return;
        }

        var cancellation = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(
            ref _lyricDiagnosticsApplyCancellation,
            cancellation);
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();

        try
        {
            await PushLyricDiagnosticsApplyStateAsync(
                "running",
                providerId,
                candidateId,
                mode,
                mode == LyricDiagnosticApplyMode.Remember
                    ? "正在获取、应用并写入歌词缓存…"
                    : "正在获取并应用歌词…");
            var context = await _getCurrentTrackLyricsContext();
            cancellation.Token.ThrowIfCancellationRequested();
            if (context is null ||
                string.IsNullOrWhiteSpace(context.Track.Title) ||
                string.Equals(context.Track.Title, "Unknown Title", StringComparison.OrdinalIgnoreCase))
            {
                await PushLyricDiagnosticsApplyStateAsync(
                    "error",
                    providerId,
                    candidateId,
                    mode,
                    "当前没有可应用歌词的歌曲，请重新查找。");
                return;
            }

            var resolved = await runner.ResolveCandidateAsync(
                context.Track,
                providerId,
                candidateId,
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(Volatile.Read(ref _lyricDiagnosticRunner), runner))
            {
                return;
            }

            if (resolved is null)
            {
                await PushLyricDiagnosticsApplyStateAsync(
                    "error",
                    providerId,
                    candidateId,
                    mode,
                    "候选歌词未通过内容校验，请重新查找或选择其他候选。");
                return;
            }

            var applied = await _tryApplyResolvedLyrics(
                context.Track,
                resolved,
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!applied)
            {
                await PushLyricDiagnosticsApplyStateAsync(
                    "error",
                    providerId,
                    candidateId,
                    mode,
                    "当前歌曲已切换，请重新查找。");
                return;
            }

            if (mode == LyricDiagnosticApplyMode.Remember)
            {
                var remembered = await _rememberResolvedLyrics(
                    context.Track,
                    resolved,
                    cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                await PushLyricDiagnosticsApplyStateAsync(
                    remembered ? "success" : "error",
                    providerId,
                    candidateId,
                    mode,
                    remembered
                        ? "已应用并写入歌词缓存；再次匹配到相同标题和歌手时会直接使用。"
                        : "歌词已应用，但未能写入歌词缓存。");
                return;
            }

            await PushLyricDiagnosticsApplyStateAsync(
                "success",
                providerId,
                candidateId,
                mode,
                "已应用当前歌词。");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(Volatile.Read(ref _lyricDiagnosticRunner), runner) &&
                ReferenceEquals(Volatile.Read(ref _lyricDiagnosticsApplyCancellation), cancellation))
            {
                Log.Warn($"Applying lyric diagnostic candidate failed: {exception.Message}");
                await PushLyricDiagnosticsApplyStateAsync(
                    "error",
                    providerId,
                    candidateId,
                    mode,
                    "应用歌词失败，请重新查找或选择其他候选。");
            }
        }
        finally
        {
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _lyricDiagnosticsApplyCancellation, null, cancellation),
                    cancellation))
            {
                cancellation.Dispose();
            }
        }
    }

    private Task PushLyricDiagnosticsApplyStateAsync(
        string status,
        string providerId,
        string candidateId,
        LyricDiagnosticApplyMode mode,
        string message)
    {
        return PushLyricDiagnosticsStateAsync(new
        {
            status = "success",
            apply = new
            {
                status,
                providerId,
                candidateId,
                mode = mode == LyricDiagnosticApplyMode.Remember ? "remember" : "current",
                message
            }
        });
    }

    private async Task PushLyricDiagnosticsStateAsync(object payload)
    {
        if (!_isWebReady || SettingsWebView.CoreWebView2 is null)
        {
            return;
        }

        await SettingsWebView.ExecuteScriptAsync(
            WebViewMessageScriptFactory.Dispatch("settingsApp", "lyricDiagnosticsState", payload));
    }

    public async Task ApplyExternalSettingsAsync(AppSettings settings)
    {
        CopySettings(settings, _settings);
        await PushSettingsToWebAsync();
    }

    public async Task RequestSpectrumDisplayModeAsync(SpectrumDisplayMode mode)
    {
        if (mode == SpectrumDisplayMode.Disabled)
        {
            return;
        }

        _pendingSpectrumDisplayMode = mode;
        await PushPendingSpectrumDisplayModeAsync();
    }

    public async Task NavigateToPageAsync(string pageId, bool focusCurrentTrack = false)
    {
        _pendingPage = pageId;
        _pendingFocusCurrentTrack = focusCurrentTrack;
        await PushPendingNavigationAsync();
    }

    private void TrackOffsetRefreshTimer_Tick(object? sender, EventArgs e)
    {
        TaskObserver.Observe(RefreshRuntimeStateAsync(), "settings runtime state refresh");
    }

    private async Task RefreshRuntimeStateAsync()
    {
        if (_isRuntimeStateRefreshRunning)
        {
            return;
        }

        _isRuntimeStateRefreshRunning = true;
        try
        {
            await PushSpectrumCaptureStateAsync();
            await RefreshCurrentTrackOffsetAsync();
        }
        finally
        {
            _isRuntimeStateRefreshRunning = false;
        }
    }

    private async Task RefreshCurrentTrackOffsetAsync()
    {
        if (!_isTrackOffsetsPageActive || _isTrackOffsetRefreshRunning)
        {
            return;
        }

        _isTrackOffsetRefreshRunning = true;
        try
        {
            await PushCurrentTrackOffsetDataToWebAsync();
        }
        finally
        {
            _isTrackOffsetRefreshRunning = false;
        }
    }

    private async Task PushCurrentTrackOffsetDataToWebAsync(bool force = false)
    {
        if (!_isWebReady || SettingsWebView.CoreWebView2 is null)
        {
            return;
        }

        var context = await _getCurrentTrackLyricsContext();
        CurrentTrackOffsetPayload? current = null;
        if (context is not null)
        {
            var playerOffset = _settings.GetPlayerLyricOffsetMilliseconds(context.Track.SourceApp);
            var lyricSourceReady = !string.IsNullOrWhiteSpace(context.LyricSource);
            var trackOffset = lyricSourceReady
                ? _trackLyricOffsetStore.GetOffsetMilliseconds(context.Track, context.LyricSource)
                : 0;
            current = new CurrentTrackOffsetPayload
            {
                Title = context.Track.Title,
                Artist = context.Track.Artist,
                SourceApp = context.Track.SourceApp,
                LyricSource = context.LyricSource,
                DurationSeconds = Math.Max(0, (int)Math.Round(context.Track.Duration.TotalSeconds)),
                LyricSourceReady = lyricSourceReady,
                PlayerOffsetMilliseconds = playerOffset,
                TrackOffsetMilliseconds = trackOffset,
                EffectiveOffsetMilliseconds = playerOffset + trackOffset
            };
        }

        var json = JsonSerializer.Serialize(current, SettingsWebJson.Options);
        if (!force && string.Equals(json, _lastCurrentTrackOffsetPayloadJson, StringComparison.Ordinal))
        {
            return;
        }

        _lastCurrentTrackOffsetPayloadJson = json;
        await SettingsWebView.ExecuteScriptAsync(
            WebViewMessageScriptFactory.Dispatch("settingsApp", "currentTrackOffset", current));
    }

    private async Task PushTrackOffsetEntriesToWebAsync(TrackOffsetQueryPayload query)
    {
        if (!_isWebReady || SettingsWebView.CoreWebView2 is null)
        {
            return;
        }

        var page = await _trackLyricOffsetStore.QueryEntriesAsync(
            query.Page,
            query.PageSize,
            query.Search,
            string.Equals(query.LyricSource, "All", StringComparison.OrdinalIgnoreCase)
                ? null
                : query.LyricSource,
            ParseTrackOffsetSort(query.Sort));
        var payload = new TrackOffsetEntriesPagePayload
        {
            RequestId = query.RequestId,
            Page = page.Page,
            PageSize = page.PageSize,
            PageCount = page.PageCount,
            TotalCount = page.TotalCount,
            UnfilteredCount = page.UnfilteredCount,
            LyricSources = page.LyricSources.ToList(),
            Entries = page.Entries.Select(entry => new TrackOffsetEntryPayload
            {
                Key = entry.Key,
                Title = entry.DisplayTitle,
                Artist = entry.DisplayArtist,
                SourceApp = entry.SourceApp,
                LyricSource = entry.LyricSource,
                DurationSeconds = entry.Key.DurationBucketSeconds,
                OffsetMilliseconds = entry.OffsetMilliseconds,
                UpdatedAtUtc = entry.UpdatedAtUtc
            })
            .ToList()
        };
        await SettingsWebView.ExecuteScriptAsync(
            WebViewMessageScriptFactory.Dispatch("settingsApp", "trackOffsetEntries", payload));
    }

    private async Task SetCurrentTrackOffsetAsync(JsonElement? value)
    {
        var context = await _getCurrentTrackLyricsContext();
        if (context is null || string.IsNullOrWhiteSpace(context.LyricSource))
        {
            await PushTrackOffsetSaveStatusAsync(false, "歌词源尚未确定，暂时无法保存单曲偏移。");
            return;
        }

        var offset = ReadOffsetMilliseconds(value, 0);
        var result = await _trackLyricOffsetStore.SetOffsetAsync(
            context.Track,
            context.LyricSource,
            offset);
        await CompleteTrackOffsetMutationAsync(result);
    }

    private async Task SetStoredTrackOffsetAsync(JsonElement? value)
    {
        var mutation = DeserializeTrackOffsetMutation(value);
        if (mutation?.Key is null)
        {
            await PushTrackOffsetSaveStatusAsync(false, "偏移记录无效。");
            return;
        }

        var result = await _trackLyricOffsetStore.SetOffsetAsync(
            mutation.Key.Value,
            mutation.OffsetMilliseconds);
        await CompleteTrackOffsetMutationAsync(result);
    }

    private async Task DeleteTrackOffsetAsync(JsonElement? value)
    {
        var key = DeserializeTrackOffsetKey(value);
        if (key is null)
        {
            await PushTrackOffsetSaveStatusAsync(false, "偏移记录无效。");
            return;
        }

        var result = await _trackLyricOffsetStore.DeleteAsync(key.Value);
        await CompleteTrackOffsetMutationAsync(result);
    }

    private async Task ClearTrackOffsetsAsync()
    {
        var result = await _trackLyricOffsetStore.ClearAsync();
        await CompleteTrackOffsetMutationAsync(result);
    }

    private async Task CompleteTrackOffsetMutationAsync(TrackLyricOffsetSaveResult result)
    {
        _lastCurrentTrackOffsetPayloadJson = string.Empty;
        await PushCurrentTrackOffsetDataToWebAsync(force: true);
        await PushTrackOffsetEntriesToWebAsync(_trackOffsetQuery);
        await PushTrackOffsetSaveStatusAsync(
            result.IsSaved,
            result.IsSaved ? "单曲偏移已保存" : "单曲偏移保存失败，请稍后重试。");
    }

    private async Task PushTrackOffsetSaveStatusAsync(bool isSaved, string message)
    {
        if (!_isWebReady || SettingsWebView.CoreWebView2 is null)
        {
            return;
        }

        var payload = new
        {
            state = isSaved ? "saved" : "error",
            message
        };
        await SettingsWebView.ExecuteScriptAsync(
            WebViewMessageScriptFactory.Dispatch("settingsApp", "trackOffsetSaveStatus", payload));
    }

    private async Task PushPendingNavigationAsync()
    {
        if (!_isWebReady || SettingsWebView.CoreWebView2 is null || string.IsNullOrWhiteSpace(_pendingPage))
        {
            return;
        }

        var page = _pendingPage;
        var focusCurrentTrack = _pendingFocusCurrentTrack;
        _pendingPage = null;
        _pendingFocusCurrentTrack = false;
        await SettingsWebView.ExecuteScriptAsync(
            WebViewMessageScriptFactory.Dispatch("settingsApp", "navigate", new
            {
                page,
                focusCurrentTrack
            }));
    }

    private async Task PushPendingSpectrumDisplayModeAsync()
    {
        if (!_isWebReady ||
            SettingsWebView.CoreWebView2 is null ||
            _pendingSpectrumDisplayMode is not { } mode)
        {
            return;
        }

        await SettingsWebView.ExecuteScriptAsync(
            WebViewMessageScriptFactory.Dispatch("settingsApp", "requestSpectrumDisplayMode", new
            {
                mode = mode.ToString()
            }));
        _pendingSpectrumDisplayMode = null;
    }

    private async Task PushSpectrumCaptureStateAsync(bool force = false)
    {
        if (!_isWebReady || SettingsWebView.CoreWebView2 is null)
        {
            return;
        }

        var diagnostics = SpectrumDiagnosticsState.Current;
        var state = !_settings.SpectrumAudioAccessGranted
            ? "notGranted"
            : _settings.SpectrumDisplayMode == SpectrumDisplayMode.Disabled
                ? "disabled"
                : !string.IsNullOrWhiteSpace(diagnostics.LastError)
                    ? "blocked"
                    : diagnostics.IsCaptureAvailable
                        ? "capturing"
                        : "waiting";
        var message = state switch
        {
            "notGranted" => "尚未允许读取系统播放声音。",
            "disabled" => "频谱已关闭，不会读取系统播放声音。",
            "blocked" => "系统音频采集被系统或安全软件阻止。",
            "capturing" => "正在读取系统播放声音并生成频谱。",
            _ => "已允许；仅在频谱需要显示时读取系统播放声音。"
        };
        var payload = new { state, message };
        var json = JsonSerializer.Serialize(payload, SettingsWebJson.Options);
        if (!force && string.Equals(json, _lastSpectrumCaptureStateJson, StringComparison.Ordinal))
        {
            return;
        }

        _lastSpectrumCaptureStateJson = json;
        await SettingsWebView.ExecuteScriptAsync(
            WebViewMessageScriptFactory.Dispatch("settingsApp", "spectrumCaptureState", payload));
    }

    private async Task PushSpectrumRetryingStateAsync()
    {
        if (!_isWebReady || SettingsWebView.CoreWebView2 is null)
        {
            return;
        }

        var payload = new
        {
            state = "waiting",
            message = "正在重新连接系统音频…"
        };
        _lastSpectrumCaptureStateJson = JsonSerializer.Serialize(payload, SettingsWebJson.Options);
        await SettingsWebView.ExecuteScriptAsync(
            WebViewMessageScriptFactory.Dispatch("settingsApp", "spectrumCaptureState", payload));
    }

    private async Task ConfirmSpectrumAudioAccessAsync(JsonElement? value)
    {
        if (value is null ||
            !Enum.TryParse<SpectrumDisplayMode>(ReadString(value.Value, string.Empty), true, out var mode) ||
            mode == SpectrumDisplayMode.Disabled)
        {
            return;
        }

        _settings.SpectrumAudioAccessGranted = true;
        _settings.SpectrumDisplayMode = mode;
        await SaveSettingsAndNotifyWebAsync();
        await PushSettingsToWebAsync();
        await PushSpectrumCaptureStateAsync(force: true);
    }

    private async Task RevokeSpectrumAudioAccessAsync()
    {
        _settings.SpectrumAudioAccessGranted = false;
        _settings.SpectrumDisplayMode = SpectrumDisplayMode.Disabled;
        await SaveSettingsAndNotifyWebAsync();
        await PushSettingsToWebAsync();
        await PushSpectrumCaptureStateAsync(force: true);
    }

    private async Task DisableSpectrumAsync()
    {
        _settings.SpectrumDisplayMode = SpectrumDisplayMode.Disabled;
        await SaveSettingsAndNotifyWebAsync();
        await PushSettingsToWebAsync();
        await PushSpectrumCaptureStateAsync(force: true);
    }

    private static int ReadOffsetMilliseconds(JsonElement? value, int fallback)
    {
        if (value is null)
        {
            return fallback;
        }

        return (int)Math.Round(Math.Clamp(
            ReadDouble(value.Value, fallback),
            TrackLyricOffsetStore.MinimumOffsetMilliseconds,
            TrackLyricOffsetStore.MaximumOffsetMilliseconds));
    }

    private static TrackOffsetMutationPayload? DeserializeTrackOffsetMutation(JsonElement? value)
    {
        return value is null
            ? null
            : JsonSerializer.Deserialize<TrackOffsetMutationPayload>(value.Value.GetRawText(), SettingsWebJson.Options);
    }

    private static TrackOffsetQueryPayload? DeserializeTrackOffsetQuery(JsonElement? value)
    {
        return value is null
            ? null
            : JsonSerializer.Deserialize<TrackOffsetQueryPayload>(value.Value.GetRawText(), SettingsWebJson.Options);
    }

    private static TrackLyricOffsetSort ParseTrackOffsetSort(string? value)
    {
        return value switch
        {
            "title" => TrackLyricOffsetSort.Title,
            "offset" => TrackLyricOffsetSort.OffsetMagnitude,
            _ => TrackLyricOffsetSort.Updated
        };
    }

    private static TrackLyricOffsetRecordKey? DeserializeTrackOffsetKey(JsonElement? value)
    {
        return value is null
            ? null
            : JsonSerializer.Deserialize<TrackLyricOffsetRecordKey>(value.Value.GetRawText(), SettingsWebJson.Options);
    }

    private WebSettingsPayload CreateSettingsPayload()
    {
        _settings.NormalizePlayerSources();
        _settings.NormalizeLyricsTextAlignment();
        var mediaHotkeys = _settings.GlobalMediaHotkeys ??= new GlobalMediaHotkeySettings();
        var layoutMetrics = CreateLyricsLayoutMetrics();
        return new WebSettingsPayload
        {
            SourceRecognitionOrder = NormalizeSourceOrder(_settings.SourceRecognitionOrder),
            EnableNetease = _settings.EnableNetease,
            EnableQQMusic = _settings.EnableQQMusic,
            EnableKugou = _settings.EnableKugou,
            EnableSpotify = _settings.EnableSpotify,
            PlayerLyricOffsets = _settings.PlayerSources.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.LyricOffsetMilliseconds,
                StringComparer.OrdinalIgnoreCase),
            DefaultPlayerLyricOffsets = _settings.PlayerSources.Keys.ToDictionary(
                source => source,
                AppSettings.GetDefaultPlayerLyricOffsetMilliseconds,
                StringComparer.OrdinalIgnoreCase),
            EnableLocalLyrics = _settings.EnableLocalLyrics,
            LocalMusicFolders = NormalizeLocalMusicFolders(_settings.LocalMusicFolders),
            EnableGlobalMediaHotkeys = mediaHotkeys.Enabled,
            HotkeyTogglePlayPause = mediaHotkeys.TogglePlayPause,
            HotkeyPreviousTrack = mediaHotkeys.PreviousTrack,
            HotkeyNextTrack = mediaHotkeys.NextTrack,
            HotkeySeekBackward = mediaHotkeys.SeekBackward,
            HotkeySeekForward = mediaHotkeys.SeekForward,
            HotkeyToggleLyricsVisibility = mediaHotkeys.ToggleLyricsVisibility,
            HotkeyToggleTranslation = mediaHotkeys.ToggleTranslation,
            HotkeyToggleWordScanning = mediaHotkeys.ToggleWordScanning,
            MediaHotkeyStatuses = (System.Windows.Application.Current as App)?.GetMediaHotkeyStatuses()
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal),
            MediaHotkeys = MediaHotkeyCatalog.Definitions.Select(definition => new WebMediaHotkeyDefinition
            {
                Action = definition.Action.ToString(),
                SettingKey = definition.SettingKey,
                StatusKey = definition.StatusKey,
                DisplayName = definition.DisplayName
            })
            .ToList(),
            ShowLyricsOnStartup = _settings.ShowLyricsOnStartup,
            AutoHideWhenNoPlayback = _settings.AutoHideWhenNoPlayback,
            StartWithWindows = _settings.StartWithWindows,
            AutoCheckUpdates = _settings.AutoCheckUpdates,
            ShowLyricTranslation = _settings.ShowLyricTranslation,
            EnableWordScanning = _settings.EnableWordScanning,
            ToolWindowTheme = _settings.ToolWindowTheme,
            SpectrumDisplayMode = _settings.SpectrumDisplayMode.ToString(),
            SpectrumAudioAccessGranted = _settings.SpectrumAudioAccessGranted,
            FontSize = _settings.FontSize,
            ShowCover = _settings.ShowCover,
            CoverSize = _settings.CoverSize,
            CoverGap = _settings.CoverGap,
            CoverCornerRadius = _settings.CoverCornerRadius,
            LyricsLayoutScalePercent = layoutMetrics.ScalePercent,
            EffectiveFontSize = layoutMetrics.FontSize,
            EffectiveCoverSize = layoutMetrics.CoverSize,
            EffectiveCoverGap = layoutMetrics.CoverGap,
            EffectiveCoverCornerRadius = layoutMetrics.CoverCornerRadius,
            EffectiveWindowWidth = AppSettings.ClampEffectiveWindowWidth(
                _settings.WindowWidth,
                _settings.LyricsLayoutScalePercent,
                SystemParameters.WorkArea.Width),
            FontFamily = FontCatalogService.ResolveInstalledFamily(AppSettings.NormalizeFontFamily(_settings.FontFamily)) ?? AppSettings.BundledFontFamily,
            FontWeight = NormalizeFontWeight(_settings.FontWeight),
            ForegroundColorMode = _settings.ForegroundColorMode,
            ForegroundColor = _settings.ForegroundColor,
            ShowBackground = _settings.ShowBackground,
            BackgroundOpacity = _settings.BackgroundOpacity,
            ShowBorder = _settings.ShowBorder,
            ShowTextShadow = _settings.ShowTextShadow,
            WindowWidth = _settings.WindowWidth,
            HorizontalAnchor = _settings.HorizontalAnchor,
            LyricsTextAlignment = _settings.LyricsTextAlignment,
            XOffset = _settings.XOffset,
            YOffset = _settings.YOffset,
            ForceAlwaysOnTop = _settings.ForceAlwaysOnTop,
            TaskbarEmbeddingEnabled = _settings.TaskbarEmbeddingEnabled,
            EmbeddedTaskbarWidth = _settings.EmbeddedTaskbarWidth,
            EmbeddedTaskbarHorizontalOffset = _settings.EmbeddedTaskbarHorizontalOffset,
            EmbeddedTaskbarVerticalOffset = _settings.EmbeddedTaskbarVerticalOffset,
            LyricsDisplayMode = _settings.LyricsDisplayMode,
            SelectedDisplayIds = _settings.SelectedDisplayIds.ToList(),
            AvailableDisplays = DisplayMonitorService.GetDisplays()
                .Select(display => new WebDisplayMonitor
                {
                    Id = display.Id,
                    Name = display.Name,
                    IsPrimary = display.IsPrimary,
                    Width = display.Width,
                    Height = display.Height
                })
                .ToList(),
            AppVersion = UpdateChecker.GetCurrentVersion(),
            RepositoryUrl = UpdateChecker.RepositoryUrl
        };
    }

    private object CreateLyricsLayoutPreview()
    {
        var metrics = CreateLyricsLayoutMetrics();
        return new
        {
            scalePercent = metrics.ScalePercent,
            fontSize = AppSettings.ClampFontSize(_settings.FontSize),
            coverSize = AppSettings.ClampCoverSize(_settings.CoverSize),
            coverGap = AppSettings.ClampCoverGap(_settings.CoverGap),
            coverCornerRadius = AppSettings.ClampCoverCornerRadius(
                _settings.CoverCornerRadius,
                AppSettings.ClampCoverSize(_settings.CoverSize)),
            effectiveFontSize = metrics.FontSize,
            effectiveCoverSize = metrics.CoverSize,
            effectiveCoverGap = metrics.CoverGap,
            effectiveCoverCornerRadius = metrics.CoverCornerRadius,
            effectiveWindowWidth = AppSettings.ClampEffectiveWindowWidth(
                _settings.WindowWidth,
                _settings.LyricsLayoutScalePercent,
                SystemParameters.WorkArea.Width)
        };
    }

    private LyricsLayoutMetrics CreateLyricsLayoutMetrics()
    {
        return LyricsLayoutMetrics.Create(_settings, VisualTreeHelper.GetDpi(this).DpiScaleX);
    }

    private static List<string> NormalizeSourceOrder(IEnumerable<string>? order)
    {
        var known = new[] { "QQMusic", "Netease", "Kugou", "Spotify" };
        var result = new List<string>();

        foreach (var source in order ?? Enumerable.Empty<string>())
        {
            if (known.Contains(source) && !result.Contains(source))
            {
                result.Add(source);
            }
        }

        foreach (var source in known)
        {
            if (!result.Contains(source))
            {
                result.Add(source);
            }
        }

        return result;
    }

    private static string NormalizeFontWeight(string? value)
    {
        return value?.Trim() switch
        {
            "Light" => "Light",
            "Normal" => "Normal",
            "Medium" => "Medium",
            "SemiBold" => "SemiBold",
            "Bold" => "Bold",
            _ => "SemiBold"
        };
    }

    private void ApplyWebSettingUpdate(string? key, JsonElement? value)
    {
        if (key is null || value is null)
        {
            return;
        }

        var element = value.Value;
        const string playerLyricOffsetPrefix = "playerLyricOffset:";
        if (key.StartsWith(playerLyricOffsetPrefix, StringComparison.Ordinal))
        {
            var sourceApp = key[playerLyricOffsetPrefix.Length..];
            var offset = (int)Math.Round(ReadDouble(
                element,
                _settings.GetPlayerLyricOffsetMilliseconds(sourceApp)));
            _settings.SetPlayerLyricOffsetMilliseconds(sourceApp, offset);
            return;
        }

        var hotkeyDefinition = MediaHotkeyCatalog.Definitions
            .FirstOrDefault(definition => string.Equals(definition.SettingKey, key, StringComparison.Ordinal));
        if (hotkeyDefinition is not null)
        {
            var hotkeySettings = EnsureMediaHotkeySettings();
            hotkeyDefinition.WriteBinding(
                hotkeySettings,
                ReadHotkeyBinding(element, hotkeyDefinition.ReadBinding(hotkeySettings)));
            return;
        }

        switch (key)
        {
            case "enableQQMusic":
                _settings.EnableQQMusic = ReadBool(element, _settings.EnableQQMusic);
                break;
            case "enableNetease":
                _settings.EnableNetease = ReadBool(element, _settings.EnableNetease);
                break;
            case "enableKugou":
                _settings.EnableKugou = ReadBool(element, _settings.EnableKugou);
                break;
            case "enableSpotify":
                _settings.EnableSpotify = ReadBool(element, _settings.EnableSpotify);
                break;
            case "enableLocalLyrics":
                _settings.EnableLocalLyrics = ReadBool(element, _settings.EnableLocalLyrics);
                break;
            case "localMusicFolders":
                _settings.LocalMusicFolders = NormalizeLocalMusicFolders(ReadStringList(element));
                break;
            case "enableGlobalMediaHotkeys":
                EnsureMediaHotkeySettings().Enabled = ReadBool(element, EnsureMediaHotkeySettings().Enabled);
                break;
            case "showLyricsOnStartup":
                _settings.ShowLyricsOnStartup = ReadBool(element, _settings.ShowLyricsOnStartup);
                break;
            case "autoHideWhenNoPlayback":
                _settings.AutoHideWhenNoPlayback = ReadBool(element, _settings.AutoHideWhenNoPlayback);
                break;
            case "startWithWindows":
                _settings.StartWithWindows = ReadBool(element, _settings.StartWithWindows);
                StartupService.SetEnabled(_settings.StartWithWindows);
                break;
            case "autoCheckUpdates":
                _settings.AutoCheckUpdates = ReadBool(element, _settings.AutoCheckUpdates);
                break;
            case "showLyricTranslation":
                _settings.ShowLyricTranslation = ReadBool(element, _settings.ShowLyricTranslation);
                break;
            case "enableWordScanning":
                _settings.EnableWordScanning = ReadBool(element, _settings.EnableWordScanning);
                break;
            case "toolWindowTheme":
                if (Enum.TryParse<ToolWindowTheme>(ReadString(element, _settings.ToolWindowTheme.ToString()), true, out var toolWindowTheme))
                {
                    _settings.ToolWindowTheme = toolWindowTheme;
                }
                break;
            case "spectrumDisplayMode":
                var spectrumDisplayMode = ReadString(element, SpectrumDisplayMode.Disabled.ToString());
                if (Enum.TryParse<SpectrumDisplayMode>(spectrumDisplayMode, true, out var parsedSpectrumDisplayMode))
                {
                    _settings.SpectrumDisplayMode = parsedSpectrumDisplayMode == SpectrumDisplayMode.Disabled ||
                        _settings.SpectrumAudioAccessGranted
                            ? parsedSpectrumDisplayMode
                            : SpectrumDisplayMode.Disabled;
                }
                break;
            case "showBackground":
                _settings.ShowBackground = ReadBool(element, _settings.ShowBackground);
                break;
            case "showBorder":
                _settings.ShowBorder = ReadBool(element, _settings.ShowBorder);
                break;
            case "showTextShadow":
                _settings.ShowTextShadow = ReadBool(element, _settings.ShowTextShadow);
                break;
            case "forceAlwaysOnTop":
                _settings.ForceAlwaysOnTop = ReadBool(element, _settings.ForceAlwaysOnTop);
                break;
            case "taskbarEmbeddingEnabled":
                _settings.TaskbarEmbeddingEnabled = ReadBool(element, _settings.TaskbarEmbeddingEnabled);
                break;
            case "embeddedTaskbarWidth":
                _settings.EmbeddedTaskbarWidth = AppSettings.ClampEmbeddedTaskbarWidth(
                    ReadDouble(element, _settings.EmbeddedTaskbarWidth));
                break;
            case "embeddedTaskbarHorizontalOffset":
                _settings.EmbeddedTaskbarHorizontalOffset = AppSettings.ClampEmbeddedTaskbarOffset(
                    ReadDouble(element, _settings.EmbeddedTaskbarHorizontalOffset));
                break;
            case "embeddedTaskbarVerticalOffset":
                _settings.EmbeddedTaskbarVerticalOffset = AppSettings.ClampEmbeddedTaskbarOffset(
                    ReadDouble(element, _settings.EmbeddedTaskbarVerticalOffset));
                break;
            case "lyricsDisplayMode":
                _settings.LyricsDisplayMode = ReadEnum(element, _settings.LyricsDisplayMode);
                break;
            case "selectedDisplayIds":
                _settings.SelectedDisplayIds = ReadStringList(element);
                _settings.NormalizeDisplaySelection();
                break;
            case "fontSize":
                _settings.FontSize = AppSettings.ClampFontSize(ReadDouble(element, _settings.FontSize));
                break;
            case "showCover":
                _settings.ShowCover = ReadBool(element, _settings.ShowCover);
                break;
            case "coverSize":
                _settings.CoverSize = AppSettings.ClampCoverSize(ReadDouble(element, _settings.CoverSize));
                _settings.CoverCornerRadius = AppSettings.ClampCoverCornerRadius(_settings.CoverCornerRadius, _settings.CoverSize);
                break;
            case "coverGap":
                _settings.CoverGap = AppSettings.ClampCoverGap(ReadDouble(element, _settings.CoverGap));
                break;
            case "coverCornerRadius":
                _settings.CoverCornerRadius = AppSettings.ClampCoverCornerRadius(ReadDouble(element, _settings.CoverCornerRadius), _settings.CoverSize);
                break;
            case "lyricsLayoutScalePercent":
                _settings.LyricsLayoutScalePercent = AppSettings.ClampLyricsLayoutScalePercent(
                    ReadDouble(element, _settings.LyricsLayoutScalePercent));
                break;
            case "fontFamily":
                _settings.FontFamily = AppSettings.NormalizeFontFamily(ReadString(element, _settings.FontFamily));
                break;
            case "fontWeight":
                _settings.FontWeight = NormalizeFontWeight(ReadString(element, _settings.FontWeight));
                break;
            case "foregroundColor":
                _settings.ForegroundColorMode = ForegroundColorMode.Custom;
                _settings.ForegroundColor = NormalizeColor(ReadString(element, _settings.ForegroundColor));
                break;
            case "foregroundColorMode":
                _settings.ForegroundColorMode = ReadForegroundColorMode(element, _settings.ForegroundColorMode);
                ApplyForegroundColorMode();
                break;
            case "backgroundOpacity":
                _settings.BackgroundOpacity = Math.Clamp(ReadDouble(element, _settings.BackgroundOpacity), 0, 1);
                break;
            case "windowWidth":
                _settings.WindowWidth = Math.Clamp(
                    ReadDouble(element, _settings.WindowWidth),
                    AppSettings.MinimumWindowWidth,
                    AppSettings.MaximumWindowWidth);
                break;
            case "horizontalAnchor":
                if (Enum.TryParse<LyricsHorizontalAnchor>(ReadString(element, _settings.HorizontalAnchor.ToString()), out var anchor))
                {
                    _settings.HorizontalAnchor = anchor;
                }
                break;
            case "lyricsTextAlignment":
                _settings.NormalizeLyricsTextAlignment();
                var textAlignment = ReadString(element, string.Empty);
                if (Enum.TryParse<LyricsTextAlignment>(textAlignment, ignoreCase: true, out var parsedTextAlignment) &&
                    Enum.IsDefined(parsedTextAlignment))
                {
                    _settings.LyricsTextAlignment = parsedTextAlignment;
                }
                break;
            case "xOffset":
                _settings.XOffset = Math.Clamp(ReadDouble(element, _settings.XOffset), -2000, 2000);
                break;
            case "yOffset":
                _settings.YOffset = Math.Clamp(ReadDouble(element, _settings.YOffset), -2000, 2000);
                break;
        }
    }

    private void ResetMediaHotkey(JsonElement? value)
    {
        if (value is null ||
            !Enum.TryParse<MediaHotkeyAction>(ReadString(value.Value, string.Empty), true, out var action))
        {
            return;
        }

        EnsureMediaHotkeySettings().ResetBinding(action);
    }

    private GlobalMediaHotkeySettings EnsureMediaHotkeySettings()
    {
        return _settings.GlobalMediaHotkeys ??= new GlobalMediaHotkeySettings();
    }

    private static bool IsMediaHotkeySetting(string? key)
    {
        return key == "enableGlobalMediaHotkeys" ||
            MediaHotkeyCatalog.Definitions.Any(definition =>
                string.Equals(definition.SettingKey, key, StringComparison.Ordinal));
    }

    private static bool RequiresSettingsStateRefresh(string? key)
    {
        return key is "foregroundColorMode" or "spectrumDisplayMode" || IsMediaHotkeySetting(key);
    }

    private static bool IsLyricsLayoutSetting(string? key)
    {
        return key is "fontSize" or
            "coverSize" or
            "coverGap" or
            "coverCornerRadius" or
            "lyricsLayoutScalePercent" or
            "windowWidth";
    }

    private static bool IsPreviewableSetting(string? key)
    {
        return key is
            "backgroundOpacity" or
            "coverGap" or
            "coverCornerRadius" or
            "lyricsLayoutScalePercent" or
            "windowWidth" or
            "xOffset" or
            "yOffset" or
            "embeddedTaskbarWidth" or
            "embeddedTaskbarHorizontalOffset" or
            "embeddedTaskbarVerticalOffset";
    }

    private static string ReadHotkeyBinding(JsonElement element, string fallback)
    {
        var binding = ReadString(element, fallback).Trim();
        return binding.Length <= 64 ? binding : fallback;
    }

    private void ApplySourceOrder(JsonElement? value)
    {
        if (value is null || value.Value.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        _settings.SourceRecognitionOrder = NormalizeSourceOrder(value.Value.EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!));
    }

    private async Task PickForegroundColorAsync()
    {
        using var dialog = new Forms.ColorDialog
        {
            FullOpen = true
        };

        if (TryParseMediaColor(_settings.ForegroundColor, out var currentColor))
        {
            dialog.Color = Drawing.Color.FromArgb(currentColor.R, currentColor.G, currentColor.B);
        }

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        _settings.ForegroundColor = $"#FF{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        _settings.ForegroundColorMode = ForegroundColorMode.Custom;
        await SaveSettingsAndNotifyWebAsync();
        await PushSettingsToWebAsync();
    }

    private async Task PickLocalFolderAsync()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择本地音乐目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        var initialFolder = _settings.LocalMusicFolders.FirstOrDefault(Directory.Exists);
        if (!string.IsNullOrWhiteSpace(initialFolder))
        {
            dialog.SelectedPath = initialFolder;
        }

        if (dialog.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        _settings.LocalMusicFolders = NormalizeLocalMusicFolders(
            _settings.LocalMusicFolders.Append(dialog.SelectedPath));
        await SaveSettingsAndNotifyWebAsync();
        await PushSettingsToWebAsync();
    }

    private void ApplyForegroundColorMode()
    {
        ForegroundColorPolicy.ApplySelectedMode(
            _settings,
            App.IsSystemUiUsingLightTheme());
    }

    private async Task CheckForUpdatesAsync()
    {
        await PushUpdateStatusToWebAsync(new UpdateStatusPayload
        {
            State = "checking",
            Message = "正在检查更新..."
        });

        try
        {
            var result = await UpdateChecker.CheckLatestAsync();
            if (result.State == UpdateCheckState.Error)
            {
                await PushUpdateStatusToWebAsync(new UpdateStatusPayload
                {
                    State = "error",
                    Message = "没有读取到最新版本信息。"
                });
                return;
            }

            await PushUpdateStatusToWebAsync(new UpdateStatusPayload
            {
                State = result.HasUpdate ? "available" : "latest",
                Message = result.HasUpdate
                    ? $"发现新版本 {result.Version}，当前版本 {result.CurrentVersion}。"
                    : $"当前已是最新版本：{result.CurrentVersion}。",
                Version = result.Version,
                Url = result.Url
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            await PushUpdateStatusToWebAsync(new UpdateStatusPayload
            {
                State = "error",
                Message = "检查更新失败，请稍后重试。"
            });
        }
    }

    private async Task PushUpdateStatusToWebAsync(UpdateStatusPayload payload)
    {
        if (!_isWebReady || SettingsWebView.CoreWebView2 is null)
        {
            return;
        }

        await SettingsWebView.ExecuteScriptAsync(
            WebViewMessageScriptFactory.Dispatch("settingsApp", "updateStatus", payload));
    }

    private async Task PushWindowStateToWebAsync()
    {
        if (!_isWebReady || SettingsWebView.CoreWebView2 is null)
        {
            return;
        }

        var state = WindowState == WindowState.Maximized ? "maximized" : "normal";
        await SettingsWebView.ExecuteScriptAsync(
            WebViewMessageScriptFactory.Dispatch("settingsApp", "windowState", state));
    }

    private static void OpenExternalLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http"))
        {
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
    }

    private bool SaveSettings()
    {
        if (System.Windows.Application.Current is App app)
        {
            return app.SaveSettings(_settings.Clone());
        }

        return false;
    }

    private async Task SaveSettingsAndNotifyWebAsync()
    {
        var success = SaveSettings();
        if (!_isWebReady || SettingsWebView.CoreWebView2 is null)
        {
            return;
        }

        await SettingsWebView.ExecuteScriptAsync(
            WebViewMessageScriptFactory.Dispatch(
                "settingsApp",
                "settingsSaveResult",
                new { success }));
    }

    private static bool ReadBool(JsonElement element, bool fallback)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var value) => value,
            _ => fallback
        };
    }

    private static double ReadDouble(JsonElement element, double fallback)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDouble(out var value) => value,
            JsonValueKind.String when double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) => value,
            _ => fallback
        };
    }

    private static string ReadString(JsonElement element, string fallback)
    {
        return element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? fallback
            : fallback;
    }

    private static TEnum ReadEnum<TEnum>(JsonElement element, TEnum fallback)
        where TEnum : struct, Enum
    {
        return element.ValueKind == JsonValueKind.String &&
            Enum.TryParse<TEnum>(element.GetString(), ignoreCase: true, out var value)
                ? value
                : fallback;
    }

    private static List<string> ReadStringList(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToList();
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString()?
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList() ?? new List<string>();
        }

        return new List<string>();
    }

    private static List<string> NormalizeLocalMusicFolders(IEnumerable<string>? folders)
    {
        return (folders ?? Enumerable.Empty<string>())
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => folder.Trim().Trim('"'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeColor(string color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return AppSettings.LightForegroundColor;
        }

        var trimmed = color.Trim();
        return trimmed.Length == 7 && trimmed.StartsWith('#')
            ? $"#FF{trimmed[1..]}"
            : trimmed;
    }

    private static ForegroundColorMode ReadForegroundColorMode(JsonElement element, ForegroundColorMode fallback)
    {
        if (element.ValueKind == JsonValueKind.String &&
            Enum.TryParse<ForegroundColorMode>(element.GetString(), out var stringValue))
        {
            return stringValue;
        }

        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out var intValue) &&
            Enum.IsDefined(typeof(ForegroundColorMode), intValue))
        {
            return (ForegroundColorMode)intValue;
        }

        return fallback;
    }

    private static bool TryParseMediaColor(string? color, out System.Windows.Media.Color parsedColor)
    {
        parsedColor = Colors.White;
        if (string.IsNullOrWhiteSpace(color))
        {
            return false;
        }

        try
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(color.Trim()) is System.Windows.Media.Color mediaColor)
            {
                parsedColor = mediaColor;
                return true;
            }
        }
        catch (FormatException)
        {
            return false;
        }

        return false;
    }

    private static void CopySettings(AppSettings source, AppSettings target)
    {
        target.SourceRecognitionOrder = source.SourceRecognitionOrder.ToList();
        target.EnableNetease = source.EnableNetease;
        target.EnableQQMusic = source.EnableQQMusic;
        target.EnableKugou = source.EnableKugou;
        target.EnableSpotify = source.EnableSpotify;
        source.NormalizePlayerSources();
        target.PlayerSources = source.PlayerSources.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);
        target.EnableLocalLyrics = source.EnableLocalLyrics;
        target.LocalMusicFolders = NormalizeLocalMusicFolders(source.LocalMusicFolders);
        target.GlobalMediaHotkeys = (source.GlobalMediaHotkeys ?? new GlobalMediaHotkeySettings()).Clone();
        target.ShowLyricsOnStartup = source.ShowLyricsOnStartup;
        target.AutoHideWhenNoPlayback = source.AutoHideWhenNoPlayback;
        target.StartWithWindows = source.StartWithWindows;
        target.AutoCheckUpdates = source.AutoCheckUpdates;
        target.LastUpdateCheckUtc = source.LastUpdateCheckUtc;
        target.LastNotifiedUpdateVersion = source.LastNotifiedUpdateVersion;
        target.ShowLyricTranslation = source.ShowLyricTranslation;
        target.EnableWordScanning = source.EnableWordScanning;
        target.ToolWindowTheme = source.ToolWindowTheme;
        target.SpectrumDisplayMode = source.SpectrumDisplayMode;
        target.SpectrumAudioAccessGranted = source.SpectrumAudioAccessGranted;
        target.UseSafeFontSizeRange = source.UseSafeFontSizeRange;
        target.FontSize = source.FontSize;
        target.UseSafeCoverSizeRange = source.UseSafeCoverSizeRange;
        target.ShowCover = source.ShowCover;
        target.CoverSize = source.CoverSize;
        target.CoverGap = source.CoverGap;
        target.CoverCornerRadius = source.CoverCornerRadius;
        target.LyricsLayoutScalePercent = source.LyricsLayoutScalePercent;
        target.FontFamily = source.FontFamily;
        target.FontWeight = source.FontWeight;
        target.ForegroundColorMode = source.ForegroundColorMode;
        target.ForegroundColor = source.ForegroundColor;
        target.ShowBackground = source.ShowBackground;
        target.BackgroundOpacity = source.BackgroundOpacity;
        target.ShowBorder = source.ShowBorder;
        target.ShowTextShadow = source.ShowTextShadow;
        target.WindowWidth = source.WindowWidth;
        target.HorizontalAnchor = source.HorizontalAnchor;
        target.LyricsTextAlignment = source.LyricsTextAlignment;
        target.XOffset = source.XOffset;
        target.YOffset = source.YOffset;
        target.ForceAlwaysOnTop = source.ForceAlwaysOnTop;
        target.TaskbarEmbeddingEnabled = source.TaskbarEmbeddingEnabled;
        target.EmbeddedTaskbarWidth = source.EmbeddedTaskbarWidth;
        target.EmbeddedTaskbarHorizontalOffset = source.EmbeddedTaskbarHorizontalOffset;
        target.EmbeddedTaskbarVerticalOffset = source.EmbeddedTaskbarVerticalOffset;
        target.LyricsDisplayMode = source.LyricsDisplayMode;
        target.SelectedDisplayIds = source.SelectedDisplayIds.ToList();
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
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
        NativeWindowTheme.Apply(this, SettingsWebView);
    }

    private sealed class CurrentTrackOffsetPayload
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string SourceApp { get; set; } = string.Empty;
        public string LyricSource { get; set; } = string.Empty;
        public int DurationSeconds { get; set; }
        public bool LyricSourceReady { get; set; }
        public int PlayerOffsetMilliseconds { get; set; }
        public int TrackOffsetMilliseconds { get; set; }
        public int EffectiveOffsetMilliseconds { get; set; }
    }

    private sealed class TrackOffsetQueryPayload
    {
        public int RequestId { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public string Search { get; set; } = string.Empty;
        public string LyricSource { get; set; } = "All";
        public string Sort { get; set; } = "updated";
    }

    private sealed class TrackOffsetEntriesPagePayload
    {
        public int RequestId { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int PageCount { get; set; }
        public int TotalCount { get; set; }
        public int UnfilteredCount { get; set; }
        public List<string> LyricSources { get; set; } = new();
        public List<TrackOffsetEntryPayload> Entries { get; set; } = new();
    }

    private sealed class TrackOffsetEntryPayload
    {
        public TrackLyricOffsetRecordKey Key { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string SourceApp { get; set; } = string.Empty;
        public string LyricSource { get; set; } = string.Empty;
        public int DurationSeconds { get; set; }
        public int OffsetMilliseconds { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }

    private sealed class TrackOffsetMutationPayload
    {
        public TrackLyricOffsetRecordKey? Key { get; set; }
        public int OffsetMilliseconds { get; set; }
    }

    private sealed class WebSettingsPayload
    {
        public List<string> SourceRecognitionOrder { get; set; } = new();
        public bool EnableNetease { get; set; }
        public bool EnableQQMusic { get; set; }
        public bool EnableKugou { get; set; }
        public bool EnableSpotify { get; set; }
        public Dictionary<string, int> PlayerLyricOffsets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> DefaultPlayerLyricOffsets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool EnableLocalLyrics { get; set; }
        public List<string> LocalMusicFolders { get; set; } = new();
        public bool EnableGlobalMediaHotkeys { get; set; }
        public string HotkeyTogglePlayPause { get; set; } = "";
        public string HotkeyPreviousTrack { get; set; } = "";
        public string HotkeyNextTrack { get; set; } = "";
        public string HotkeySeekBackward { get; set; } = "";
        public string HotkeySeekForward { get; set; } = "";
        public string HotkeyToggleLyricsVisibility { get; set; } = "";
        public string HotkeyToggleTranslation { get; set; } = "";
        public string HotkeyToggleWordScanning { get; set; } = "";
        public Dictionary<string, string> MediaHotkeyStatuses { get; set; } = new(StringComparer.Ordinal);
        public List<WebMediaHotkeyDefinition> MediaHotkeys { get; set; } = [];
        public bool ShowLyricsOnStartup { get; set; }
        public bool AutoHideWhenNoPlayback { get; set; }
        public bool StartWithWindows { get; set; }
        public bool AutoCheckUpdates { get; set; }
        public bool ShowLyricTranslation { get; set; }
        public bool EnableWordScanning { get; set; }
        public ToolWindowTheme ToolWindowTheme { get; set; }
        public string SpectrumDisplayMode { get; set; } = TaskbarLyrics.App.SpectrumDisplayMode.Disabled.ToString();
        public bool SpectrumAudioAccessGranted { get; set; }
        public double FontSize { get; set; }
        public bool ShowCover { get; set; }
        public double CoverSize { get; set; }
        public double CoverGap { get; set; }
        public double CoverCornerRadius { get; set; }
        public double LyricsLayoutScalePercent { get; set; }
        public double EffectiveFontSize { get; set; }
        public double EffectiveCoverSize { get; set; }
        public double EffectiveCoverGap { get; set; }
        public double EffectiveCoverCornerRadius { get; set; }
        public double EffectiveWindowWidth { get; set; }
        public string FontFamily { get; set; } = "";
        public string FontWeight { get; set; } = "";
        public ForegroundColorMode ForegroundColorMode { get; set; }
        public string ForegroundColor { get; set; } = "";
        public bool ShowBackground { get; set; }
        public double BackgroundOpacity { get; set; }
        public bool ShowBorder { get; set; }
        public bool ShowTextShadow { get; set; }
        public double WindowWidth { get; set; }
        public LyricsHorizontalAnchor HorizontalAnchor { get; set; }
        public LyricsTextAlignment LyricsTextAlignment { get; set; }
        public double XOffset { get; set; }
        public double YOffset { get; set; }
        public bool ForceAlwaysOnTop { get; set; }

        public bool TaskbarEmbeddingEnabled { get; set; }

        public double EmbeddedTaskbarWidth { get; set; }

        public double EmbeddedTaskbarHorizontalOffset { get; set; }

        public double EmbeddedTaskbarVerticalOffset { get; set; }

        public LyricsDisplayMode LyricsDisplayMode { get; set; }

        public List<string> SelectedDisplayIds { get; set; } = [];

        public List<WebDisplayMonitor> AvailableDisplays { get; set; } = [];
        public string AppVersion { get; set; } = "";
        public string RepositoryUrl { get; set; } = "";
    }

    private sealed class WebDisplayMonitor
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public bool IsPrimary { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }
    }

    private sealed class WebMediaHotkeyDefinition
    {
        public string Action { get; set; } = "";

        public string SettingKey { get; set; } = "";

        public string StatusKey { get; set; } = "";

        public string DisplayName { get; set; } = "";
    }

    private sealed class UpdateStatusPayload
    {
        public string State { get; set; } = "";

        public string Message { get; set; } = "";

        public string Version { get; set; } = "";

        public string Url { get; set; } = "";
    }

}
