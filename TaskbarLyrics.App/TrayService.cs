using System.Drawing;
using Forms = System.Windows.Forms;

namespace TaskbarLyrics.App;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;

    public TrayService(Action toggleLyricsWindow, Action openSettings, Action exitApp)
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "TaskbarLyrics",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildMenu(toggleLyricsWindow, openSettings, exitApp)
        };

        _notifyIcon.DoubleClick += (_, _) => toggleLyricsWindow();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private static Forms.ContextMenuStrip BuildMenu(Action toggleLyricsWindow, Action openSettings, Action exitApp)
    {
        var menu = new Forms.ContextMenuStrip();

        var toggleItem = new Forms.ToolStripMenuItem("ÏÔÊ¾/Òþ²Ø¸è´Ê");
        toggleItem.Click += (_, _) => toggleLyricsWindow();

        var settingsItem = new Forms.ToolStripMenuItem("ÉèÖÃ");
        settingsItem.Click += (_, _) => openSettings();

        var exitItem = new Forms.ToolStripMenuItem("ÍË³ö");
        exitItem.Click += (_, _) => exitApp();

        menu.Items.Add(toggleItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        return menu;
    }
}
