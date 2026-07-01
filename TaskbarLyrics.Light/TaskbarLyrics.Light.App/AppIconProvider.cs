using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TaskbarLyrics.Light.App;

internal static class AppIconProvider
{
    private static string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon", "TaskbarLyrics.ico");

    public static Icon LoadTrayIcon()
    {
        return File.Exists(IconPath)
            ? new Icon(IconPath)
            : SystemIcons.Application;
    }

    public static void ApplyWindowIcon(Window window)
    {
        if (!File.Exists(IconPath))
        {
            return;
        }

        using var stream = File.OpenRead(IconPath);
        window.Icon = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }
}
