namespace TaskbarLyrics.App;

/// <summary>Window bounds of an expanded floating surface, in DIP.</summary>
internal readonly record struct FloatingPanelBounds(double Left, double Top, double Width, double Height);

/// <summary>
/// Geometry of the floating control panel. The collapsed surface already reserves
/// <see cref="FrameInset"/> on every side, so a reveal only adds the gap and the panel.
/// The panel always opens downwards; when the work area leaves no room below, the whole
/// surface slides up so the panel stays visible, and the lyrics come back down with it
/// when the panel retracts.
/// </summary>
internal static class FloatingPanelPlacement
{
    /// <summary>Frame the collapsed floating surface reserves for its control panel, in DIP.</summary>
    internal const double FrameInset = 4;

    /// <summary>Extra surface the panel needs: the gap between lyrics and panel, plus the panel.</summary>
    internal static double ResolveGrowth(double panelHeight, double inset, double gap) =>
        Math.Max(0, gap + panelHeight);

    internal static FloatingPanelBounds Expand(
        double left,
        double top,
        double width,
        double height,
        double panelHeight,
        double inset,
        double gap,
        double workAreaTop,
        double workAreaBottom) =>
        Interpolate(
            left,
            top,
            width,
            height,
            panelHeight,
            inset,
            gap,
            progress: 1,
            workAreaTop,
            workAreaBottom);

    /// <summary>
    /// Bounds for a partially revealed panel. Progress zero returns the collapsed bounds
    /// verbatim, so the animation both starts and ends on the exact resting geometry.
    /// </summary>
    internal static FloatingPanelBounds Interpolate(
        double left,
        double top,
        double width,
        double height,
        double panelHeight,
        double inset,
        double gap,
        double progress,
        double workAreaTop,
        double workAreaBottom)
    {
        if (panelHeight <= 0 || progress <= 0)
        {
            return new FloatingPanelBounds(left, top, width, height);
        }

        var growth = ResolveGrowth(panelHeight, inset, gap) * Math.Min(1, progress);
        var expandedHeight = height + growth;
        // The panel is revealed below the lyrics. If the surface would leave the work area,
        // it slides up by exactly the overflow: the lyrics rise while the panel fits, then
        // drop back when the panel retracts.
        var lowestTop = Math.Max(workAreaTop, workAreaBottom - expandedHeight);
        return new FloatingPanelBounds(left, Math.Min(top, lowestTop), width, expandedHeight);
    }
}
