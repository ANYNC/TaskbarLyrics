using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace TaskbarLyrics.Light.App;

public sealed class TrayService : IDisposable
{
    private readonly Icon _icon;
    private readonly Forms.NotifyIcon _notifyIcon;
    private TrayMenuWindow? _menuWindow;

    private readonly Action _toggleLyricsWindow;
    private readonly Action _openSettings;
    private readonly Action _rematchLyrics;
    private readonly Action _clearCaches;
    private readonly Action _cycleSpectrumStyle;
    private readonly Action _toggleTimelineMonitor;
    private readonly Action _exitApp;
    private readonly Func<AppSettings> _getSettings;

    public TrayService(
        Action toggleLyricsWindow,
        Action openSettings,
        Action rematchLyrics,
        Action clearCaches,
        Action cycleSpectrumStyle,
        Action toggleTimelineMonitor,
        Action exitApp,
        Func<AppSettings> getSettings)
    {
        _toggleLyricsWindow = toggleLyricsWindow;
        _openSettings = openSettings;
        _rematchLyrics = rematchLyrics;
        _clearCaches = clearCaches;
        _cycleSpectrumStyle = cycleSpectrumStyle;
        _toggleTimelineMonitor = toggleTimelineMonitor;
        _exitApp = exitApp;
        _getSettings = getSettings;
        _icon = AppIconProvider.LoadTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "TaskbarLyrics",
            Icon = _icon,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => _toggleLyricsWindow();
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                    ShowMenu());
            }
        };
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menuWindow?.Close();
        _icon.Dispose();
    }

    public void ShowNotification(string title, string message, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info)
    {
        if (!_notifyIcon.Visible)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(7000);
    }

    private void ShowMenu()
    {
        _menuWindow?.Close();
        _menuWindow = new TrayMenuWindow(
            _toggleLyricsWindow,
            _openSettings,
            _rematchLyrics,
            _clearCaches,
            _cycleSpectrumStyle,
            _toggleTimelineMonitor,
            _exitApp,
            _getSettings);
        _menuWindow.Closed += (_, _) => _menuWindow = null;
        _menuWindow.ShowAtCursor();
    }
}
