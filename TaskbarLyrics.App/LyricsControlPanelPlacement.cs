using System.Windows;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace TaskbarLyrics.App;

internal static class LyricsControlPanelPlacement
{
    private const double Gap = 16;

    internal static Point Place(Rect anchor, Rect workArea, Size panelSize, double pixelsPerDip = 1)
    {
        var centeredX = anchor.Left + (anchor.Width - panelSize.Width) / 2;
        var centeredY = anchor.Top + (anchor.Height - panelSize.Height) / 2;
        double x;
        double y;

        if (anchor.Top >= workArea.Bottom)
        {
            x = centeredX;
            y = anchor.Top - panelSize.Height - Gap;
        }
        else if (anchor.Bottom <= workArea.Top)
        {
            x = centeredX;
            y = anchor.Bottom + Gap;
        }
        else if (anchor.Right <= workArea.Left)
        {
            x = anchor.Right + Gap;
            y = centeredY;
        }
        else if (anchor.Left >= workArea.Right)
        {
            x = anchor.Left - panelSize.Width - Gap;
            y = centeredY;
        }
        else
        {
            x = centeredX;
            y = anchor.Top - panelSize.Height - Gap >= workArea.Top
                ? anchor.Top - panelSize.Height - Gap
                : anchor.Bottom + Gap;
        }

        var placedY = Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - panelSize.Height));
        var preferredInset = anchor.Top >= workArea.Bottom
            ? Math.Max(0, workArea.Bottom - placedY - panelSize.Height)
            : 16 * pixelsPerDip;
        var horizontalInset = Math.Min(preferredInset, Math.Max(0, (workArea.Width - panelSize.Width) / 2));
        var minX = workArea.Left + horizontalInset;
        var maxX = Math.Max(minX, workArea.Right - panelSize.Width - horizontalInset);
        return new Point(Math.Clamp(x, minX, maxX), placedY);
    }
}
