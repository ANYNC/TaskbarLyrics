using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace TaskbarLyrics.App;

public sealed class TrayService : IDisposable
{
    private readonly Icon _icon;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _contextMenu;
    private TrayMenuWindow? _menuWindow;

    public TrayService(Action toggleLyricsWindow, Action openSettings, Action exitApp)
    {
        _icon = AppIconProvider.LoadTrayIcon();

        _contextMenu = new Forms.ContextMenuStrip();

        var toggleItem = new Forms.ToolStripMenuItem("显示/隐藏歌词");
        toggleItem.Click += (_, _) => toggleLyricsWindow();
        _contextMenu.Items.Add(toggleItem);

        var settingsItem = new Forms.ToolStripMenuItem("设置");
        settingsItem.Click += (_, _) => openSettings();
        _contextMenu.Items.Add(settingsItem);

        _contextMenu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = new Forms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => exitApp();
        _contextMenu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "TaskbarLyrics",
            Icon = _icon,
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        _notifyIcon.DoubleClick += (_, _) => toggleLyricsWindow();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menuWindow?.Close();
        _contextMenu.Dispose();
        _icon.Dispose();
    }

    private void ShowMenu(Action toggleLyricsWindow, Action openSettings, Action exitApp)
    {
        _menuWindow?.Close();
        _menuWindow = new TrayMenuWindow(toggleLyricsWindow, openSettings, exitApp);
        _menuWindow.Closed += (_, _) => _menuWindow = null;
        _menuWindow.ShowAtCursor();
    }
}
