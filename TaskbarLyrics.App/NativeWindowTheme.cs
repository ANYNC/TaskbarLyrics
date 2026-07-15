using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Drawing = System.Drawing;
using WpfColor = System.Windows.Media.Color;

namespace TaskbarLyrics.App;

internal static class NativeWindowTheme
{
    private static readonly WpfColor DarkBackground = WpfColor.FromRgb(10, 10, 10);
    private static readonly WpfColor DarkForeground = WpfColor.FromRgb(250, 250, 250);
    private static readonly WpfColor LightBackground = WpfColor.FromRgb(250, 250, 250);
    private static readonly WpfColor LightForeground = WpfColor.FromRgb(24, 24, 27);

    public static void Apply(Window window, WebView2 webView)
    {
        var isLight = App.IsSystemUsingLightTheme();
        var background = isLight ? LightBackground : DarkBackground;
        var foreground = isLight ? LightForeground : DarkForeground;

        window.Background = CreateBrush(background);
        window.Foreground = CreateBrush(foreground);
        webView.DefaultBackgroundColor = Drawing.Color.FromArgb(
            byte.MaxValue,
            background.R,
            background.G,
            background.B);

        if (webView.CoreWebView2 is not null)
        {
            webView.CoreWebView2.Profile.PreferredColorScheme = isLight
                ? CoreWebView2PreferredColorScheme.Light
                : CoreWebView2PreferredColorScheme.Dark;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } compositionTarget })
        {
            compositionTarget.BackgroundColor = background;
        }

        var darkMode = isLight ? 0 : 1;
        _ = DwmSetWindowAttribute(
            hwnd,
            DwmWindowAttributeUseImmersiveDarkMode,
            ref darkMode,
            Marshal.SizeOf<int>());

        var cornerPreference = DwmWindowCornerPreferenceRound;
        _ = DwmSetWindowAttribute(
            hwnd,
            DwmWindowAttributeWindowCornerPreference,
            ref cornerPreference,
            Marshal.SizeOf<int>());
    }

    private static SolidColorBrush CreateBrush(WpfColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private const int DwmWindowAttributeUseImmersiveDarkMode = 20;
    private const int DwmWindowAttributeWindowCornerPreference = 33;
    private const int DwmWindowCornerPreferenceRound = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
}
