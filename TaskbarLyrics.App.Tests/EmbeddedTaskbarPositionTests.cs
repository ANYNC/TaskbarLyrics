using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class EmbeddedTaskbarPositionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(100)]
    public void AnchorsUseAvailableTravelOnBothTaskbarOrientations(double percent)
    {
        var settings = new AppSettings { TaskbarXAnchorPercent = percent, TaskbarYAnchorPercent = percent };
        var horizontalBar = new TaskbarBounds(0, 0, 1400, 64);
        var verticalBar = new TaskbarBounds(0, 0, 64, 1100);
        var horizontal = EmbeddedTaskbarPosition.Calculate(settings, horizontalBar, 1.25);
        var vertical = EmbeddedTaskbarPosition.Calculate(settings, verticalBar, 1.25);

        Assert.Equal((horizontalBar.Width - horizontal.Width) * percent / 100, horizontal.Left, 6);
        Assert.Equal((horizontalBar.Height - horizontal.Height) * percent / 100, horizontal.Top, 6);
        Assert.Equal(0, vertical.Left);
        Assert.Equal((verticalBar.Height - vertical.Height) * percent / 100, vertical.Top, 6);
    }

    [Theory]
    [InlineData(1400, 58, LyricsHorizontalAnchor.Left, 2000, -3)]
    [InlineData(1400, 64, LyricsHorizontalAnchor.Right, -161, 3)]
    [InlineData(1200, 48, LyricsHorizontalAnchor.Center, 74, 3)]
    [InlineData(64, 1100, LyricsHorizontalAnchor.Left, 0, 300)]
    public void MigrationPreservesTheClampedLegacyPlacement(
        double width, double height, LyricsHorizontalAnchor anchor, double xOffset, double yOffset)
    {
        const double dpi = 1.5;
        var taskbar = new TaskbarBounds(0, 0, width, height);
        var settings = new AppSettings { HorizontalAnchor = anchor, XOffset = xOffset, YOffset = yOffset };
        var legacy = EmbeddedTaskbarPosition.Calculate(settings, taskbar, dpi);

        Assert.True(EmbeddedTaskbarPosition.MigrateLegacy(settings, taskbar, dpi));
        var migrated = EmbeddedTaskbarPosition.Calculate(settings, taskbar, dpi);
        Assert.InRange(Math.Abs(legacy.Left - migrated.Left) * dpi, 0, 0.5);
        Assert.InRange(Math.Abs(legacy.Top - migrated.Top) * dpi, 0, 0.5);
        Assert.False(EmbeddedTaskbarPosition.MigrateLegacy(settings, taskbar with { Width = width * 2 }, dpi));
        Assert.Equal(xOffset, settings.XOffset);
        Assert.Equal(yOffset, settings.YOffset);
    }
}
