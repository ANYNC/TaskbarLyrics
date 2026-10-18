using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class SmartTopmostPolicyTests
{
    private static readonly NativeRect PrimaryMonitor = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DesktopFollowsShellPlacementAndReassertsOrderWithoutDependingOnFullscreen(bool shellTopmost)
    {
        var placement = new SmartTopmostDecision(shellTopmost, new IntPtr(73));
        var result = SmartTopmostPolicy.Evaluate(FloatingWindowLayer.Desktop, PrimaryMonitor,
            CreateForegroundWindow(PrimaryMonitor), placement);
        Assert.Equal(placement.ShouldBeTopmost, result.ShouldBeTopmost);
        Assert.Equal(placement.WindowToPlaceAfter, result.WindowToPlaceAfter);
        Assert.True(SmartTopmostPolicy.RequiresTopmostStateUpdate(result, result));
    }

    [Fact]
    public void FullscreenOnAnotherMonitorDoesNotLowerWindowLevel()
    {
        var result = SmartTopmostPolicy.Evaluate(FloatingWindowLayer.Window, PrimaryMonitor,
            CreateForegroundWindow(new NativeRect(-2560, 0, 0, 1440)));
        Assert.True(result.ShouldBeTopmost);
    }

    [Fact]
    public void EvaluateWhenForceAlwaysOnTopIsEnabledKeepsLyricsTopmost()
    {
        var foreground = CreateForegroundWindow(new NativeRect(0, 0, 1920, 1080));

        var result = SmartTopmostPolicy.Evaluate(FloatingWindowLayer.Fullscreen, PrimaryMonitor, foreground);

        Assert.True(result.ShouldBeTopmost);
        Assert.Equal(IntPtr.Zero, result.WindowToPlaceAfter);
    }

    [Fact]
    public void EvaluateWhenEligibleForegroundWindowCoversTargetMonitorYieldsToForegroundWindow()
    {
        var handle = new IntPtr(42);
        var foreground = CreateForegroundWindow(new NativeRect(0, 0, 1920, 1080), handle);

        var result = SmartTopmostPolicy.Evaluate(FloatingWindowLayer.Window, PrimaryMonitor, foreground);

        Assert.False(result.ShouldBeTopmost);
        Assert.Equal(handle, result.WindowToPlaceAfter);
    }

    [Theory]
    [InlineData(1, 0, 1920, 1080)]
    [InlineData(0, 1, 1920, 1080)]
    [InlineData(0, 0, 1919, 1080)]
    [InlineData(0, 0, 1920, 1079)]
    public void EvaluateWhenForegroundWindowDoesNotCoverTargetMonitorKeepsLyricsTopmost(
        int left,
        int top,
        int right,
        int bottom)
    {
        var foreground = CreateForegroundWindow(new NativeRect(left, top, right, bottom));

        var result = SmartTopmostPolicy.Evaluate(FloatingWindowLayer.Window, PrimaryMonitor, foreground);

        Assert.True(result.ShouldBeTopmost);
        Assert.Equal(IntPtr.Zero, result.WindowToPlaceAfter);
    }

    [Fact]
    public void EvaluateWhenForegroundWindowIsNotEligibleKeepsLyricsTopmost()
    {
        var foreground = CreateForegroundWindow(
            new NativeRect(0, 0, 1920, 1080),
            isDesktopOrShell: true);

        var result = SmartTopmostPolicy.Evaluate(FloatingWindowLayer.Window, PrimaryMonitor, foreground);

        Assert.True(result.ShouldBeTopmost);
        Assert.Equal(IntPtr.Zero, result.WindowToPlaceAfter);
    }

    [Fact]
    public void EvaluateSupportsNegativeCoordinateTargetMonitor()
    {
        var targetMonitor = new NativeRect(-2560, 0, 0, 1440);
        var handle = new IntPtr(99);
        var foreground = CreateForegroundWindow(targetMonitor, handle);

        var result = SmartTopmostPolicy.Evaluate(FloatingWindowLayer.Window, targetMonitor, foreground);

        Assert.False(result.ShouldBeTopmost);
        Assert.Equal(handle, result.WindowToPlaceAfter);
    }

    [Fact]
    public void RequiresTopmostStateUpdateWhenTopmostDecisionIsUnchangedReassertsTopmost()
    {
        var decision = new SmartTopmostDecision(true, IntPtr.Zero);

        var result = SmartTopmostPolicy.RequiresTopmostStateUpdate(decision, decision);

        Assert.True(result);
    }

    [Fact]
    public void RequiresTopmostStateUpdateWhenYieldDecisionIsUnchangedDoesNotReassertTopmost()
    {
        var foregroundHandle = new IntPtr(42);
        var decision = new SmartTopmostDecision(false, foregroundHandle);

        var result = SmartTopmostPolicy.RequiresTopmostStateUpdate(decision, decision);

        Assert.False(result);
    }

    private static SmartTopmostForegroundWindow CreateForegroundWindow(
        NativeRect bounds,
        IntPtr? handle = null,
        bool isDesktopOrShell = false) =>
        new(
            handle ?? new IntPtr(1),
            bounds,
            IsDesktopOrShell: isDesktopOrShell);
}
