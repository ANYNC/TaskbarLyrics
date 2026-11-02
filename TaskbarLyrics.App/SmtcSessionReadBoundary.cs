using System.Runtime.InteropServices;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.App;

internal static class SmtcSessionReadBoundary
{
    internal static async Task<PlaybackSnapshot> ReadAsync(
        Func<CancellationToken, Task<PlaybackSnapshot?>> read,
        Func<PlaybackSnapshot> fallback,
        Action<Exception?> reportUnavailable,
        CancellationToken cancellationToken,
        Func<PlaybackSnapshot, bool>? isUsable = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var snapshot = await read(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot is not null && (isUsable is null || isUsable(snapshot))) return snapshot;
            if (snapshot is null) reportUnavailable(null);
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
