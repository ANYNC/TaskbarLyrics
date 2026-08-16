using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class TaskbarPlacementServiceTests
{
    [Fact]
    public void ConvertPhysicalDisplayMetricsPreservesNegativeCoordinatesAtMixedDpi()
    {
        var monitor = new NativeRect(-2560, -200, 0, 1240);
        var workArea = new NativeRect(-2560, -200, 0, 1168);

        var result = TaskbarPlacementService.ConvertPhysicalDisplayMetrics(monitor, workArea, 2);

        Assert.Equal(-1280, result.Left);
        Assert.Equal(-100, result.Top);
        Assert.Equal(1280, result.Width);
        Assert.Equal(720, result.Height);
        Assert.Equal(684, result.WorkAreaHeight);
    }

    [Theory]
    [InlineData(0, 0, 1920, 1032, 0, 1032, 1920, 48)]
    [InlineData(0, 48, 1920, 1032, 0, 0, 1920, 48)]
    [InlineData(48, 0, 1872, 1080, 0, 0, 48, 1080)]
    [InlineData(0, 0, 1872, 1080, 1872, 0, 48, 1080)]
    public void ResolveTaskbarBoundsSupportsEveryTaskbarEdge(
        double workLeft,
        double workTop,
        double workWidth,
        double workHeight,
        double expectedLeft,
        double expectedTop,
        double expectedWidth,
        double expectedHeight)
    {
        var display = new TaskbarDisplayMetrics(
            0,
            0,
            1920,
            1080,
            workLeft,
            workTop,
            workWidth,
            workHeight);

        var result = TaskbarPlacementService.ResolveTaskbarBounds(display);

        Assert.Equal(expectedLeft, result.Left);
        Assert.Equal(expectedTop, result.Top);
        Assert.Equal(expectedWidth, result.Width);
        Assert.Equal(expectedHeight, result.Height);
    }
}
