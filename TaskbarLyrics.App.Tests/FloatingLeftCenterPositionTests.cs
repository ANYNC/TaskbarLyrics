using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Theory]
    [InlineData("bottom", 1)]
    [InlineData("top", 1.25)]
    [InlineData("left", 1.5)]
    [InlineData("right", 2)]
    public void FloatingDefaultSitsAtTheScreenLeftCenter(string edge, double scale)
    {
        RunSta(() =>
        {
            var window = CreateOwner();
            try
            {
                var screen = new NativeRect(-1920, -120, 0, 960);
                var work = edge switch
                {
                    "top" => screen with { Top = screen.Top + 60 },
                    "left" => screen with { Left = screen.Left + 60 },
                    "right" => screen with { Right = screen.Right - 60 },
                    _ => screen with { Bottom = screen.Bottom - 60 }
                };
                var display = new DisplayMonitor("test", "test", false, screen, work, scale);
                TaskbarPlacementService.Anchor(window, new AppSettings
                {
                    UseFloatingWindow = true,
                    HorizontalAnchor = LyricsHorizontalAnchor.Right,
                    XOffset = 100,
                    YOffset = 80
                }, display);
                var bounds = FloatingLyricsPanelController.GetBounds(window);
                Assert.Equal(screen.Left, bounds.Left);
                Assert.InRange(Math.Abs(bounds.Top + bounds.Height / 2 - (screen.Top + screen.Height / 2.0)), 0, 0.5);
            }
            finally { window.Close(); }
        });
    }
}
