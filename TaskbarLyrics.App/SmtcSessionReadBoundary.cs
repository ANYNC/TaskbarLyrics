using System.Runtime.InteropServices;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.App;

internal static class SmtcSessionReadBoundary
{
    internal static async Task<PlaybackSnapshot> ReadAsync(
        Func<CancellationToken, Task<PlaybackSnapshot?>> read,
        Func<PlaybackSnapshot> fallback,
        Action<Exception?> reportUnavailable,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var snapshot = await read(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot is not null) return snapshot;
            reportUnavailable(null);
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException ||
            exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reportUnavailable(exception);
        }
        return fallback();
    }
}
