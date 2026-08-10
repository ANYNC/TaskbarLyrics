using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TaskbarLyrics.Core.Services;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

public partial class App : System.Windows.Application, IDisposable
{
    private static readonly TimeSpan AutoUpdateCheckDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan AutoUpdateCheckInterval = TimeSpan.FromDays(1);

    private SettingsStore? _settingsStore;
    private TrayService? _trayService;
    private SettingsWindow? _settingsWindow;
    private SpectrumTuningWindow? _spectrumTuningWindow;
    private LyricsWindowHost? _lyricsWindowHost;
    private TrackLyricOffsetStore? _trackLyricOffsetStore;
    private GlobalMediaHotkeyService? _mediaHotkeyService;
    private IAppCompositionRoot? _compositionRoot;
    private CancellationTokenSource? _activationServerCancellation;
    private SpectrumTuningSettings _spectrumTuningSettings = SpectrumTuningSettings.CreateDefault();
    private int _isDisposed;

    public AppSettings Settings { get; private set; } = new();

    public bool IsExiting { get; private set; }
    public bool UserWantsLyricsVisible { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!SingleInstanceService.EnsureCurrentInstance())
        {
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);
        Log.EnsureLogsDirectory();
        Wpf.Ui.Appearance.ApplicationAccentColorManager.ApplySystemAccent();

