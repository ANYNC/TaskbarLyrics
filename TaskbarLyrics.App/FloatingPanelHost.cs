using System.Windows;

namespace TaskbarLyrics.App;

/// <summary>Arguments for a control-panel request raised by a lyrics surface.</summary>
internal sealed class FloatingPanelActionRequestedEventArgs(string action, TimeSpan? position = null) : EventArgs
{
    internal string Action { get; } = action;

    internal TimeSpan? Position { get; } = position;
}

/// <summary>
/// Owns the transient geometry of a floating lyrics window while its control panel is
/// open. The lyrics keep their screen position, so the host window only grows; the
/// pre-expansion bounds are restored verbatim when the panel closes.
/// </summary>
internal sealed class FloatingPanelHost(Window window)
{
    private Rect? _baseBounds;
    private (double PanelHeight, double Inset, double Gap, bool Above)? _expandRequest;

    internal bool IsExpanded => _baseBounds is not null;

    /// <summary>
    /// Grows the window around the panel. <paramref name="above"/> reports whether the
    /// panel opens upwards, which the lyrics surface mirrors in its own layout.
    /// </summary>
    internal bool TryExpand(
        DisplayMonitor? display,
        double panelHeight,
        double inset,
        double gap,
        bool above,
        out bool applied)
    {
        applied = false;
        if (panelHeight <= 0 || inset < 0 || gap < 0)
        {
            return false;
        }

        // Re-requesting the same expansion must not resize again: a redundant resize can
        // raise a fresh layout notification and feed an expand/resize loop.
        if (_baseBounds is not null && _expandRequest == (panelHeight, inset, gap, above))
        {
            return false;
        }

        _baseBounds ??= new Rect(window.Left, window.Top, window.Width, window.Height);
        _expandRequest = (panelHeight, inset, gap, above);
        ApplyExpansion(display, out applied);
        return true;
    }

    /// <summary>
    /// Restores the pre-expansion bounds. The caller re-anchors afterwards, because
    /// re-anchors that arrived while the panel was open were deferred.
    /// </summary>
    internal bool Collapse()
    {
        if (_baseBounds is not { } baseBounds)
        {
            return false;
        }

        _baseBounds = null;
        _expandRequest = null;
        ApplyBounds(new FloatingPanelBounds(baseBounds.Left, baseBounds.Top, baseBounds.Width, baseBounds.Height, Above: false));
        return true;
    }

    private void ApplyExpansion(DisplayMonitor? display, out bool above)
    {
        var baseBounds = _baseBounds!.Value;
        var request = _expandRequest!.Value;
        above = request.Above;
        var (workAreaTop, workAreaBottom) = GetWorkArea(display);
        var bounds = FloatingPanelPlacement.Expand(
            baseBounds.Left,
            baseBounds.Top,
            baseBounds.Width,
            baseBounds.Height,
            request.PanelHeight,
            request.Inset,
            request.Gap,
            above,
            workAreaTop,
            workAreaBottom);
        above = bounds.Above;
        ApplyBounds(bounds);
    }

    /// <summary>
    /// Applies the new geometry in a single native move. Setting the WPF properties one
    /// by one would issue several <c>SetWindowPos</c> calls, and the intermediate states
    /// (moved but not resized) are what make the surface visibly jump.
    /// </summary>
    private void ApplyBounds(FloatingPanelBounds bounds)
    {
        TaskbarPlacementService.ApplyLogicalBounds(
            window,
            new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height),
            GetPixelsPerDip());
    }

    private double GetPixelsPerDip()
    {
        var scale = TaskbarPlacementService.GetPixelsPerDip(window);
        return scale > 0 && double.IsFinite(scale) ? scale : 1;
    }

    private static (double Top, double Bottom) GetWorkArea(DisplayMonitor? display)
    {
        if (display is null)
        {
            var workArea = SystemParameters.WorkArea;
            return (workArea.Top, workArea.Bottom);
        }

        var metrics = TaskbarPlacementService.ConvertPhysicalDisplayMetrics(
            display.Bounds,
            display.WorkArea,
            display.PixelsPerDip);
        return (metrics.Top, metrics.Bottom);
    }
}
