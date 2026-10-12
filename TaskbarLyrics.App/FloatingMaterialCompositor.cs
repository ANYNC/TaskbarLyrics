using System.Runtime.InteropServices;
using Windows.System;
using Windows.UI.Composition;
using WinRT;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

internal sealed class FloatingMaterialCompositor : IDisposable
{
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
            if (_queue is not null) TaskObserver.Observe(_queue.ShutdownQueueAsync().AsTask(), "floating material failed initialization cleanup");
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Compositor.Dispose();
        if (_queue is not null) TaskObserver.Observe(_queue.ShutdownQueueAsync().AsTask(), "floating material dispatcher shutdown");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueueOptions { internal int Size; internal int Thread; internal int Apartment; }
    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(QueueOptions options, out IntPtr controller);
}
