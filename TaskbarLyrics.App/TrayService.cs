using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace TaskbarLyrics.App;

public sealed class TrayService : IDisposable
{
    private readonly Icon _icon;
    private readonly Forms.NotifyIcon _notifyIcon;
    private TrayMenuWindow? _menuWindow;

    public TrayService(
        Action toggleLyricsWindow,
        Action<bool, SpectrumDisplayMode> setSpectrumDisplayMode,
        Func<bool> isSpectrumEnabled,
        Func<SpectrumDisplayMode> getSpectrumDisplayMode,
        Action openCurrentTrackOffsetSettings,
        Action openSettings,
        Action openSmtcMonitor,
        Action openSpectrumTuning,
        Action exitApp)
    {
        _icon = AppIconProvider.LoadTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "TaskbarLyrics",
            Icon = _icon,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => toggleLyricsWindow();
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right)
            {
                var invocationPoint = Forms.Cursor.Position;
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                    ShowMenu(
                        toggleLyricsWindow,
                        setSpectrumDisplayMode,
                        isSpectrumEnabled(),
                        getSpectrumDisplayMode(),
                        openCurrentTrackOffsetSettings,
                        openSettings,
                        openSmtcMonitor,
                        openSpectrumTuning,
                        exitApp,
                        invocationPoint));
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

    private void ShowMenu(
        Action toggleLyricsWindow,
        Action<bool, SpectrumDisplayMode> setSpectrumDisplayMode,
        bool isSpectrumEnabled,
        SpectrumDisplayMode spectrumDisplayMode,
        Action openCurrentTrackOffsetSettings,
        Action openSettings,
        Action openSmtcMonitor,
        Action openSpectrumTuning,
        Action exitApp,
        System.Drawing.Point invocationPoint)
    {
        _menuWindow?.Close();
        _menuWindow = new TrayMenuWindow(
            toggleLyricsWindow,
            setSpectrumDisplayMode,
            isSpectrumEnabled,
            spectrumDisplayMode,
            openCurrentTrackOffsetSettings,
            openSettings,
            openSmtcMonitor,
            openSpectrumTuning,
            exitApp);
        _menuWindow.Closed += (_, _) => _menuWindow = null;
        _menuWindow.ShowAt(invocationPoint);
    }
}
