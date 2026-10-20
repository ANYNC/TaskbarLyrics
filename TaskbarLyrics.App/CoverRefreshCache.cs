using System.Security.Cryptography;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

internal sealed class CoverRefreshCache : IDisposable
{
    private static readonly TimeSpan[] RecheckDelays =
        [TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5)];
    private static readonly TimeSpan MissingCoverRetryInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _utcNow;
    private string _identity = string.Empty;
    private string _metadataIdentity = string.Empty;
    private byte[]? _bytes;
    private string _hash = string.Empty;
    private DateTimeOffset _trackChangedUtc;
    private DateTimeOffset _nextMissingRetryUtc;
    private int _nextRecheck;
    private bool _hasRead;
    private long _generation;
    private CancellationTokenSource? _readCancellation;
    private bool _disposed;

    internal CoverRefreshCache(Func<DateTimeOffset>? utcNow = null) => _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    internal (byte[]? Bytes, bool IsLoading) GetCover(
        string identity,
        string metadataIdentity,
        Func<CancellationToken, Task<byte[]?>>? read)
    {
        CancellationTokenSource? cancellation = null;
        long generation;
        (byte[]? Bytes, bool IsLoading) snapshot;
        lock (_gate)
        {
            if (_disposed) return (null, false);
            var now = _utcNow();
            if (_identity != identity || _metadataIdentity != metadataIdentity)
            {
                ResetLocked();
                _identity = identity;
                _metadataIdentity = metadataIdentity;
                _trackChangedUtc = now;
            }

            var recheckDue = _nextRecheck < RecheckDelays.Length && now >= _trackChangedUtc + RecheckDelays[_nextRecheck];
            var missingRetryDue = _bytes is null && now >= _nextMissingRetryUtc;
            if (read is not null && _readCancellation is null && (!_hasRead || recheckDue || missingRetryDue))
            {
                _hasRead = true;
                // Coalesce missed checkpoints after a slow read or suspended polling.
                while (_nextRecheck < RecheckDelays.Length && now >= _trackChangedUtc + RecheckDelays[_nextRecheck])
                    _nextRecheck++;
                cancellation = new CancellationTokenSource(ReadTimeout);
                _readCancellation = cancellation;
            }

            generation = _generation;
            snapshot = (_bytes, _bytes is null && _readCancellation is not null);
        }

        if (cancellation is not null)
            TaskObserver.Observe(ReadAsync(read!, generation, cancellation), "SMTC cover refresh");
        return snapshot;
    }

    private async Task ReadAsync(Func<CancellationToken, Task<byte[]?>> read, long generation, CancellationTokenSource cancellation)
    {
        try
        {
            var bytes = await read(cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            var hash = bytes is { Length: > 0 } ? CoverContentHash.Compute(bytes) : string.Empty;
            lock (_gate)
            {
                // The generation also distinguishes A -> B -> A, even if a reader ignores cancellation.
                if (_disposed || generation != _generation || !ReferenceEquals(cancellation, _readCancellation)) return;
                if (hash.Length > 0 && hash != _hash)
                {
                    _bytes = bytes;
                    _hash = hash;
                    Log.Diagnostic("COVER-SMTC", $"ArtworkUpdated Generation={generation} Hash='{hash}'");
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(cancellation, _readCancellation))
                {
                    _readCancellation = null;
                    _nextMissingRetryUtc = _utcNow() + MissingCoverRetryInterval;
                }
                cancellation.Dispose();
            }
        }
    }

    internal void Reset()
    {
        lock (_gate) ResetLocked();
    }

    private void ResetLocked()
    {
        _generation++;
        var previous = _readCancellation;
        _readCancellation = null;
        previous?.Cancel();
        _identity = _metadataIdentity = _hash = string.Empty;
        _bytes = null;
        _nextRecheck = 0;
        _hasRead = false;
        _nextMissingRetryUtc = default;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            ResetLocked();
        }
    }
}

internal static class CoverContentHash
{
    internal static string Compute(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
