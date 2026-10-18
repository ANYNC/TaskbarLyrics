using System.Windows;

namespace TaskbarLyrics.App;

internal readonly record struct FloatingLyricsPanelPlacement(Rect Bounds, bool Above)
{
    internal static double GetHeight(double widthInDips, bool playbackControls = true) => !playbackControls ? 56 : widthInDips < 220 ? 128 : 96;

    internal static FloatingLyricsPanelPlacement Follow(Rect lyrics, bool above, double pixelsPerDip, bool playbackControls = true)
    {
        var height = GetHeight(lyrics.Width / pixelsPerDip, playbackControls) * pixelsPerDip;
        return new(new Rect(lyrics.Left, above ? lyrics.Top - height : lyrics.Bottom, lyrics.Width, height), above);
    }

    internal static FloatingLyricsPanelPlacement Place(Rect lyrics, Rect workArea, double pixelsPerDip, bool playbackControls = true)
    {
        var height = GetHeight(lyrics.Width / pixelsPerDip, playbackControls) * pixelsPerDip;
        var below = Math.Max(0, workArea.Bottom - lyrics.Bottom);
        var above = Math.Max(0, lyrics.Top - workArea.Top);
        var opensAbove = below < height && above > below;
        height = Math.Min(height, opensAbove ? above : below);
        return new(new Rect(lyrics.Left, opensAbove ? lyrics.Top - height : lyrics.Bottom,
            lyrics.Width, height), opensAbove);
    }
}
