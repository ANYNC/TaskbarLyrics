using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class FloatingPanelPlacementTests
{
    [Fact]
    public void GrowthAddsTheGapAndThePanelOnTopOfTheReservedFrame()
    {
        // The 4px frame is part of the collapsed surface, so it is not part of the growth.
        Assert.Equal(60, FloatingPanelPlacement.ResolveGrowth(panelHeight: 56, inset: 4, gap: 4));
    }

    [Fact]
    public void RevealGrowsDownwardsAndKeepsTheLyricsWhereTheyAre()
    {
        var bounds = FloatingPanelPlacement.Expand(
            left: 100, top: 900, width: 400, height: 48,
            panelHeight: 56, inset: 4, gap: 4,
            workAreaTop: 0, workAreaBottom: 1080);

        Assert.Equal(100, bounds.Left);
        Assert.Equal(900, bounds.Top);
        Assert.Equal(400, bounds.Width);
        Assert.Equal(108, bounds.Height);
    }

    [Fact]
    public void RevealNearTheBottomEdgeSlidesUpByTheOverflow()
    {
        // Only 20px left below the surface: the reveal slides the whole window up so the
        // panel fits, which lifts the lyrics for as long as the panel is open.
        var bounds = FloatingPanelPlacement.Expand(
            left: 100, top: 1030, width: 400, height: 48,
            panelHeight: 56, inset: 4, gap: 4,
            workAreaTop: 0, workAreaBottom: 1098);

        Assert.Equal(100, bounds.Left);
        Assert.Equal(990, bounds.Top);
        Assert.Equal(108, bounds.Height);
    }

    [Fact]
    public void RevealNeverLeavesTheTopOfTheWorkArea()
    {
        var bounds = FloatingPanelPlacement.Expand(
            left: 100, top: 40, width: 400, height: 48,
            panelHeight: 56, inset: 4, gap: 4,
            workAreaTop: 0, workAreaBottom: 100);

        Assert.Equal(0, bounds.Top);
        Assert.Equal(108, bounds.Height);
    }

    [Fact]
    public void PartialProgressInterpolatesTowardsTheFullReveal()
    {
        var half = FloatingPanelPlacement.Interpolate(
            left: 100, top: 900, width: 400, height: 48,
            panelHeight: 56, inset: 4, gap: 4, progress: 0.5,
            workAreaTop: 0, workAreaBottom: 1080);

        Assert.Equal(900, half.Top);
        Assert.Equal(78, half.Height);
    }

    [Fact]
    public void ZeroProgressReturnsTheRestingGeometryUntouched()
    {
        var bounds = FloatingPanelPlacement.Interpolate(
            left: 100, top: 900, width: 400, height: 48,
            panelHeight: 56, inset: 4, gap: 4, progress: 0,
            workAreaTop: 0, workAreaBottom: 1080);

        Assert.Equal(100, bounds.Left);
        Assert.Equal(900, bounds.Top);
        Assert.Equal(400, bounds.Width);
        Assert.Equal(48, bounds.Height);
    }

    [Fact]
    public void FullProgressLandsOnTheSameBoundsAsAnInstantReveal()
    {
        var animated = FloatingPanelPlacement.Interpolate(
            left: 100, top: 900, width: 400, height: 48,
            panelHeight: 56, inset: 4, gap: 4, progress: 1,
            workAreaTop: 0, workAreaBottom: 1080);
        var direct = FloatingPanelPlacement.Expand(
            left: 100, top: 900, width: 400, height: 48,
            panelHeight: 56, inset: 4, gap: 4,
            workAreaTop: 0, workAreaBottom: 1080);

        Assert.Equal(direct, animated);
    }
}
