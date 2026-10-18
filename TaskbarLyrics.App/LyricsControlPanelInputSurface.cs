using System.Windows;

namespace TaskbarLyrics.App;

internal static class LyricsControlPanelInputSurface
{
    internal static void Apply(FrameworkElement surface, AppSettings settings, LyricsLayoutMetrics metrics)
    {
        // WebView2 draws in a child HWND. A layered WPF host also needs nonzero alpha
        // behind the lyrics, or Windows passes the pointer through before the page receives it.
        surface.Visibility = !settings.UseFloatingWindow && settings.EnableControlPanel && settings.ControlPanelTriggerLyrics
            ? Visibility.Visible : Visibility.Collapsed;
        var coverInset = settings.ShowCover ? metrics.CoverSize + metrics.CoverGap : 0;
        if (metrics.IsVertical)
        {
            var coverAtBottom = settings.CoverPosition == CoverPosition.Right;
            surface.Margin = new Thickness(metrics.LayoutHorizontalPadding, coverAtBottom ? 0 : coverInset,
                metrics.LayoutHorizontalPadding, (coverAtBottom ? coverInset : 0) - metrics.ViewportDescenderBuffer);
            return;
        }
        surface.Margin = new Thickness(
            metrics.LayoutHorizontalPadding + (settings.CoverPosition == CoverPosition.Left ? coverInset : 0),
            0,
            metrics.LayoutHorizontalPadding + (settings.CoverPosition == CoverPosition.Right ? coverInset : 0),
            -metrics.ViewportDescenderBuffer);
    }
}
