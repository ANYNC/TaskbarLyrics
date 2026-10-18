using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class LyricsRegionVisibilityTests
{
    [Fact]
    public void EntranceWaitsForItsFirstFrameAndExitKeepsSurfaceUntilCompletion()
    {
        RunSta(() =>
        {
            var commands = new List<(int Generation, LyricsRegionVisibilityPhase Phase)>();
            var surface = (Visible: true, Interactive: true);
            using var controller = new LyricsRegionVisibilityController(Dispatcher.CurrentDispatcher,
                (generation, phase) => commands.Add((generation, phase)), (visible, interactive) => surface = (visible, interactive));
            controller.OnDocumentReady();
            Assert.Equal(LyricsRegionVisibilityPhase.Preparing, controller.Phase);
            Assert.Equal((false, false), surface);
            controller.Complete(commands[^1].Generation, true);
            Assert.False(controller.IsInteractive);
            controller.OnFrameDispatched();
            Assert.Equal((true, false), surface);
            controller.Complete(commands[^1].Generation, true);
            Assert.Equal((true, true), surface);
            controller.SetVisible(false);
            Assert.Equal(LyricsRegionVisibilityPhase.Exiting, controller.Phase);
            Assert.Equal((true, false), surface);
            controller.Complete(commands[^1].Generation, false);
            Assert.Equal((false, false), surface);
        });
    }

    [Fact]
    public void PlaybackReturningDuringExitRejectsTheOldCompletionAndWaitsForANewFrame()
    {
        RunSta(() =>
        {
            var commands = new List<(int Generation, LyricsRegionVisibilityPhase Phase)>();
            var surface = (Visible: true, Interactive: true);
            using var controller = new LyricsRegionVisibilityController(Dispatcher.CurrentDispatcher,
                (generation, phase) => commands.Add((generation, phase)), (visible, interactive) => surface = (visible, interactive));
            controller.OnDocumentReady();
            controller.OnFrameDispatched();
            controller.Complete(commands[^1].Generation, true);
            controller.SetVisible(false);
            var exitingGeneration = commands[^1].Generation;
            controller.SetVisible(true);
            controller.Complete(exitingGeneration, false);
            Assert.Equal(LyricsRegionVisibilityPhase.Preparing, controller.Phase);
            Assert.Equal((true, false), surface);
            controller.OnFrameDispatched();
            controller.Complete(exitingGeneration, true);
            Assert.False(controller.IsInteractive);
            controller.Complete(commands[^1].Generation, true);
            Assert.Equal((true, true), surface);
        });
    }

    [Fact]
    public void HiddenOrUnreadyDocumentsDoNotStartAnExitAndRepeatedTargetsDoNotReplay()
    {
        RunSta(() =>
        {
            var commands = new List<(int Generation, LyricsRegionVisibilityPhase Phase)>();
            var visible = true;
            using var controller = new LyricsRegionVisibilityController(Dispatcher.CurrentDispatcher,
                (generation, phase) => commands.Add((generation, phase)), (shown, _) => visible = shown);
            controller.SetVisible(false);
            Assert.False(visible);
            Assert.Empty(commands);
            controller.OnDocumentReady();
            Assert.Equal(LyricsRegionVisibilityPhase.Hidden, commands[^1].Phase);
            controller.OnFrameDispatched();
            controller.SetVisible(false);
            Assert.Single(commands);
            controller.SetVisible(true);
            controller.SetVisible(false);
            Assert.Equal(LyricsRegionVisibilityPhase.Hidden, controller.Phase);
            Assert.False(visible);
            controller.Dispose();
            var count = commands.Count;
            controller.OnDocumentReady();
            controller.OnFrameDispatched();
            Assert.Equal(count, commands.Count);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingWebCompletionFallsBackToTheRequestedVisibility(bool visible)
    {
        RunSta(() =>
        {
            var commands = new List<(int Generation, LyricsRegionVisibilityPhase Phase)>();
            var surface = (Visible: true, Interactive: true);
            using var controller = new LyricsRegionVisibilityController(Dispatcher.CurrentDispatcher,
                (generation, phase) => commands.Add((generation, phase)), (shown, interactive) => surface = (shown, interactive));
            controller.OnDocumentReady();
            controller.OnFrameDispatched();
            if (!visible) controller.SetVisible(false);
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
            Assert.Equal((visible, visible), surface);
            Assert.Equal(visible ? LyricsRegionVisibilityPhase.Visible : LyricsRegionVisibilityPhase.Hidden, commands[^1].Phase);
        });
    }

    [Fact]
    public void AnImmediateCompletionDoesNotLeaveALaterFallbackThatReplaysTheTerminalState()
    {
        RunSta(() =>
        {
            var commands = new List<LyricsRegionVisibilityPhase>();
            LyricsRegionVisibilityController? active = null;
            using var controller = new LyricsRegionVisibilityController(Dispatcher.CurrentDispatcher,
                (generation, phase) =>
                {
                    commands.Add(phase);
                    if (phase == LyricsRegionVisibilityPhase.Entering) active!.Complete(generation, true);
                }, (_, _) => { });
            active = controller;
            controller.OnDocumentReady();
            controller.OnFrameDispatched();
            Assert.True(controller.IsInteractive);
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
            Assert.Equal([LyricsRegionVisibilityPhase.Preparing, LyricsRegionVisibilityPhase.Entering], commands);
        });
    }

    [Theory]
    [InlineData(0, 0, 1920, 1032, "bottom")]
    [InlineData(0, 48, 1920, 1080, "top")]
    [InlineData(48, 0, 1920, 1080, "left")]
    [InlineData(0, 0, 1872, 1080, "right")]
    public void RegionDirectionUsesEachMonitorsActualEdgeIncludingNegativeCoordinatesAndDpi(
        int left, int top, int right, int bottom, string expected)
    {
        var display = new DisplayMonitor("test", "Test", true, new NativeRect(-1920, -200, 0, 880),
            new NativeRect(left - 1920, top - 200, right - 1920, bottom - 200), 1.5);
        var edge = TaskbarPlacementService.GetTaskbarEdge(display);
        Assert.Equal(expected, edge);
        var script = LyricsWebViewScriptFactory.SetRegionVisibility(7, LyricsRegionVisibilityPhase.Exiting, edge);
        Assert.Contains("\"version\":1", script);
        Assert.Contains("\"phase\":\"exiting\"", script);
        Assert.Contains($"\"edge\":\"{expected}\"", script);
    }

    [Theory]
    [InlineData("{\"version\":1,\"type\":\"regionVisibilityCompleted\",\"payload\":{\"generation\":1,\"visible\":true}}", true)]
    [InlineData("{\"version\":1,\"type\":\"regionVisibilityCompleted\",\"payload\":{\"generation\":2,\"visible\":false}}", true)]
    [InlineData("{\"version\":2,\"type\":\"regionVisibilityCompleted\",\"payload\":{\"generation\":1,\"visible\":true}}", false)]
    [InlineData("{\"version\":1,\"type\":\"unknown\",\"payload\":{\"generation\":1,\"visible\":true}}", false)]
    [InlineData("{\"version\":1,\"type\":\"regionVisibilityCompleted\",\"payload\":{\"generation\":-1,\"visible\":true}}", false)]
    [InlineData("{\"version\":1,\"type\":\"regionVisibilityCompleted\",\"payload\":{\"generation\":\"1\",\"visible\":true}}", false)]
    [InlineData("{\"version\":1,\"type\":\"regionVisibilityCompleted\",\"payload\":{\"generation\":1,\"visible\":\"false\"}}", false)]
    [InlineData("{\"version\":1,\"type\":\"regionVisibilityCompleted\",\"payload\":{}}", false)]
    [InlineData("{\"version\":1,\"type\":\"regionVisibilityCompleted\",\"payload\":null}", false)]
    public void CompletionMessagesRequireAValidVersionGenerationAndBoolean(string json, bool expected)
    {
        Assert.Equal(expected, LyricsWebMessageRouter.TryGetRegionVisibilityCompleted(LyricsWebMessageRouter.Parse(json), out _, out _));
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
