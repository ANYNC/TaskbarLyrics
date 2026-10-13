using System.Windows;

namespace TaskbarLyrics.App;

internal interface IFloatingPanelMaterial : IDisposable
{
    IntPtr Handle { get; }
    void Position(Rect cardBounds, double scale, IntPtr lyricsHandle);
    void Configure(bool light, double brightness, double tintOpacity, double blurAmount);
    void SetVisible(bool visible, bool immediate = false);
}
