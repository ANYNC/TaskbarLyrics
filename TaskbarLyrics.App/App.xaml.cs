using System.IO;
using System.Windows;

namespace TaskbarLyrics.App;

public partial class App : System.Windows.Application
{
    private SettingsStore? _settingsStore;
    private TrayService? _trayService;
    private SettingsWindow? _settingsWindow;
    private MainWindow? _mainWindow;

    public AppSettings Settings { get; private set; } = new();

    public bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskbarLyrics",
            "settings.json");

        _settingsStore = new SettingsStore(settingsPath);
        Settings = _settingsStore.Load();

        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;

        if (Settings.ShowLyricsOnStartup)
        {
            _mainWindow.Show();
        }

        _trayService = new TrayService(ToggleLyricsWindow, OpenSettingsWindow, ExitApplication);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settingsStore?.Save(Settings);
        _trayService?.Dispose();
        base.OnExit(e);
    }

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        _settingsStore?.Save(Settings);
        _mainWindow?.ApplySettings(Settings);
    }

    private void ToggleLyricsWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (_mainWindow.IsVisible)
        {
            _mainWindow.Hide();
        }
        else
        {
            _mainWindow.Show();
        }
    }

    private void OpenSettingsWindow()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(Settings.Clone());
        _settingsWindow.Owner = _mainWindow;
        _settingsWindow.Show();
    }

    private void ExitApplication()
    {
        IsExiting = true;
        _mainWindow?.Close();
        Shutdown();
    }
}
