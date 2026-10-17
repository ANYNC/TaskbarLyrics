using System.IO;
using System.Windows;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class FloatingWindowPositionTests
{
    [Fact]
    public void ResetClearsAllMonitorPositionsAndPersistsTheEmptyProfile()
    {
        using var files = new PositionFiles();
        var store = new FloatingWindowPositionStore(files.Path);
        store.Remember("primary", new FloatingWindowPosition("a", 0.2, 0.5));
        store.Remember("mirror", new FloatingWindowPosition("b", 0.8, 0.1));
        store.Clear();
        var loaded = new FloatingWindowPositionStore(files.Path);
        Assert.Null(loaded.Get("primary"));
        Assert.Null(loaded.Get("mirror"));
        Assert.Single(Directory.GetFiles(files.Directory));
    }

    [Fact]
    public void MissingPositionFileKeepsExistingDefaultAndDoesNotCreateData()
    {
        using var files = new PositionFiles();
        Assert.Null(new FloatingWindowPositionStore(files.Path).Get("primary"));
        Assert.False(File.Exists(files.Path));
    }

    [Fact]
    public void PositionsRoundTripIndependentlyByWindowAndLeaveNoTemporaryFiles()
    {
        using var files = new PositionFiles();
        var store = new FloatingWindowPositionStore(files.Path);
        var first = new FloatingWindowPosition("monitor-A", 0.2, 0.75);
        var second = new FloatingWindowPosition("monitor-B", 0.9, 0.1);
        store.Remember("primary", first);
        store.Remember("mirror", second);
        var loaded = new FloatingWindowPositionStore(files.Path);
        Assert.Equal(first, loaded.Get("PRIMARY"));
        Assert.Equal(second, loaded.Get("mirror"));
        loaded.Forget("primary");
        var afterReset = new FloatingWindowPositionStore(files.Path);
        Assert.Null(afterReset.Get("primary"));
        Assert.Equal(second, afterReset.Get("mirror"));
        Assert.Single(Directory.GetFiles(files.Directory));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"Version\":2,\"Positions\":{}}")]
    [InlineData("{\"Version\":1,\"Positions\":null}")]
    [InlineData("{\"Version\":1,\"Positions\":{\"a\":{\"DisplayId\":\"m\",\"X\":2,\"Y\":0}}}")]
    [InlineData("{\"Version\":1,\"Positions\":{\"a\":null}}")]
    public void UnreadableOrUnsupportedDataIsPreservedAfterDragging(string json)
    {
        using var files = new PositionFiles();
        File.WriteAllText(files.Path, json);
        var store = new FloatingWindowPositionStore(files.Path);
        Assert.Null(store.Get("a"));
        store.Remember("a", new FloatingWindowPosition("monitor", 0.5, 0.5));
        store.Clear();
        Assert.Equal(json, File.ReadAllText(files.Path));
    }

    [Fact]
    public void InvalidCoordinatesAreIgnoredAndFailedWritesDoNotThrowOrLeaveTemporaryFiles()
    {
        using var files = new PositionFiles();
        var store = new FloatingWindowPositionStore(files.Path);
        store.Remember("a", new FloatingWindowPosition("monitor", double.NaN, 0));
        store.Remember("b", new FloatingWindowPosition("monitor", 0, -1));
        Assert.False(File.Exists(files.Path));
        Directory.CreateDirectory(files.Path); // A directory cannot be replaced by the saved file.
        var valid = new FloatingWindowPosition("monitor", 0.5, 0.5);
        store.Remember("a", valid);
        Assert.Equal(valid, store.Get("a"));
        Assert.Empty(Directory.GetFiles(files.Directory));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void NormalizedPositionsRoundTripOnNegativeCoordinateDisplaysAndScaleWithWorkArea(double scale)
    {
        var display = Monitor("negative", new NativeRect(-1920, -120, 0, 960), scale);
        var bounds = new Rect(-1800, 420, 420 * scale, 44 * scale);
        var position = FloatingWindowPlacement.Capture(bounds, display);
        var restored = FloatingWindowPlacement.RestoreBounds(position, display.WorkArea, bounds.Size);
        Assert.Equal(bounds.Left, restored.Left, 6);
        Assert.Equal(bounds.Top, restored.Top, 6);
        var bottomRight = FloatingWindowPlacement.RestoreBounds(new FloatingWindowPosition("negative", 1, 1),
            new NativeRect(-2560, -200, 0, 1200), new Size(420 * scale, 44 * scale));
        Assert.Equal(0, bottomRight.Right);
        Assert.Equal(1200, bottomRight.Bottom);
    }

    [Fact]
    public void OffScreenAndOversizedWindowsRecoverInsideAnAvailableDisplay()
    {
        var left = Monitor("left", new NativeRect(-1920, 0, 0, 1080), 1);
        var right = Monitor("right", new NativeRect(0, 0, 1920, 1080), 1.5);
        Assert.Equal(right, FloatingWindowPlacement.Nearest(new Rect(1700, 100, 420, 44), [left, right]));
        Assert.Equal(left, FloatingWindowPlacement.Nearest(new Rect(-3000, 100, 420, 44), [left, right]));
        Assert.Equal(new Rect(-1920, 1036, 420, 44),
            FloatingWindowPlacement.Clamp(new Rect(-3000, 1400, 420, 44), left.WorkArea));
        Assert.Equal(new Rect(-1920, 0, 3000, 1400),
            FloatingWindowPlacement.Clamp(new Rect(200, 100, 3000, 1400), left.WorkArea));
    }

    private static DisplayMonitor Monitor(string id, NativeRect work, double scale) => new(id, id, false, work, work, scale);

    [Fact]
    public void SavedTargetMonitorIsRestoredOrFallsBackWithoutRewritingItsIdentity()
    {
        var home = Monitor("home", new NativeRect(0, 0, 1920, 1080), 1);
        var other = Monitor("other", new NativeRect(-2560, -100, 0, 1340), 1.5);
        var saved = new FloatingWindowPosition("OTHER", 0.5, 0.25);
        Assert.Equal(other, FloatingWindowPlacement.RestoreDisplay(saved, home, [home, other]));
        Assert.Equal(home, FloatingWindowPlacement.RestoreDisplay(saved, home, [home]));
        Assert.Equal("OTHER", saved.DisplayId);
    }

    private sealed class PositionFiles : IDisposable
    {
        internal string Directory { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, $"floating-positions-{Guid.NewGuid():N}");
        internal string Path => System.IO.Path.Combine(Directory, "positions.json");
        internal PositionFiles() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
