using System.Reflection;
using System.Windows.Threading;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Windows.System;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class LyricsWindowHostShutdownTests
{
    [Fact]
    public async Task FailedStartupStillCompletesItsPendingNativeQueueShutdown()
    {
        using var composition = new FailingCompositionRoot();
        using var offsets = new TrackLyricOffsetStore();

        var failure = Assert.Throws<AggregateException>(() => new LyricsWindowHost(
            new AppSettings(), offsets, composition, () => { }, () => { }, _ => { }));

        Assert.Equal("Startup probe failed.", Assert.IsType<InvalidOperationException>(failure.InnerException).Message);
        await composition.QueueStopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(composition.LyricsThread!.Join(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task CloseAsyncPumpsLyricsStaUntilTheNativeQueueCompletes()
    {
        using var composition = new AppCompositionRoot();
        using var offsets = new TrackLyricOffsetStore();
        using var host = new LyricsWindowHost(new AppSettings { UseFloatingWindow = true }, offsets,
            composition, () => { }, () => { }, _ => { });
        var dispatcher = (Dispatcher)typeof(LyricsWindowHost)
            .GetField("_dispatcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var releaseShutdown = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shutdownCompleted = false;

        await dispatcher.InvokeAsync(() =>
        {
            var compositor = new FloatingMaterialCompositor();
            var queue = DispatcherQueue.GetForCurrentThread();
            queue.ShutdownStarting += (_, args) =>
            {
                var deferral = args.GetDeferral();
                releaseShutdown.TrySetResult(deferral.Complete);
            };
            queue.ShutdownCompleted += (_, _) => shutdownCompleted = true;
            compositor.Dispose();
        });

        var release = await releaseShutdown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var closing = host.CloseAsync();
        try
        {
            Assert.Same(closing, host.CloseAsync());
            // WPF must still dispatch work while the native queue holds its shutdown deferral.
            await dispatcher.InvokeAsync(() => Assert.False(closing.IsCompleted));
        }
        finally
        {
            await dispatcher.InvokeAsync(release);
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.True(shutdownCompleted);
        Assert.True(dispatcher.HasShutdownFinished);
    }

    private sealed class FailingCompositionRoot : IAppCompositionRoot
    {
        internal TaskCompletionSource QueueStopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Thread? LyricsThread { get; private set; }

        public FloatingWindowPlacement CreateFloatingWindowPlacement(System.Windows.Window window)
        {
            LyricsThread = Thread.CurrentThread;
            using var compositor = new FloatingMaterialCompositor();
            DispatcherQueue.GetForCurrentThread().ShutdownCompleted += (_, _) => QueueStopped.TrySetResult();
            throw new InvalidOperationException("Startup probe failed.");
        }

        public MusicSessionServices CreateMusicSessionServices() => throw new NotSupportedException();
        public LyricSyncService CreateLyricSyncService(AppSettings settings, TrackLyricOffsetStore offsets) => throw new NotSupportedException();
        public LyricDiagnosticRunner CreateLyricDiagnosticRunner() => throw new NotSupportedException();
        public LyricDiagnosticRunner CreateLyricDiagnosticRunner(AppSettings settings) => throw new NotSupportedException();
        public ValueTask<ResolvedLyricCacheStoreResult> RememberResolvedLyricsAsync(TrackInfo track, ResolvedLyrics lyrics,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public void ClearLyricCache() => throw new NotSupportedException();
        public LocalMediaCoverProvider? CreateLocalMediaCoverProvider(AppSettings settings) => throw new NotSupportedException();
        public IReadOnlyCollection<string> GetEnabledPlayerSources(AppSettings settings) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
