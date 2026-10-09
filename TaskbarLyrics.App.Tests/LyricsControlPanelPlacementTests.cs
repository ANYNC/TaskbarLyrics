using System.Windows;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class LyricsControlPanelPlacementTests
{
    [Theory]
    [InlineData(980, 1010, 390, 844)]
    [InlineData(-10, 20, 390, 36)]
    public void PlacesPanelBesideHorizontalTaskbarAndClampsToWorkArea(
        double anchorTop, double anchorBottom, double expectedX, double expectedY)
    {
        var anchor = new Rect(500, anchorTop, 20, anchorBottom - anchorTop);
        var position = LyricsControlPanelPlacement.Place(
            anchor, new Rect(0, 30, 900, 950), new Size(240, 120));

        Assert.Equal(expectedX, position.X);
        Assert.Equal(expectedY, position.Y);
    }

    [Theory]
    [InlineData(0, 980, 16)]
    [InlineData(0, 988, 8)]
    [InlineData(880, 988, 564)]
    public void MatchesHorizontalInsetToBottomTaskbarGap(
        double anchorLeft, double anchorTop, double expectedX)
    {
        var anchor = new Rect(anchorLeft, anchorTop, 20, 30);
        var position = LyricsControlPanelPlacement.Place(
            anchor, new Rect(0, 0, 900, 980), new Size(328, 160));

        var actualInset = anchorLeft == 0 ? position.X : 900 - position.X - 328;
        Assert.Equal(expectedX, position.X);
        Assert.Equal(980 - position.Y - 160, actualInset);
    }

    [Fact]
    public void KeepsInsetOnMonitorWithNegativeCoordinates()
    {
        var position = LyricsControlPanelPlacement.Place(
            new Rect(-1910, 980, 20, 30),
            new Rect(-1920, 0, 1920, 980),
            new Size(328, 160), 1.5);

        Assert.Equal(-1904, position.X);
    }

    [Fact]
    public void KeepsDpiScaledInsetAwayFromBottomTaskbar()
    {
        var position = LyricsControlPanelPlacement.Place(
            new Rect(0, -10, 20, 30),
            new Rect(0, 30, 900, 950),
            new Size(328, 160), 1.5);

        Assert.Equal(24, position.X);
    }
}
