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
    private readonly Action<AppSettings> _applySettings;
    private readonly Action _exitApp;
    private readonly Func<bool> _isLyricsWindowVisible;
    private readonly Func<AppSettings> _getSettings;

    public TrayService(
        Action toggleLyricsWindow,
        Action openSettings,
        Action rematchLyrics,
        Action clearCaches,
        Action<AppSettings> applySettings,
        Action exitApp,
        Func<bool> isLyricsWindowVisible,
        Func<AppSettings> getSettings)
    {
        _toggleLyricsWindow = toggleLyricsWindow;
        _openSettings = openSettings;
        _rematchLyrics = rematchLyrics;
        _clearCaches = clearCaches;
        _applySettings = applySettings;
        _exitApp = exitApp;
        _isLyricsWindowVisible = isLyricsWindowVisible;
        _getSettings = getSettings;
        _icon = AppIconProvider.LoadTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "TaskbarLyrics",
            Icon = _icon,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => _toggleLyricsWindow();
        _notifyIcon.MouseDown += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right)
            {
                _menuWindow?.IgnoreOutsideClicksFor(TimeSpan.FromMilliseconds(260));
            }
        };
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
        if (_menuWindow is { IsVisible: true })
        {
            _menuWindow.IgnoreOutsideClicksFor(TimeSpan.FromMilliseconds(180));
            _menuWindow.ShowAtCursor(animate: false);
            return;
        }

        _menuWindow?.Close();
        _menuWindow = new TrayMenuWindow(
            _toggleLyricsWindow,
            _openSettings,
            _rematchLyrics,
            _clearCaches,
            _applySettings,
            _exitApp,
            _isLyricsWindowVisible,
            _getSettings);
        _menuWindow.Closed += (_, _) => _menuWindow = null;
        _menuWindow.ShowAtCursor(animate: true);
    }
}
