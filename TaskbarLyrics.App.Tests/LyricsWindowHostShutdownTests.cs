using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
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

    [Fact]
    public async Task UnexpectedWindowClosureKeepsDispatcherAliveAndRecreatesHiddenWindow()
    {
        using var composition = new AppCompositionRoot();
        using var offsets = new TrackLyricOffsetStore();
        using var host = new LyricsWindowHost(new AppSettings { UseFloatingWindow = true }, offsets,
            composition, () => { }, () => { }, _ => { });
        var dispatcher = GetHostField<Dispatcher>(host, "_dispatcher");
        var oldWindow = await dispatcher.InvokeAsync(() => GetHostField<MainWindow>(host, "_window"));
        try
        {
            await dispatcher.InvokeAsync(() =>
            {
                // Native destruction bypasses the managed CloseFromHost path, just
                // as a child HWND can disappear with its removed secondary taskbar.
                var hwnd = new WindowInteropHelper(oldWindow).EnsureHandle();
                Assert.True(DestroyWindow(hwnd));
            });
            var replacement = await WaitForReplacementAsync(host, dispatcher, oldWindow);
            await dispatcher.InvokeAsync(() =>
            {
                Assert.False(replacement.IsVisible);
                Assert.False(host.IsVisible);
                Assert.False(dispatcher.HasShutdownStarted);
            });
        }
        finally { await host.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.True(dispatcher.HasShutdownFinished);
    }

    [Fact]
    public async Task RepeatedRecreationRequestsCoalesceAndKeepSettingsChangedDuringNativeShutdown()
    {
        using var composition = new AppCompositionRoot();
        using var offsets = new TrackLyricOffsetStore();
        using var host = new LyricsWindowHost(new AppSettings { UseFloatingWindow = true }, offsets,
            composition, () => { }, () => { }, _ => { });
        var dispatcher = GetHostField<Dispatcher>(host, "_dispatcher");
        var releaseShutdown = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldWindow = await dispatcher.InvokeAsync(() =>
        {
            var window = GetHostField<MainWindow>(host, "_window");
            var compositor = new FloatingMaterialCompositor();
            DispatcherQueue.GetForCurrentThread().ShutdownStarting += (_, args) =>
            {
                var deferral = args.GetDeferral();
                releaseShutdown.TrySetResult(deferral.Complete);
            };
            compositor.Dispose();
            RequestRecreation(host, window);
            RequestRecreation(host, window);
            return window;
        });
        var release = await releaseShutdown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var released = false;
        try
        {
            host.ApplySettings(new AppSettings { UseFloatingWindow = true, FloatingWindowWidth = 650 });
            host.Hide();
            await dispatcher.InvokeAsync(release);
            released = true;
            var replacement = await WaitForReplacementAsync(host, dispatcher, oldWindow);
            await dispatcher.InvokeAsync(() =>
            {
                Assert.False(replacement.IsVisible);
                Assert.Equal(650, GetHostField<AppSettings>(host, "_currentSettings").FloatingWindowWidth);
                Assert.Equal(650, replacement.Width);
                RequestRecreation(host, oldWindow); // An obsolete callback must not replace the new host.
            });
            await dispatcher.InvokeAsync(() => Assert.Same(replacement, GetHostField<MainWindow>(host, "_window")));
        }
        finally
        {
            if (!released) await dispatcher.InvokeAsync(release);
            await host.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ClosingDuringRecreationDoesNotReviveWindowsOrDisplayNotifications()
    {
        using var composition = new AppCompositionRoot();
        using var offsets = new TrackLyricOffsetStore();
        using var host = new LyricsWindowHost(new AppSettings { UseFloatingWindow = true }, offsets,
            composition, () => { }, () => { }, _ => { });
        var dispatcher = GetHostField<Dispatcher>(host, "_dispatcher");
        var releaseShutdown = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        await dispatcher.InvokeAsync(() =>
        {
            var compositor = new FloatingMaterialCompositor();
            DispatcherQueue.GetForCurrentThread().ShutdownStarting += (_, args) =>
            {
                var deferral = args.GetDeferral();
                releaseShutdown.TrySetResult(deferral.Complete);
            };
            compositor.Dispose();
            RequestRecreation(host, GetHostField<MainWindow>(host, "_window"));
        });
        var release = await releaseShutdown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await dispatcher.InvokeAsync(() => Assert.True(GetHostField<bool>(host, "_recreatingWindow")));
        var closing = host.CloseAsync();
        await dispatcher.InvokeAsync(release);
        await closing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(dispatcher.HasShutdownFinished);
        Assert.Null(GetHostField<MainWindow?>(host, "_window"));
        Assert.Null(GetHostField<DisplayEnvironmentObserver?>(host, "_displayObserver"));
    }

    private static T GetHostField<T>(LyricsWindowHost host, string name) =>
        (T)typeof(LyricsWindowHost).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    private static void RequestRecreation(LyricsWindowHost host, MainWindow window) =>
        typeof(LyricsWindowHost).GetMethod("OnLyricsWindowRecreateRequested", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, [window, EventArgs.Empty]);

    private static async Task<MainWindow> WaitForReplacementAsync(LyricsWindowHost host, Dispatcher dispatcher, MainWindow oldWindow)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var window = await dispatcher.InvokeAsync(() => GetHostField<MainWindow?>(host, "_window"));
            if (window is not null && window != oldWindow) return window;
            await Task.Delay(20, timeout.Token);
        }
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
