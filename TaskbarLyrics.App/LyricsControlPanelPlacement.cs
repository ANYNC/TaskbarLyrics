using System.Windows;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace TaskbarLyrics.App;

internal static class LyricsControlPanelPlacement
{
    private const double GapDip = 16;

    internal static Point Place(Rect anchor, Rect workArea, Size panelSize, double pixelsPerDip = 1,
        CoverPosition coverPosition = CoverPosition.Left)
    {
        var alignedX = coverPosition == CoverPosition.Right ? anchor.Right - panelSize.Width : anchor.Left;
        var gap = GapDip * pixelsPerDip;
        var centeredY = anchor.Top + (anchor.Height - panelSize.Height) / 2;
        double x;
        double y;

        if (anchor.Top >= workArea.Bottom)
        {
            x = alignedX;
            y = anchor.Top - panelSize.Height - gap;
        }
        else if (anchor.Bottom <= workArea.Top)
        {
            x = alignedX;
            y = anchor.Bottom + gap;
        }
        else if (anchor.Right <= workArea.Left)
        {
            x = anchor.Right + gap;
            y = centeredY;
        }
        else if (anchor.Left >= workArea.Right)
        {
            x = anchor.Left - panelSize.Width - gap;
            y = centeredY;
        }
        else
        {
            x = alignedX;
            y = anchor.Top - panelSize.Height - gap >= workArea.Top
                ? anchor.Top - panelSize.Height - gap
                : anchor.Bottom + gap;
        }

        var placedY = Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - panelSize.Height));
        var preferredInset = anchor.Top >= workArea.Bottom
            ? Math.Max(0, workArea.Bottom - placedY - panelSize.Height)
            : gap;
        var horizontalInset = Math.Min(preferredInset, Math.Max(0, (workArea.Width - panelSize.Width) / 2));
        var minX = workArea.Left + horizontalInset;
        var maxX = Math.Max(minX, workArea.Right - panelSize.Width - horizontalInset);
        return new Point(Math.Clamp(x, minX, maxX), placedY);
    }
}
