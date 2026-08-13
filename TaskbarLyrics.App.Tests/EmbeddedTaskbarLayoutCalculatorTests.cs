using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class EmbeddedTaskbarLayoutCalculatorTests
{
    [Theory]
    [InlineData(EmbeddedTaskbarHorizontalAnchor.Left, 0, 0)]
    [InlineData(EmbeddedTaskbarHorizontalAnchor.Left, 40, 40)]
    [InlineData(EmbeddedTaskbarHorizontalAnchor.Center, 0, 190)]
    [InlineData(EmbeddedTaskbarHorizontalAnchor.Right, 0, 380)]
    public void CalculateHorizontalLeftFollowsAnchor(
        EmbeddedTaskbarHorizontalAnchor anchor,
        double offset,
        double expected)
    {
        var result = EmbeddedTaskbarLayoutCalculator.CalculateHorizontalLeft(
            420,
            40,
            anchor,
            offset);

        Assert.Equal(expected, result, 10);
    }

    [Fact]
    public void CalculateVerticalTopCentersWindowInTaskbar()
    {
        Assert.Equal(20, EmbeddedTaskbarLayoutCalculator.CalculateVerticalTop(80, 40, 0));
        Assert.Equal(25, EmbeddedTaskbarLayoutCalculator.CalculateVerticalTop(80, 40, 5));
        Assert.Equal(15, EmbeddedTaskbarLayoutCalculator.CalculateVerticalTop(80, 40, -5));
    }

    [Fact]
    public void ToTaskbarClientBoundsConvertsLogicalToPhysicalPixels()
    {
        var bounds = EmbeddedTaskbarLayoutCalculator.ToTaskbarClientBounds(10, 20, 320, 40, 1.5);

        Assert.Equal(15, bounds.Left);
        Assert.Equal(30, bounds.Top);
        Assert.Equal(480, bounds.Width);
        Assert.Equal(60, bounds.Height);
    }

    [Fact]
    public void ToTaskbarClientBoundsTreatsInvalidScaleAsOne()
    {
        var bounds = EmbeddedTaskbarLayoutCalculator.ToTaskbarClientBounds(10, 20, 100, 40, 0);

        Assert.Equal(10, bounds.Left);
        Assert.Equal(20, bounds.Top);
        Assert.Equal(100, bounds.Width);
    }

    [Fact]
    public void ClampEmbeddedTaskbarWidthConstrainsToWindowRange()
    {
        Assert.Equal(320, AppSettings.ClampEmbeddedTaskbarWidth(100));
        Assert.Equal(1400, AppSettings.ClampEmbeddedTaskbarWidth(5000));
        Assert.Equal(500, AppSettings.ClampEmbeddedTaskbarWidth(500));
    }

    [Fact]
    public void ClampEmbeddedTaskbarOffsetConstrainsToOffsetRange()
    {
        Assert.Equal(-2000, AppSettings.ClampEmbeddedTaskbarOffset(-9999));
        Assert.Equal(2000, AppSettings.ClampEmbeddedTaskbarOffset(9999));
        Assert.Equal(0, AppSettings.ClampEmbeddedTaskbarOffset(0));
    }
}
