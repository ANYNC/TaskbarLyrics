using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class DisplayMonitorServiceTests
{
    private static readonly DisplayMonitor Primary = CreateDisplay(@"\\.\DISPLAY1", isPrimary: true);
    private static readonly DisplayMonitor Secondary = CreateDisplay(@"\\.\DISPLAY2", isPrimary: false);

    [Fact]
    public void AllModeReturnsEveryConnectedDisplay()
    {
        var result = LyricsDisplayTargetSelector.Select(
            [Primary, Secondary],
            LyricsDisplayMode.All,
            []);

        Assert.Equal([Primary, Secondary], result);
    }

    [Fact]
    public void SelectedModeReturnsOnlyConnectedSelections()
    {
        var result = LyricsDisplayTargetSelector.Select(
            [Primary, Secondary],
            LyricsDisplayMode.Selected,
            [@"\\.\DISPLAY2", @"\\.\DISPLAY9"]);

        Assert.Equal([Secondary], result);
    }

    [Fact]
    public void SelectedModeFallsBackToPrimaryWhenSelectionsAreDisconnected()
    {
        var result = LyricsDisplayTargetSelector.Select(
            [Primary, Secondary],
            LyricsDisplayMode.Selected,
            [@"\\.\DISPLAY9"]);

        Assert.Equal([Primary], result);
    }

    [Fact]
    public void SelectedModeMatchesDisplayIdsCaseInsensitively()
    {
        var result = LyricsDisplayTargetSelector.Select(
            [Primary, Secondary],
            LyricsDisplayMode.Selected,
            [@"\\.\display2"]);

        Assert.Equal([Secondary], result);
    }

    [Fact]
    public void NoConnectedDisplaysReturnsNoTargets()
    {
        var result = LyricsDisplayTargetSelector.Select(
            [],
            LyricsDisplayMode.All,
            []);

        Assert.Empty(result);
    }

    private static DisplayMonitor CreateDisplay(string id, bool isPrimary) =>
        new(
            id,
            id,
            isPrimary,
            new NativeRect(0, 0, 1920, 1080),
            new NativeRect(0, 0, 1920, 1032),
            1);
}
