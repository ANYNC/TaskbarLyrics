using System.Windows;

namespace TaskbarLyrics.App;

internal readonly record struct FloatingLyricsPanelPlacement(Rect Bounds, bool Above)
{
    internal static FloatingLyricsPanelPlacement Place(Rect lyrics, Rect workArea, double pixelsPerDip)
    {
        var height = (lyrics.Width / pixelsPerDip < 220 ? 136 : 104) * pixelsPerDip;
        var below = Math.Max(0, workArea.Bottom - lyrics.Bottom);
        var above = Math.Max(0, lyrics.Top - workArea.Top);
        var opensAbove = below < height && above > below;
        height = Math.Min(height, opensAbove ? above : below);
        return new(new Rect(lyrics.Left, opensAbove ? lyrics.Top - height : lyrics.Bottom,
            lyrics.Width, height), opensAbove);
    }
}
