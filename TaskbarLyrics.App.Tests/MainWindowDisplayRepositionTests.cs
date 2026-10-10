using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class MainWindowDisplayRepositionTests
{
    private static readonly DisplayMonitor Display = new(
        "DISPLAY1", "Primary", true, new NativeRect(0, 0, 1920, 1080),
        new NativeRect(0, 0, 1920, 1032), 1);

    [Fact]
    public void UnchangedDisplayDoesNotRepositionOrCloseTheControlPanel()
    {
        var refreshed = Display with { Name = "Renamed", Id = "display1" };

        Assert.False(MainWindow.RequiresDisplayReposition(Display, refreshed));
    }

    [Fact]
    public void WorkAreaOrDpiChangeRequiresReposition()
    {
        Assert.True(MainWindow.RequiresDisplayReposition(Display,
            Display with { WorkArea = new NativeRect(0, 0, 1920, 1000) }));
        Assert.True(MainWindow.RequiresDisplayReposition(Display,
            Display with { PixelsPerDip = 1.5 }));
    }
}
