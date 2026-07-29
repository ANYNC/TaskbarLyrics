using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class LyricsLayoutMetricsTests
{
    [Fact]
    public void CreateScalesContinuousFontAndPixelAlignsLayoutGeometry()
    {
        var settings = new AppSettings
        {
            FontSize = 14,
            CoverSize = 34,
            CoverGap = 8,
            CoverCornerRadius = 6,
            LyricsLayoutScalePercent = 125
        };

        var metrics = LyricsLayoutMetrics.Create(settings);

        Assert.Equal(17.5, metrics.FontSize);
        Assert.Equal(43, metrics.CoverSize);
        Assert.Equal(10, metrics.CoverGap);
        Assert.Equal(8, metrics.CoverCornerRadius);
        Assert.Equal(56, metrics.DesiredWindowHeight);
    }

    [Fact]
    public void CreateDoesNotRewriteOrRoundBaseSettings()
    {
        var settings = new AppSettings
        {
            FontSize = 14.3,
            CoverSize = 34.3,
            LyricsLayoutScalePercent = 100
        };

        var metrics = LyricsLayoutMetrics.Create(settings);

        Assert.Equal(14.3, settings.FontSize);
        Assert.Equal(34.3, settings.CoverSize);
        Assert.Equal(14.3, metrics.FontSize);
        Assert.Equal(34, metrics.CoverSize);
    }

    [Fact]
    public void CreateAlignsGeometryToPhysicalPixelsAtFractionalDpi()
    {
        var settings = new AppSettings
        {
            CoverSize = 34,
            CoverGap = 8,
            CoverCornerRadius = 6,
            LyricsLayoutScalePercent = 125
        };

        var metrics = LyricsLayoutMetrics.Create(settings, pixelsPerDip: 1.25);

        Assert.Equal(53, metrics.CoverSize * 1.25);
        Assert.Equal(13, metrics.CoverGap * 1.25);
        Assert.Equal(9, metrics.CoverCornerRadius * 1.25);
        Assert.Equal(Math.Ceiling(metrics.DesiredWindowHeight * 1.25), metrics.DesiredWindowHeight * 1.25);
    }

    [Fact]
    public void CreateClampsScaleBeforeCalculatingMetrics()
    {
        var settings = new AppSettings
        {
            LyricsLayoutScalePercent = 400
        };

        var metrics = LyricsLayoutMetrics.Create(settings);

        Assert.Equal(300, metrics.ScalePercent);
        Assert.Equal(42, metrics.FontSize);
        Assert.Equal(102, metrics.CoverSize);
    }

    [Fact]
    public void CreateWhenCoverIsHiddenUsesTextHeightAndPreservesCoverMetrics()
    {
        var settings = new AppSettings
        {
            ShowCover = false,
            FontSize = 14,
            CoverSize = 34,
            CoverGap = 8,
            LyricsLayoutScalePercent = 125
        };

        var metrics = LyricsLayoutMetrics.Create(settings);

        Assert.Equal(53, metrics.DesiredWindowHeight);
        Assert.Equal(43, metrics.CoverSize);
        Assert.Equal(10, metrics.CoverGap);
        Assert.Equal(34, settings.CoverSize);
        Assert.Equal(8, settings.CoverGap);
    }

    [Fact]
    public void VerticalPositionKeepsTheSameCenterAwayFromScreenEdges()
    {
        const double anchorCenterY = 600;
        var compactTop = TaskbarPlacementService.CalculateVerticalPosition(anchorCenterY, 44, 1080, -200);
        var enlargedTop = TaskbarPlacementService.CalculateVerticalPosition(anchorCenterY, 132, 1080, -200);

        Assert.Equal(compactTop + 22, enlargedTop + 66);
    }

    [Fact]
    public void PhysicalDisplayMetricsAreConvertedToCurrentWindowDips()
    {
        var monitor = new TaskbarNativeMethods.NativeRect(0, 0, 1920, 1080);
        var workArea = new TaskbarNativeMethods.NativeRect(0, 0, 1920, 1008);

        var metrics = TaskbarPlacementService.ConvertPhysicalDisplayMetrics(monitor, workArea, 1.5);

        Assert.Equal(1280, metrics.Width);
        Assert.Equal(720, metrics.Height);
        Assert.Equal(672, metrics.WorkAreaHeight);
        Assert.Equal(1280, metrics.Right);
        Assert.Equal(720, metrics.Bottom);
    }

    [Theory]
    [InlineData(1, 1394, 420, 44)]
    [InlineData(1.25, 1383, 525, 55)]
    [InlineData(1.5, 1371, 630, 66)]
    [InlineData(2, 1348, 840, 88)]
    public void LogicalWindowBoundsStayWithinScreenAtSupportedDpiScales(
        double pixelsPerDip,
        int expectedTop,
        int expectedWidth,
        int expectedHeight)
    {
        const double physicalScreenHeight = 1440;
        const double logicalWindowHeight = 44;
        var logicalScreenHeight = physicalScreenHeight / pixelsPerDip;
        var logicalTop = TaskbarPlacementService.CalculateVerticalPosition(
            logicalScreenHeight - 24,
            logicalWindowHeight,
            logicalScreenHeight,
            0);

        var bounds = TaskbarPlacementService.ConvertLogicalWindowBounds(
            0,
            logicalTop,
            420,
            logicalWindowHeight,
            pixelsPerDip);

        Assert.Equal(expectedTop, bounds.Top);
        Assert.Equal(expectedWidth, bounds.Width);
        Assert.Equal(expectedHeight, bounds.Height);
        Assert.InRange(bounds.Top + bounds.Height, 1, (int)physicalScreenHeight);
    }
}
