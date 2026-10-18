using System.Windows;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class LyricsControlPanelPlacementTests
{
    [Theory]
    [InlineData(980, 1010, 500, 844)]
    [InlineData(-10, 20, 500, 36)]
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

        Assert.Equal(-1896, position.X);
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

    [Theory]
    [InlineData(CoverPosition.Left, 1, 500, 760)]
    [InlineData(CoverPosition.Right, 1, 174, 760)]
    [InlineData(CoverPosition.Left, 1.5, 500, 752)]
    [InlineData(CoverPosition.Right, 1.5, 174, 752)]
    [InlineData(CoverPosition.Left, 2, 500, 744)]
    [InlineData(CoverPosition.Right, 2, 174, 744)]
    public void AlignsCoverEdgeAndScalesGap(CoverPosition coverPosition, double scale,
        double expectedX, double expectedY)
    {
        var anchor = new Rect(500, 900, 34, 34);
        var position = LyricsControlPanelPlacement.Place(
            anchor, new Rect(0, 0, 1200, 900), new Size(360, 124), scale, coverPosition);

        Assert.Equal(expectedX, position.X);
        Assert.Equal(expectedY, position.Y);
    }

    [Theory]
    [InlineData(CoverPosition.Right, 0, 24)]
    [InlineData(CoverPosition.Left, 1180, 816)]
    public void ClampsAlignedPanelToScreenEdges(CoverPosition coverPosition, double anchorX, double expectedX)
    {
        var position = LyricsControlPanelPlacement.Place(
            new Rect(anchorX, 900, 20, 30), new Rect(0, 0, 1200, 900), new Size(360, 124), 1.5, coverPosition);

        Assert.Equal(expectedX, position.X);
        Assert.Equal(752, position.Y);
    }

    [Theory]
    [InlineData(CoverPosition.Left, -1400)]
    [InlineData(CoverPosition.Right, -1726)]
    public void AlignsCoverOnMonitorWithNegativeCoordinates(CoverPosition coverPosition, double expectedX)
    {
        var position = LyricsControlPanelPlacement.Place(
            new Rect(-1400, -10, 34, 30), new Rect(-1920, 30, 1920, 950), new Size(360, 124), 1.5, coverPosition);

        Assert.Equal(expectedX, position.X);
        Assert.Equal(44, position.Y);
    }

    [Theory]
    [InlineData(-30, 24)]
    [InlineData(1200, 816)]
    public void AlignsPanelTopWithVerticalTaskbarCoverAndScalesGap(double anchorX, double expectedX)
    {
        var position = LyricsControlPanelPlacement.Place(
            new Rect(anchorX, 400, 30, 34), new Rect(0, 0, 1200, 900), new Size(360, 124), 1.5);

        Assert.Equal(expectedX, position.X);
        Assert.Equal(400, position.Y);
    }

    [Theory]
    [InlineData(false, 1, 0, 0)]
    [InlineData(true, 1, 0, 0)]
    [InlineData(false, 1.25, 0, 0)]
    [InlineData(true, 1.25, 0, 0)]
    [InlineData(false, 1.5, 0, 0)]
    [InlineData(true, 1.5, 0, 0)]
    [InlineData(false, 2, 0, 0)]
    [InlineData(true, 2, 0, 0)]
    [InlineData(false, 1, -1920, -1080)]
    [InlineData(true, 1.5, -1920, -1080)]
    public void SideTaskbarGapMatchesHorizontalTaskbarWithoutAddingWorkAreaInset(bool right, double scale,
        double originX, double originY)
    {
        var work = new Rect(originX + (right ? 0 : 48 * scale), originY, 1872 * scale, 1080 * scale);
        var cover = new Rect(originX + (right ? 1878 : 8) * scale, originY + 20 * scale, 34 * scale, 34 * scale);
        var size = new Size(360 * scale, 124 * scale);
        var position = LyricsControlPanelPlacement.Place(cover, work, size, scale);
        var horizontalCover = new Rect(work.Left + 400 * scale, work.Bottom, 34 * scale, 34 * scale);
        var horizontalPosition = LyricsControlPanelPlacement.Place(horizontalCover, work, size, scale);
        var verticalGap = horizontalCover.Top - horizontalPosition.Y - size.Height;
        var sideGap = right ? cover.Left - position.X - size.Width : position.X - cover.Right;

        Assert.Equal(cover.Top, position.Y, 6);
        Assert.Equal(16 * scale, sideGap, 6);
        Assert.Equal(verticalGap, sideGap, 6);
    }

    [Theory]
    [InlineData(false, -10, 0)]
    [InlineData(true, -10, 0)]
    [InlineData(false, 1060, 956)]
    [InlineData(true, 1060, 956)]
    public void SideTaskbarTopAlignmentStillKeepsPanelInsideWorkArea(bool right, double coverY, double expectedY)
    {
        var work = new Rect(right ? 0 : 48, 0, 1872, 1080);
        var cover = new Rect(right ? 1878 : 8, coverY, 34, 34);
        var size = new Size(360, 124);
        var position = LyricsControlPanelPlacement.Place(cover, work, size);

        Assert.Equal(expectedY, position.Y);
        Assert.True(work.Contains(new Rect(position, size)));
    }
}
