using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class FloatingWindowHitSurfaceTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    public void ShouldTrackPointerOnlyForAFloatingSurfaceThatReceivesInput(
        bool useFloatingWindow,
        bool clickThrough,
        bool expected)
    {
        Assert.Equal(
            expected,
            FloatingWindowHitSurface.ShouldTrackPointer(useFloatingWindow, clickThrough));
    }

    [Fact]
    public void ContainsPointIncludesTheTopLeftEdgeAndExcludesTheBottomRightEdge()
    {
        Assert.True(FloatingWindowHitSurface.ContainsPoint(100, 200, 420, 252, 100, 200));
        Assert.True(FloatingWindowHitSurface.ContainsPoint(100, 200, 420, 252, 419, 251));
        Assert.False(FloatingWindowHitSurface.ContainsPoint(100, 200, 420, 252, 420, 251));
        Assert.False(FloatingWindowHitSurface.ContainsPoint(100, 200, 420, 252, 419, 252));
        Assert.False(FloatingWindowHitSurface.ContainsPoint(100, 200, 420, 252, 99, 220));
    }

    [Fact]
    public void ContainsPointHandlesMonitorsLeftOfThePrimaryOrigin()
    {
        // A monitor placed left of the primary one has negative screen coordinates, so the
        // comparison must not be clamped to a positive screen space.
        Assert.True(FloatingWindowHitSurface.ContainsPoint(-1920, 0, -1600, 52, -1700, 26));
        Assert.False(FloatingWindowHitSurface.ContainsPoint(-1920, 0, -1600, 52, 100, 26));
    }

    [Fact]
    public void FillIsTheSmallestAlphaWindowsStillHitTests()
    {
        // Alpha 0 would make the whole surface transparent to mouse input, which is what
        // leaves hover, dragging and the control panel without events.
        Assert.Equal(1, FloatingWindowHitSurface.Fill.Color.A);
    }
}