        // 初始化 SQLite 别名与纯音乐映射库
        TaskbarLyrics.Core.Database.SongSearchMapDbContext.InitializeDatabase();

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskbarLyrics",
            "settings.json");

        _settingsStore = new SettingsStore(settingsPath);
        Settings = _settingsStore.Load();
        Settings.FontFamily = AppSettings.NormalizeFontFamily(Settings.FontFamily);
        Settings.SpectrumTuning ??= SpectrumTuningSettings.CreateDefault();
        Settings.GlobalMediaHotkeys ??= new GlobalMediaHotkeySettings();
        NativeWindowTheme.SetMode(Settings.ToolWindowTheme);
        _spectrumTuningSettings = Settings.SpectrumTuning.Clone();
        ApplyStartupForegroundColor(Settings);
        Settings.StartWithWindows = Settings.StartWithWindows || StartupService.IsEnabled();
        StartupService.SetEnabled(Settings.StartWithWindows);

        _compositionRoot = new AppCompositionRoot();
        _trackLyricOffsetStore = new TrackLyricOffsetStore();
        _lyricsWindowHost = new LyricsWindowHost(Settings, _trackLyricOffsetStore, _compositionRoot);
        _mediaHotkeyService = new GlobalMediaHotkeyService(ExecuteMediaHotkeyAsync);
        _mediaHotkeyService.Apply(Settings.GlobalMediaHotkeys);

        if (Settings.ShowLyricsOnStartup)
        {
            _lyricsWindowHost.Show();
        }
        UserWantsLyricsVisible = Settings.ShowLyricsOnStartup;

        _lyricsWindowHost.ApplySpectrumTuning(_spectrumTuningSettings);
        _trayService = new TrayService(
            ToggleLyricsWindow,
            () => Settings.GlobalMediaHotkeys is { Enabled: true } hotkeys
                ? hotkeys.ToggleLyricsVisibility
                : string.Empty,
            () => ToggleMediaHotkeyAction(MediaHotkeyAction.ToggleTranslation),
            () => Settings.GlobalMediaHotkeys is { Enabled: true } hotkeys
                ? hotkeys.ToggleTranslation
                : string.Empty,
            () => ToggleMediaHotkeyAction(MediaHotkeyAction.ToggleWordScanning),
            () => Settings.GlobalMediaHotkeys is { Enabled: true } hotkeys
                ? hotkeys.ToggleWordScanning
                : string.Empty,
            SetSpectrumDisplayMode,
            () => Settings.SpectrumDisplayMode,
            OpenCurrentTrackOffsetSettings,
            OpenSettingsWindow,
            OpenSmtcTimelineMonitorWindow,
            OpenSpectrumTuningWindow,
            ExitApplication);
        StartActivationServer();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        TaskObserver.Observe(RunAutomaticUpdateCheckAsync(), "automatic update check");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _activationServerCancellation?.Cancel();
        _activationServerCancellation?.Dispose();
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _settingsStore?.Save(Settings);
        _spectrumTuningWindow?.Close();
        _mediaHotkeyService?.Dispose();
        _lyricsWindowHost?.Dispose();
        _trayService?.Dispose();
        _trackLyricOffsetStore?.Dispose();
        SingleInstanceService.Release();
        GC.SuppressFinalize(this);
    }

    public bool SaveSettings(AppSettings settings)
    {
        var currentSettings = Settings;
        var nextSettings = settings.Clone();
        if (!nextSettings.SpectrumAudioAccessGranted)
        {
            nextSettings.SpectrumDisplayMode = SpectrumDisplayMode.Disabled;
        }

        nextSettings.SpectrumTuning = Settings.SpectrumTuning.Clone();
        nextSettings.GlobalMediaHotkeys ??= new GlobalMediaHotkeySettings();
        var changes = AppSettingsChangeSet.Create(currentSettings, nextSettings);
        NativeWindowTheme.SetMode(nextSettings.ToolWindowTheme);
        Settings = nextSettings;
        var saved = _settingsStore?.Save(Settings) ?? false;
        if (changes.RequiresLyricsWindowApply)
        {
            _lyricsWindowHost?.ApplySettings(Settings);
            if (_settingsWindow is not null)
            {
                TaskObserver.Observe(
                    _settingsWindow.ApplyExternalSettingsAsync(Settings.Clone()),
                    "settings window state update");
            }
        }

        if (changes.GlobalMediaHotkeysChanged)
        {
            _mediaHotkeyService?.Apply(Settings.GlobalMediaHotkeys);
        }

        return saved;
    }

    public void PreviewSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _lyricsWindowHost?.ApplySettings(settings);
    }

    public IReadOnlyDictionary<string, string> GetMediaHotkeyStatuses()
    {
        return _mediaHotkeyService?.GetStatusSnapshot()
            ?? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["toggleLyricsVisibility"] = "未注册",
                ["togglePlayPause"] = "未注册",
                ["previousTrack"] = "未注册",
                ["nextTrack"] = "未注册",
                ["seekBackward"] = "未注册",
                ["seekForward"] = "未注册",
                ["toggleTranslation"] = "未注册",
                ["toggleWordScanning"] = "未注册"
            };
    }

    private async Task RunAutomaticUpdateCheckAsync()
    {
        if (!Settings.AutoCheckUpdates)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (Settings.LastUpdateCheckUtc is { } lastCheck &&
            now - lastCheck < AutoUpdateCheckInterval)
        {
            return;
        }

        try
        {
            await Task.Delay(AutoUpdateCheckDelay);
            if (IsExiting || !Settings.AutoCheckUpdates)
            {
                return;
            }

            var result = await UpdateChecker.CheckLatestAsync();
            Settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;

            if (result.HasUpdate &&
                !string.Equals(Settings.LastNotifiedUpdateVersion, result.Version, StringComparison.OrdinalIgnoreCase))
            {
                Settings.LastNotifiedUpdateVersion = result.Version;
                _trayService?.ShowNotification(
                    "TaskbarLyrics 有新版本",
                    $"发现 {result.Version}，当前版本 {result.CurrentVersion}。可在设置页的关于中打开发布页。");
            }

            _settingsStore?.Save(Settings);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            Settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            _settingsStore?.Save(Settings);
        }
    }

    internal static void ApplyStartupForegroundColor(AppSettings settings)
    {
        ForegroundColorPolicy.ApplyStartup(settings, IsSystemUiUsingLightTheme());
    }

    internal static bool ApplySystemThemeForegroundColor(AppSettings settings)
    {
        return ForegroundColorPolicy.ApplySystemTheme(settings, IsSystemUiUsingLightTheme());
    }

    internal static bool IsApplicationUsingLightTheme()
    {
        return ReadLightThemePreference("AppsUseLightTheme");
    }

    internal static bool IsSystemUiUsingLightTheme()
    {
        return ReadLightThemePreference("SystemUsesLightTheme");
    }

    private static bool ReadLightThemePreference(string valueName)
    {
        const string personalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        using var key = Registry.CurrentUser.OpenSubKey(personalizeKey);
        return key?.GetValue(valueName) is not int value || value != 0;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.Color or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        NativeWindowTheme.RefreshSystemTheme();

        Dispatcher.BeginInvoke(() =>
        {
            if (ApplySystemThemeForegroundColor(Settings))
            {
                _settingsStore?.Save(Settings);
                _lyricsWindowHost?.ApplySettings(Settings);
                if (_settingsWindow is not null)
                {
                    TaskObserver.Observe(
                        _settingsWindow.ApplyExternalSettingsAsync(Settings.Clone()),
                        "settings window state update");
                }
            }
        });
    }

    private void ToggleLyricsWindow()
    {
        if (_lyricsWindowHost is null)
        {
            return;
        }

        if (_lyricsWindowHost.IsVisible)
        {
            UserWantsLyricsVisible = false;
            _lyricsWindowHost.Hide();
        }
        else
        {
            UserWantsLyricsVisible = true;
            _lyricsWindowHost.Show();
        }
    }

    private void SetSpectrumDisplayMode(SpectrumDisplayMode mode)
    {
        if (mode != SpectrumDisplayMode.Disabled && !Settings.SpectrumAudioAccessGranted)
        {
            OpenSettingsWindow("lyrics", focusCurrentTrack: false);
            if (_settingsWindow is not null)
            {
                TaskObserver.Observe(
                    _settingsWindow.RequestSpectrumDisplayModeAsync(mode),
                    "spectrum audio access confirmation");
            }

            return;
        }

        Settings.SpectrumDisplayMode = mode;

        _settingsStore?.Save(Settings);
        _lyricsWindowHost?.ApplySettings(Settings);
        if (_settingsWindow is not null)
        {
            TaskObserver.Observe(
                _settingsWindow.ApplyExternalSettingsAsync(Settings.Clone()),
                "settings window state update");
        }
    }

    public void OpenSmtcTimelineMonitorWindow()
    {
        _lyricsWindowHost?.OpenSmtcTimelineMonitorWindow();
    }

    private void ToggleMediaHotkeyAction(MediaHotkeyAction action)
    {
        TaskObserver.Observe(
            ExecuteMediaHotkeyAsync(action, CancellationToken.None),
            $"hotkey {action}");
    }

    private Task ExecuteMediaHotkeyAsync(MediaHotkeyAction action, CancellationToken cancellationToken)
    {
        if (IsExiting || cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        return Dispatcher.InvokeAsync(
            () => ExecuteMediaHotkeyOnUiThreadAsync(action, cancellationToken),
            DispatcherPriority.Normal,
            cancellationToken).Task.Unwrap();
    }

    private Task ExecuteMediaHotkeyOnUiThreadAsync(MediaHotkeyAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (action == MediaHotkeyAction.ToggleLyricsVisibility)
        {
            ToggleLyricsWindow();
            return Task.CompletedTask;
        }

        if (action == MediaHotkeyAction.ToggleTranslation)
        {
            var toggled = Settings.Clone();
            toggled.ShowLyricTranslation = !Settings.ShowLyricTranslation;
            SaveSettings(toggled);
            return Task.CompletedTask;
        }

        if (action == MediaHotkeyAction.ToggleWordScanning)
        {
            var toggled = Settings.Clone();
            toggled.EnableWordScanning = !Settings.EnableWordScanning;
            SaveSettings(toggled);
            return Task.CompletedTask;
        }

        return _lyricsWindowHost?.ExecuteMediaHotkeyAsync(action, cancellationToken) ?? Task.CompletedTask;
    }

    public void ShowLyricsWindow()
    {
        if (_lyricsWindowHost is null)
        {
            return;
        }

        UserWantsLyricsVisible = true;
        _lyricsWindowHost.Show();
    }

    public void MarkLyricsHiddenByUser()
    {
        UserWantsLyricsVisible = false;
    }

    public void MarkLyricsVisibleBySystem()
    {
        UserWantsLyricsVisible = true;
    }

    private void OpenSettingsWindow()
    {
        OpenSettingsWindow(pageId: null, focusCurrentTrack: false);
    }

    private void OpenSettingsWindow(string? pageId, bool focusCurrentTrack)
    {
        if (_settingsWindow is { IsVisible: true })
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
            if (!string.IsNullOrWhiteSpace(pageId))
            {
                TaskObserver.Observe(
                    _settingsWindow.NavigateToPageAsync(pageId, focusCurrentTrack),
                    "settings navigation");
            }
            return;
        }

        if (_trackLyricOffsetStore is null || _compositionRoot is null)
        {
            return;
        }

        _settingsWindow = new SettingsWindow(
            Settings.Clone(),
            _trackLyricOffsetStore,
            () => _lyricsWindowHost?.GetCurrentTrackLyricsContextAsync()
                ?? Task.FromResult<CurrentTrackLyricsContext?>(null),
            _compositionRoot.CreateLyricDiagnosticRunner);
        _settingsWindow.Closed += SettingsWindow_Closed;
        _settingsWindow.Show();
        if (!string.IsNullOrWhiteSpace(pageId))
        {
            TaskObserver.Observe(
                _settingsWindow.NavigateToPageAsync(pageId, focusCurrentTrack),
                "settings navigation");
        }
    }

    private void OpenCurrentTrackOffsetSettings()
    {
        TaskObserver.Observe(OpenCurrentTrackOffsetSettingsAsync(), "open current track offset settings");
    }

    private async Task OpenCurrentTrackOffsetSettingsAsync()
    {
        var context = _lyricsWindowHost is null
            ? null
            : await _lyricsWindowHost.GetCurrentTrackLyricsContextAsync();
        if (context is null ||
            string.IsNullOrWhiteSpace(context.Track.Title) ||
            string.Equals(context.Track.Title, "Unknown Title", StringComparison.OrdinalIgnoreCase))
        {
            _trayService?.ShowNotification("无法调整单曲偏移", "当前没有可调整的歌曲。");
            return;
        }

        if (string.IsNullOrWhiteSpace(context.LyricSource))
        {
            _trayService?.ShowNotification("歌词仍在检索", "歌词源确定后再调整单曲偏移。");
            return;
        }

        OpenSettingsWindow("trackOffsets", focusCurrentTrack: true);
    }

    private void StartActivationServer()
    {
        _activationServerCancellation = new CancellationTokenSource();
        var activationTask = Task.Run(() => SingleInstanceService.ListenForActivationAsync(
            () => Dispatcher.InvokeAsync(OpenSettingsWindow).Task,
            _activationServerCancellation.Token));
        TaskObserver.Observe(activationTask, "single-instance activation listener");
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Closed -= SettingsWindow_Closed;
            _settingsWindow = null;
        }
    }

    public void OpenSpectrumTuningWindow()
    {
        if (!Settings.SpectrumAudioAccessGranted)
        {
            SetSpectrumDisplayMode(SpectrumDisplayMode.PureMusicOrNoLyrics);
            return;
        }

        if (Settings.SpectrumDisplayMode == SpectrumDisplayMode.Disabled)
        {
            SetSpectrumDisplayMode(SpectrumDisplayMode.PureMusicOrNoLyrics);
        }

        if (_spectrumTuningWindow is { IsVisible: true })
        {
            _lyricsWindowHost?.SetSpectrumPreviewEnabled(true);
            if (_spectrumTuningWindow.WindowState == WindowState.Minimized)
            {
                _spectrumTuningWindow.WindowState = WindowState.Normal;
            }

            _spectrumTuningWindow.Activate();
            return;
        }

        _lyricsWindowHost?.SetSpectrumPreviewEnabled(true);
        _spectrumTuningWindow = new SpectrumTuningWindow(_spectrumTuningSettings, ApplySpectrumTuning);
        _spectrumTuningWindow.Closed += SpectrumTuningWindow_Closed;
        _spectrumTuningWindow.Show();
    }

    private void ApplySpectrumTuning(SpectrumTuningSettings settings)
    {
        _spectrumTuningSettings = settings.Clone();
        Settings.SpectrumTuning = _spectrumTuningSettings.Clone();
        _settingsStore?.Save(Settings);
        _lyricsWindowHost?.ApplySpectrumTuning(_spectrumTuningSettings);
    }

    private void SpectrumTuningWindow_Closed(object? sender, EventArgs e)
    {
        _lyricsWindowHost?.SetSpectrumPreviewEnabled(false);
        if (_spectrumTuningWindow is not null)
        {
            _spectrumTuningWindow.Closed -= SpectrumTuningWindow_Closed;
            _spectrumTuningWindow = null;
        }
    }

    public void RetrySpectrumCapture()
    {
        _lyricsWindowHost?.RetrySpectrumCapture();
    }

    private void ExitApplication()
    {
        TaskObserver.Observe(ExitApplicationAsync(), "application shutdown");
    }

    private async Task ExitApplicationAsync()
    {
        if (IsExiting)
        {
            return;
        }

        IsExiting = true;
        if (_mediaHotkeyService is not null)
        {
            await _mediaHotkeyService.StopAsync(TimeSpan.FromSeconds(1));
        }

        _lyricsWindowHost?.Close();
        Shutdown();
    }
}
