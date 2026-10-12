namespace TaskbarLyrics.App;

/// <summary>Window bounds of an expanded floating surface, in DIP.</summary>
internal readonly record struct FloatingPanelBounds(double Left, double Top, double Width, double Height, bool Above);

/// <summary>
/// Geometry of the floating control panel. The lyrics keep their on-screen position,
/// so the host window grows downwards when the work area allows it and upwards
/// otherwise.
/// </summary>
internal static class FloatingPanelPlacement
{
    /// <summary>
    /// Extra surface the panel needs: the frame inset above the lyrics (or above the
    /// panel when it opens upwards), the gap, and the panel itself. There is
    /// deliberately no bottom inset, so the lyrics keep their exact position.
    /// </summary>
    internal static double ResolveGrowth(double panelHeight, double inset, double gap) =>
        Math.Max(0, inset + gap + panelHeight);

    internal static FloatingPanelBounds Expand(
        double left,
        double top,
        double width,
        double height,
        double panelHeight,
        double inset,
        double gap,
        bool above,
        double workAreaTop,
        double workAreaBottom)
    {
        if (panelHeight <= 0)
        {
            return new FloatingPanelBounds(left, top, width, height, Above: false);
        }

        var growth = ResolveGrowth(panelHeight, inset, gap);
        var expandedLeft = left - inset;
        var expandedWidth = width + (2 * inset);
        if (above)
        {
            var expandedTop = Math.Max(workAreaTop, top - growth);
            return new FloatingPanelBounds(expandedLeft, expandedTop, expandedWidth, height + growth, Above: true);
        }

        // The lyrics stay put: the surface only gains room above them and below them.
        var downwardTop = Math.Min(top - inset, Math.Max(workAreaTop, workAreaBottom - (height + growth)));
        return new FloatingPanelBounds(expandedLeft, downwardTop, expandedWidth, height + growth, Above: false);
    }
}
