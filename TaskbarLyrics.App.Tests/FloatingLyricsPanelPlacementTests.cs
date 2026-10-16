using System.Windows;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class FloatingLyricsPanelPlacementTests
{
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 1.5)]
    public void DraggingKeepsTheOpeningDirectionAndFullControlHeight(bool above, double scale)
    {
        var lyrics = new Rect(-1000, -20, 420 * scale, 44 * scale);
        var placement = FloatingLyricsPanelPlacement.Follow(lyrics, above, scale);
        Assert.Equal(above, placement.Above);
        Assert.Equal(96 * scale, placement.Bounds.Height);
        Assert.Equal(above ? lyrics.Top : lyrics.Bottom, above ? placement.Bounds.Bottom : placement.Bounds.Top);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void OpensBelowWithoutMovingOrChangingLyricsWidth(double scale)
    {
        var lyrics = new Rect(100, 100, 420 * scale, 40 * scale);
        var placed = FloatingLyricsPanelPlacement.Place(lyrics, new Rect(0, 0, 1920, 1080), scale);
        Assert.False(placed.Above);
        Assert.Equal(lyrics.Bottom, placed.Bounds.Top);
        Assert.Equal(lyrics.Left, placed.Bounds.Left);
        Assert.Equal(lyrics.Width, placed.Bounds.Width);
        Assert.Equal(96 * scale, placed.Bounds.Height);
    }

    [Fact]
    public void OpensAboveNearBottomOfNegativeCoordinateDisplay()
    {
        var lyrics = new Rect(-1800, 920, 630, 60);
        var placed = FloatingLyricsPanelPlacement.Place(lyrics, new Rect(-1920, 0, 1920, 1000), 1.5);
        Assert.True(placed.Above);
        Assert.Equal(lyrics.Top, placed.Bounds.Bottom);
        Assert.Equal(lyrics.Left, placed.Bounds.Left);
        Assert.Equal(144, placed.Bounds.Height);
    }

    [Fact]
    public void LimitsExpansionToAvailableSpace()
    {
        var placed = FloatingLyricsPanelPlacement.Place(new Rect(0, 40, 420, 100), new Rect(0, 0, 800, 200), 1);
        Assert.False(placed.Above);
        Assert.Equal(60, placed.Bounds.Height);
    }

    [Fact]
    public void NarrowLyricsUseMoreHeightWithoutChangingTheirWidth()
    {
        var placed = FloatingLyricsPanelPlacement.Place(new Rect(100, 100, 150, 40), new Rect(0, 0, 1920, 1080), 1);
        Assert.Equal(150, placed.Bounds.Width);
        Assert.Equal(128, placed.Bounds.Height);
    }
}
