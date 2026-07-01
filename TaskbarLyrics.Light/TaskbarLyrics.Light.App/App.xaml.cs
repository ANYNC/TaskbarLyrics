using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using TaskbarLyrics.Core.Services;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Light.App;

public partial class App : System.Windows.Application
{
    private static readonly TimeSpan AutoUpdateCheckDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan AutoUpdateCheckInterval = TimeSpan.FromDays(1);

    private SettingsStore? _settingsStore;
    private TrayService? _trayService;
    private SettingsWindow? _settingsWindow;
    private SpectrumTuningWindow? _spectrumTuningWindow;
    private LyricsWindowHost? _lyricsWindowHost;
    private PlayerPresenceMonitor? _playerPresenceMonitor;
    private CancellationTokenSource? _activationServerCancellation;
    private SpectrumTuningSettings _spectrumTuningSettings = SpectrumTuningSettings.CreateDefault();
    private bool? _lastAppliedStartWithWindows;
    private bool _userDismissedAutoLyrics;

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

        BundledFontRegistrar.Register();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskbarLyrics.Light",
            "settings.json");

        _settingsStore = new SettingsStore(settingsPath);
        Settings = _settingsStore.Load();
        ApplyStartupForegroundColor(Settings);
        StartupRunRegistrar.Apply(Settings.StartWithWindows);
        _lastAppliedStartWithWindows = Settings.StartWithWindows;

        _lyricsWindowHost = new LyricsWindowHost(Settings);

        // 开启「播放器关闭时隐藏」时，启动阶段默认不显示；若同时开启「播放器打开时显示」，由 PlayerPresenceMonitor 检测到播放后再显示。
        var shouldShowOnStartup = Settings.ShowLyricsOnStartup && !Settings.AutoHideLyricsWhenPlayerCloses;
        if (shouldShowOnStartup)
        {
            _lyricsWindowHost.Show();
        }

        UserWantsLyricsVisible = shouldShowOnStartup;
        _lyricsWindowHost.ApplySpectrumTuning(_spectrumTuningSettings);
        _trayService = new TrayService(
            ToggleLyricsWindow,
            OpenSettingsWindow,
            RematchCurrentLyrics,
            ClearLyricCaches,
            CycleSpectrumStyle,
            ToggleSmtcTimelineMonitor,
            ExitApplication,
            () => Settings.Clone());
        StartActivationServer();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        ConfigurePlayerPresenceMonitor();
        _ = RunAutomaticUpdateCheckAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationServerCancellation?.Cancel();
        _activationServerCancellation?.Dispose();
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _playerPresenceMonitor?.Dispose();
        _settingsStore?.Save(Settings);
        _spectrumTuningWindow?.Close();
        _lyricsWindowHost?.Dispose();
        _trayService?.Dispose();
        SingleInstanceService.Release();
        base.OnExit(e);
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
                    "TaskbarLyrics Light 有新版本",
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

    private void StartActivationServer()
    {
        _activationServerCancellation = new CancellationTokenSource();
        _ = Task.Run(() => SingleInstanceService.ListenForActivationAsync(
            () => Dispatcher.InvokeAsync(OpenSettingsWindow).Task,
            _activationServerCancellation.Token));
    }

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        _settingsStore?.Save(Settings);
        if (_lastAppliedStartWithWindows != Settings.StartWithWindows)
        {
            StartupRunRegistrar.Apply(Settings.StartWithWindows);
            _lastAppliedStartWithWindows = Settings.StartWithWindows;
        }

        ConfigurePlayerPresenceMonitor();
        _lyricsWindowHost?.ApplySettings(Settings);
    }

    public void RematchCurrentLyrics()
    {
        _lyricsWindowHost?.RematchCurrentLyrics();
    }

    public void ClearLyricCaches()
    {
        LyricProviderBase.ClearCache();
        GenericSmtcLyricProvider.ClearCache();
    }

    public void CycleSpectrumStyle()
    {
        var next = Settings.SpectrumStyle switch
        {
            SpectrumDisplayStyle.Center => SpectrumDisplayStyle.Bottom,
            SpectrumDisplayStyle.Bottom => SpectrumDisplayStyle.Mirror,
            SpectrumDisplayStyle.Mirror => SpectrumDisplayStyle.Thin,
            SpectrumDisplayStyle.Thin => SpectrumDisplayStyle.Dots,
            SpectrumDisplayStyle.Dots => SpectrumDisplayStyle.Pulse,
            _ => SpectrumDisplayStyle.Center
        };

        var snapshot = Settings.Clone();
        snapshot.SpectrumStyle = next;
        SaveSettings(snapshot);
        _settingsWindow?.ApplyExternalSettings(snapshot.Clone());
    }

    public void ToggleSmtcTimelineMonitor()
    {
        var snapshot = Settings.Clone();
        snapshot.EnableSmtcTimelineMonitor = !snapshot.EnableSmtcTimelineMonitor;
        SaveSettings(snapshot);
        _settingsWindow?.ApplyExternalSettings(snapshot.Clone());
    }

    internal static void ApplyStartupForegroundColor(AppSettings settings) =>
        ApplySystemThemeForegroundColor(settings, migrateLegacyCustomColor: true);

    internal static bool ApplySystemThemeForegroundColor(AppSettings settings, bool migrateLegacyCustomColor = false)
    {
        if (migrateLegacyCustomColor && IsLegacyCustomForeground(settings.ForegroundColor))
        {
            settings.ForegroundColorMode = ForegroundColorMode.Custom;
            return false;
        }

        if (settings.ForegroundColorMode == ForegroundColorMode.Custom)
        {
            return false;
        }

        var nextMode = IsSystemUsingLightTheme() ? ForegroundColorMode.Dark : ForegroundColorMode.Light;
        var nextColor = nextMode == ForegroundColorMode.Dark
            ? AppSettings.DarkForegroundColor
            : AppSettings.LightForegroundColor;

        var changed = settings.ForegroundColorMode != nextMode ||
            !string.Equals(settings.ForegroundColor, nextColor, StringComparison.OrdinalIgnoreCase);
        settings.ForegroundColorMode = nextMode;
        settings.ForegroundColor = nextColor;
        return changed;
    }

    private static bool IsLegacyCustomForeground(string? color)
    {
        var normalized = NormalizeColor(color);
        return !string.Equals(normalized, AppSettings.DarkForegroundColor, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalized, AppSettings.LightForegroundColor, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return AppSettings.LightForegroundColor;
        }

        var trimmed = color.Trim();
        return trimmed.Length == 7 && trimmed.StartsWith('#') ? $"#FF{trimmed[1..]}" : trimmed;
    }

    internal static bool IsSystemUsingLightTheme()
    {
        const string personalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        using var key = Registry.CurrentUser.OpenSubKey(personalizeKey);
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.Color or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (ApplySystemThemeForegroundColor(Settings))
            {
                _settingsStore?.Save(Settings);
                _lyricsWindowHost?.ApplySettings(Settings);
                _settingsWindow?.ApplyExternalSettings(Settings.Clone());
            }
        });
    }

    private void ConfigurePlayerPresenceMonitor()
    {
        var shouldMonitor = Settings.AutoShowLyricsWhenPlayerOpens || Settings.AutoHideLyricsWhenPlayerCloses;
        if (!shouldMonitor)
        {
            _playerPresenceMonitor?.Stop();
            return;
        }

        _playerPresenceMonitor ??= new PlayerPresenceMonitor();
        _playerPresenceMonitor.PresenceChanged -= OnPlayerPresenceChanged;
        _playerPresenceMonitor.ApplySettings(Settings);
        _playerPresenceMonitor.PresenceChanged += OnPlayerPresenceChanged;
        _playerPresenceMonitor.Start();
    }

    private void OnPlayerPresenceChanged(object? sender, bool isActive)
    {
        if (_lyricsWindowHost is null || IsExiting)
        {
            return;
        }

        if (!isActive)
        {
            _userDismissedAutoLyrics = false;
            if (Settings.AutoHideLyricsWhenPlayerCloses)
            {
                UserWantsLyricsVisible = false;
                _lyricsWindowHost.Hide();
            }

            return;
        }

        if (Settings.AutoShowLyricsWhenPlayerOpens && !_userDismissedAutoLyrics)
        {
            UserWantsLyricsVisible = true;
            _lyricsWindowHost.Show();
        }
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
            _userDismissedAutoLyrics = Settings.AutoShowLyricsWhenPlayerOpens;
            _lyricsWindowHost.Hide();
        }
        else
        {
            UserWantsLyricsVisible = true;
            _userDismissedAutoLyrics = false;
            _lyricsWindowHost.Show();
        }
    }

    public void MarkLyricsHiddenByUser()
    {
        UserWantsLyricsVisible = false;
        if (Settings.AutoShowLyricsWhenPlayerOpens)
        {
            _userDismissedAutoLyrics = true;
        }
    }

    public void MarkLyricsVisibleBySystem() => UserWantsLyricsVisible = true;

    private void OpenSettingsWindow()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(Settings.Clone());
        _settingsWindow.Closed += SettingsWindow_Closed;
        _settingsWindow.Show();
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
        if (_spectrumTuningWindow is { IsVisible: true })
        {
            _spectrumTuningWindow.Activate();
            return;
        }

        _spectrumTuningWindow = new SpectrumTuningWindow(_spectrumTuningSettings, ApplySpectrumTuning);
        _spectrumTuningWindow.Closed += SpectrumTuningWindow_Closed;
        _spectrumTuningWindow.Show();
    }

    private void ApplySpectrumTuning(SpectrumTuningSettings settings)
    {
        _spectrumTuningSettings = settings.Clone();
        _lyricsWindowHost?.ApplySpectrumTuning(_spectrumTuningSettings);
    }

    private void SpectrumTuningWindow_Closed(object? sender, EventArgs e)
    {
        if (_spectrumTuningWindow is not null)
        {
            _spectrumTuningWindow.Closed -= SpectrumTuningWindow_Closed;
            _spectrumTuningWindow = null;
        }
    }

    private void ExitApplication()
    {
        IsExiting = true;
        _lyricsWindowHost?.Close();
        Shutdown();
    }
}
