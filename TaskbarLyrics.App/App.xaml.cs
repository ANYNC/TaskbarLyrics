using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

public partial class App : System.Windows.Application
{
    private static readonly TimeSpan AutoUpdateCheckDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan AutoUpdateCheckInterval = TimeSpan.FromDays(1);

    private SettingsStore? _settingsStore;
    private TrayService? _trayService;
    private SettingsWindow? _settingsWindow;
    private SpectrumTuningWindow? _spectrumTuningWindow;
    private LyricsWindowHost? _lyricsWindowHost;
    private CancellationTokenSource? _activationServerCancellation;
    private SpectrumTuningSettings _spectrumTuningSettings = SpectrumTuningSettings.CreateDefault();

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
        _spectrumTuningSettings = Settings.SpectrumTuning.Clone();
        ApplyStartupForegroundColor(Settings);
        Settings.StartWithWindows = Settings.StartWithWindows || StartupService.IsEnabled();
        StartupService.SetEnabled(Settings.StartWithWindows);

        _lyricsWindowHost = new LyricsWindowHost(Settings);

        if (Settings.ShowLyricsOnStartup)
        {
            _lyricsWindowHost.Show();
        }
        UserWantsLyricsVisible = Settings.ShowLyricsOnStartup;

        _lyricsWindowHost.ApplySpectrumTuning(_spectrumTuningSettings);
        _trayService = new TrayService(
            ToggleLyricsWindow,
            SetSpectrumDisplayMode,
            () => Settings.EnableSpectrum,
            () => Settings.SpectrumDisplayMode,
            OpenSettingsWindow,
            OpenSmtcTimelineMonitorWindow,
            OpenSpectrumTuningWindow,
            ExitApplication);
        StartActivationServer();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _ = RunAutomaticUpdateCheckAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationServerCancellation?.Cancel();
        _activationServerCancellation?.Dispose();
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _settingsStore?.Save(Settings);
        _spectrumTuningWindow?.Close();
        _lyricsWindowHost?.Dispose();
        _trayService?.Dispose();
        SingleInstanceService.Release();
        base.OnExit(e);
    }

    public void SaveSettings(AppSettings settings)
    {
        var nextSettings = settings.Clone();
        nextSettings.SpectrumTuning = Settings.SpectrumTuning.Clone();
        Settings = nextSettings;
        _settingsStore?.Save(Settings);
        _lyricsWindowHost?.ApplySettings(Settings);
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
        ApplySystemThemeForegroundColor(settings, migrateLegacyCustomColor: true);
    }

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

        var nextMode = IsSystemUsingLightTheme()
            ? ForegroundColorMode.Dark
            : ForegroundColorMode.Light;
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
        return trimmed.Length == 7 && trimmed.StartsWith('#')
            ? $"#FF{trimmed[1..]}"
            : trimmed;
    }

    internal static bool IsSystemUsingLightTheme()
    {
        const string personalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        using var key = Registry.CurrentUser.OpenSubKey(personalizeKey);
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
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

    private void SetSpectrumDisplayMode(bool enabled, SpectrumDisplayMode mode)
    {
        Settings.EnableSpectrum = enabled;
        if (enabled)
        {
            Settings.SpectrumDisplayMode = mode;
        }

        _settingsStore?.Save(Settings);
        _lyricsWindowHost?.SetSpectrumDisplayMode(enabled, mode);
        _settingsWindow?.ApplyExternalSettings(Settings.Clone());
    }

    public void OpenSmtcTimelineMonitorWindow()
    {
        _lyricsWindowHost?.OpenSmtcTimelineMonitorWindow();
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
        if (_settingsWindow is { IsVisible: true })
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(Settings.Clone());
        _settingsWindow.Closed += SettingsWindow_Closed;
        _settingsWindow.Show();
    }

    private void StartActivationServer()
    {
        _activationServerCancellation = new CancellationTokenSource();
        _ = Task.Run(() => SingleInstanceService.ListenForActivationAsync(
            () => Dispatcher.InvokeAsync(OpenSettingsWindow).Task,
            _activationServerCancellation.Token));
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

    private void ExitApplication()
    {
        IsExiting = true;
        _lyricsWindowHost?.Close();
        Shutdown();
    }
}
