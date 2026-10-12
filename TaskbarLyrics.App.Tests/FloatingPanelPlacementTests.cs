using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class FloatingPanelPlacementTests
{
    [Fact]
    public void GrowthAddsTheFrameInsetTheGapAndThePanel()
    {
        Assert.Equal(64, FloatingPanelPlacement.ResolveGrowth(panelHeight: 56, inset: 4, gap: 4));
    }

    [Fact]
    public void ExpandDownwardsKeepsTheLyricsPosition()
    {
        var bounds = FloatingPanelPlacement.Expand(
            left: 100, top: 900, width: 400, height: 40,
            panelHeight: 56, inset: 4, gap: 4, above: false,
            workAreaTop: 0, workAreaBottom: 1080);

        // The window gains the frame band above the lyrics, so the lyrics themselves do
        // not move: the top only shifts by the inset.
        Assert.Equal(96, bounds.Left);
        Assert.Equal(896, bounds.Top);
        Assert.Equal(408, bounds.Width);
        Assert.Equal(104, bounds.Height);
        Assert.False(bounds.Above);
    }

    [Fact]
    public void ExpandUpwardsKeepsTheLyricsBottom()
    {
        var bounds = FloatingPanelPlacement.Expand(
            left: 100, top: 1030, width: 400, height: 40,
            panelHeight: 56, inset: 4, gap: 4, above: true,
            workAreaTop: 0, workAreaBottom: 1080);

        Assert.Equal(96, bounds.Left);
        Assert.Equal(966, bounds.Top);
        Assert.Equal(408, bounds.Width);
        Assert.Equal(104, bounds.Height);
        Assert.True(bounds.Above);
    }

    [Fact]
    public void ExpandUpwardsClampsToTheWorkAreaTop()
    {
        var bounds = FloatingPanelPlacement.Expand(
            left: 100, top: 30, width: 400, height: 40,
            panelHeight: 56, inset: 4, gap: 4, above: true,
            workAreaTop: 0, workAreaBottom: 1080);

        Assert.Equal(0, bounds.Top);
        Assert.Equal(104, bounds.Height);
        Assert.True(bounds.Above);
    }

    [Fact]
    public void ExpandDownwardsClampsIntoTheWorkArea()
    {
        var bounds = FloatingPanelPlacement.Expand(
            left: 100, top: 1040, width: 400, height: 40,
            panelHeight: 56, inset: 4, gap: 4, above: false,
            workAreaTop: 0, workAreaBottom: 1000);

        Assert.Equal(896, bounds.Top);
        Assert.Equal(104, bounds.Height);
        Assert.False(bounds.Above);
    }

    [Fact]
    public void ExpandWithoutPanelHeightLeavesTheGeometryUntouched()
    {
        var bounds = FloatingPanelPlacement.Expand(
            left: 100, top: 900, width: 400, height: 40,
            panelHeight: 0, inset: 4, gap: 4, above: true,
            workAreaTop: 0, workAreaBottom: 1080);

        Assert.Equal(100, bounds.Left);
        Assert.Equal(900, bounds.Top);
        Assert.Equal(400, bounds.Width);
        Assert.Equal(40, bounds.Height);
        Assert.False(bounds.Above);
    }
}
