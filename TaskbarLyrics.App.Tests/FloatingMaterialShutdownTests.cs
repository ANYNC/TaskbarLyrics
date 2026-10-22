using Windows.System;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Fact]
    public void OwnedCompositionQueueFinishesShutdownOnItsStaBeforeThreadExit()
    {
        RunSta(() =>
        {
            var ownerThread = Environment.CurrentManagedThreadId;
            var shutdownThread = 0;
            var context = new FloatingMaterialCompositor();
            var queue = DispatcherQueue.GetForCurrentThread();
            Assert.NotNull(queue);
            queue.ShutdownCompleted += (_, _) => shutdownThread = Environment.CurrentManagedThreadId;

            context.Dispose();
            var shutdown = FloatingMaterialCompositor.WaitForPendingShutdownAsync();
            context.Dispose();
            Assert.Same(shutdown, FloatingMaterialCompositor.WaitForPendingShutdownAsync());
            CompleteStartupPanel(shutdown);

            Assert.Equal(ownerThread, shutdownThread);
        });
    }

    [Fact]
    public void BorrowedCompositorDisposalKeepsItsOwnersQueueRunning()
    {
        RunSta(() =>
        {
            var owner = new FloatingMaterialCompositor();
            var queue = DispatcherQueue.GetForCurrentThread();
            var shutdownStarted = false;
            queue.ShutdownStarting += (_, _) => shutdownStarted = true;
            try
            {
                using (var borrower = new FloatingMaterialCompositor())
                {
                    using var brush = borrower.Compositor.CreateColorBrush();
                }

                PumpMessages(TimeSpan.FromMilliseconds(20));
                Assert.False(shutdownStarted);
                Assert.Same(queue, DispatcherQueue.GetForCurrentThread());
                using var ownerBrush = owner.Compositor.CreateColorBrush();
            }
            finally { owner.Dispose(); }

            CompleteStartupPanel(FloatingMaterialCompositor.WaitForPendingShutdownAsync());
            Assert.True(shutdownStarted);
        });
    }

    [Fact]
    public void CompositionQueueCanBeRecreatedAfterEachCompletedShutdown()
    {
        RunSta(() =>
        {
            for (var cycle = 0; cycle < 4; cycle++)
            {
                var shutdownCompleted = false;
                using (var context = new FloatingMaterialCompositor())
                {
                    var queue = DispatcherQueue.GetForCurrentThread();
                    queue.ShutdownCompleted += (_, _) => shutdownCompleted = true;
                    using var brush = context.Compositor.CreateColorBrush();
                }

                CompleteStartupPanel(FloatingMaterialCompositor.WaitForPendingShutdownAsync());
                Assert.True(shutdownCompleted);
            }
        });
    }
}
