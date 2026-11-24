namespace TaskbarLyrics.App;

internal readonly record struct EmbeddedTaskbarPosition(double Width, double Height, double Left, double Top)
{
    internal static EmbeddedTaskbarPosition Calculate(AppSettings settings, TaskbarBounds taskbar, double pixelsPerDip)
    {
        var embedded = settings;
        if (settings.UseFloatingWindow)
        {
            embedded = settings.Clone();
            embedded.UseFloatingWindow = false;
        }
        var metrics = LyricsLayoutMetrics.Create(embedded, pixelsPerDip, taskbar);
        var width = metrics.IsVertical ? taskbar.Width
            : AppSettings.ClampEffectiveWindowWidth(settings.WindowWidth, taskbar.Width);
        var height = Math.Min(metrics.DesiredWindowHeight, taskbar.Height);
        var horizontalTravel = Math.Max(0, taskbar.Width - width);
        var verticalTravel = Math.Max(0, taskbar.Height - height);
        var left = settings.TaskbarXAnchorPercent is { } x
            ? horizontalTravel * (x / 100)
            : EmbeddedTaskbarLayoutCalculator.CalculateHorizontalLeft(taskbar.Width, width, settings.HorizontalAnchor, settings.XOffset);
        var top = settings.TaskbarYAnchorPercent is { } y
            ? verticalTravel * (y / 100)
            : EmbeddedTaskbarLayoutCalculator.CalculateVerticalTop(taskbar.Height, height, settings.YOffset, alignTop: metrics.IsVertical);
        return new(width, height, Math.Clamp(left, 0, horizontalTravel), Math.Clamp(top, 0, verticalTravel));
    }

    internal static (double X, double Y) GetAnchorPercents(AppSettings settings, TaskbarBounds taskbar, double pixelsPerDip)
    {
        var position = Calculate(settings, taskbar, pixelsPerDip);
        var defaultX = settings.HorizontalAnchor switch
        {
            LyricsHorizontalAnchor.Left => 0,
            LyricsHorizontalAnchor.Center => 50,
            _ => 100
        };
        return (
            settings.TaskbarXAnchorPercent ?? ToPercent(position.Left, taskbar.Width - position.Width, defaultX),
            settings.TaskbarYAnchorPercent ?? ToPercent(position.Top, taskbar.Height - position.Height, taskbar.IsVertical ? 0 : 50));
    }

    internal static bool MigrateLegacy(AppSettings settings, TaskbarBounds taskbar, double pixelsPerDip)
    {
        if (settings.TaskbarXAnchorPercent.HasValue && settings.TaskbarYAnchorPercent.HasValue) return false;
        var (x, y) = GetAnchorPercents(settings, taskbar, pixelsPerDip);
        settings.TaskbarXAnchorPercent = x;
        settings.TaskbarYAnchorPercent = y;
        return true;
    }

    private static double ToPercent(double offset, double travel, double fallback) =>
        travel > 0 ? Math.Round(100 * offset / travel, 2) : fallback;
}
