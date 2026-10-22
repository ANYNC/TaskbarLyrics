using System.Runtime.InteropServices;
using Windows.System;
using Windows.UI.Composition;
using WinRT;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

internal sealed class FloatingMaterialCompositor : IDisposable
{
    // Panel compositors share one native queue on their STA. Its owner can be
    // disposed during window recreation; keep shutdown awaitable by that thread's host.
    [ThreadStatic]
    private static Task? _pendingQueueShutdown;
    private readonly DispatcherQueueController? _queue;
    private bool _disposed;
    internal Compositor Compositor { get; }

    internal FloatingMaterialCompositor()
    {
        if (DispatcherQueue.GetForCurrentThread() is null)
        {
            var options = new QueueOptions { Size = Marshal.SizeOf<QueueOptions>(), Thread = 2, Apartment = 2 };
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out var pointer));
            try { _queue = MarshalInterface<DispatcherQueueController>.FromAbi(pointer); }
            finally { Marshal.Release(pointer); }
        }
        try { Compositor = new Compositor(); }
        catch
        {
            BeginQueueShutdown();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Compositor.Dispose();
        BeginQueueShutdown();
    }

    internal static Task WaitForPendingShutdownAsync() => _pendingQueueShutdown ?? Task.CompletedTask;

    private void BeginQueueShutdown()
    {
        if (_queue is null) return;
        var shutdown = ShutdownQueueAsync();
        _pendingQueueShutdown = _pendingQueueShutdown is { IsCompleted: false } pending
            ? Task.WhenAll(pending, shutdown)
            : shutdown;
        TaskObserver.Observe(shutdown, "floating material dispatcher shutdown");
    }

    private async Task ShutdownQueueAsync()
    {
        Log.Debug($"[SHUTDOWN] Scope='CompositionQueue' Stage='Shutdown.Begin' ThreadId={Environment.CurrentManagedThreadId}");
        await _queue!.ShutdownQueueAsync();
        Log.Debug($"[SHUTDOWN] Scope='CompositionQueue' Stage='Shutdown.End' ThreadId={Environment.CurrentManagedThreadId}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueueOptions { internal int Size; internal int Thread; internal int Apartment; }
    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(QueueOptions options, out IntPtr controller);
}
