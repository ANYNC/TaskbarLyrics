using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

// Explorer calls run off the lyrics dispatcher. Only one query may be pending;
// cached physical coordinates belong to one taskbar handle and rectangle.
internal sealed class TaskbarOccupiedSpaceProvider : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IdleRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdleSnapshotLifetime = IdleRefreshInterval + SnapshotLifetime;
    private static readonly TimeSpan EventCoalesceInterval = TimeSpan.FromMilliseconds(100);
    private readonly object _gate = new();
    private readonly Func<IntPtr, Rect, CancellationToken, TaskbarInterval[]?> _read;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _stop = new();
    private Request? _request;
    private Snapshot? _snapshot;
    private Task _pendingRead = Task.CompletedTask;
    private long _lastRequest;
    private long _generation;
    private bool _hasRequested;
    private bool _queryRunning;
    private bool _disposed;
    private bool _eventMonitoring;
    private bool _readFailed;
    private long? _changedAt;
    private long? _uncertainSince;
    private long _changeVersion;
    private TaskbarInterval[]? _lastObserved;

    public TaskbarOccupiedSpaceProvider() : this(ReadOccupiedBounds, TimeProvider.System) { }

    internal TaskbarOccupiedSpaceProvider(
        Func<IntPtr, Rect, CancellationToken, TaskbarInterval[]?> read, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(clock);
        _read = read;
        _clock = clock;
    }

    internal Task PendingRead
    {
        get { lock (_gate) { return _pendingRead; } }
    }

    public event EventHandler? SnapshotAvailable;

    // Only a live event subscription permits the slower idle cadence. Hook
    // failures automatically retain the original polling/expiry contract.
    internal void SetEventMonitoring(bool available)
    {
        lock (_gate)
        {
            if (_disposed || _eventMonitoring == available) return;
            _eventMonitoring = available;
            InvalidateCache();
        }
    }

    internal void NotifyChanged()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _changeVersion++;
            _lastObserved = null;
            _changedAt = _clock.GetTimestamp();
            _uncertainSince ??= _changedAt;
        }
    }

    public IReadOnlyList<TaskbarInterval>? GetOccupiedBounds(IntPtr taskbar, Rect bounds)
    {
        lock (_gate)
        {
            if (_disposed) return null;
            if (taskbar == IntPtr.Zero || bounds.IsEmpty ||
                !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) ||
                !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
                bounds.Width <= 0 || bounds.Height <= 0)
            {
                InvalidateCache();
                return null;
            }

            var request = new Request(taskbar, bounds);
            if (_request != request)
            {
                InvalidateCache();
                _request = request;
                _changedAt = _clock.GetTimestamp();
            }

            var fastRefresh = !_eventMonitoring || _readFailed || _uncertainSince is not null ||
                              _snapshot is null || IsSettling();
            var interval = fastRefresh ? RefreshInterval : IdleRefreshInterval;
            var coalescing = _eventMonitoring && _uncertainSince is { } changed &&
                             _clock.GetElapsedTime(changed) < EventCoalesceInterval;
            if (!_queryRunning && !coalescing && (!_hasRequested || _clock.GetElapsedTime(_lastRequest) >= interval))
            {
                _lastRequest = _clock.GetTimestamp();
                _hasRequested = true;
                _queryRunning = true;
                var started = _lastRequest;
                var token = _stop.Token;
                var generation = _generation;
                var changeVersion = _changeVersion;
                _pendingRead = Task.Run(() => Refresh(request, started, generation, changeVersion, token), CancellationToken.None);
                TaskObserver.Observe(_pendingRead, "taskbar occupied-space query");
            }

            return _snapshot is { } snapshot &&
                   _clock.GetElapsedTime(snapshot.Timestamp) <= (_eventMonitoring ? IdleSnapshotLifetime : SnapshotLifetime) &&
                   (_uncertainSince is not { } uncertain || _clock.GetElapsedTime(uncertain) <= SnapshotLifetime)
                ? snapshot.Occupied
                : null;
        }
    }

    private bool IsSettling() => _changedAt is { } changed && _clock.GetElapsedTime(changed) < SnapshotLifetime;

    public void Invalidate()
    {
        lock (_gate)
        {
            if (!_disposed) InvalidateCache();
        }
    }

    // Called under _gate. Leave the in-flight read registered until it returns:
    // native UIA calls cannot always be cancelled, but their old generation can
    // never repopulate the cache after a detach or an invalid native target.
    private void InvalidateCache()
    {
        _request = null;
        _snapshot = null;
        _hasRequested = false;
        _lastObserved = null;
        _changedAt = null;
        _uncertainSince = null;
        _readFailed = false;
        _changeVersion++;
        _generation++;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _snapshot = null;
        }
        _stop.Cancel();
        _stop.Dispose();
    }

    internal static TaskbarInterval[] ToLogicalBounds(
        IReadOnlyList<TaskbarInterval> physicalBounds, double taskbarOrigin, double pixelsPerDip)
    {
        pixelsPerDip = double.IsFinite(pixelsPerDip) && pixelsPerDip > 0 ? pixelsPerDip : 1;
        return physicalBounds.Select(interval => new TaskbarInterval(
            (interval.Start - taskbarOrigin) / pixelsPerDip,
            (interval.End - taskbarOrigin) / pixelsPerDip)).ToArray();
    }

    private void Refresh(Request request, long started, long generation, long changeVersion, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var occupied = _read(request.Taskbar, request.Bounds, token);
            var accepted = false;
            lock (_gate)
            {
                if (!_disposed && _eventMonitoring && _generation == generation && _request == request &&
                    _changeVersion != changeVersion && occupied is not null &&
                    _clock.GetElapsedTime(started) <= SnapshotLifetime && _snapshot is { } prior &&
                    _clock.GetElapsedTime(prior.Timestamp) <= IdleSnapshotLifetime &&
                    (_uncertainSince is not { } uncertain || _clock.GetElapsedTime(uncertain) <= SnapshotLifetime))
                {
                    // A later event cannot make positive occupancy unsafe to add.
                    // Use it only to shrink, never to expand or renew freshness;
                    // confirmation still requires a read from the latest version.
                    _snapshot = new Snapshot(MergeOccupancy(prior.Occupied, occupied), prior.Timestamp);
                    accepted = true;
                }
                if (!_disposed && _generation == generation && _request == request && _changeVersion == changeVersion)
                {
                    if (occupied is null || _clock.GetElapsedTime(started) > SnapshotLifetime)
                    {
                        MarkReadFailure(started);
                    }
                    else
                    {
                        occupied = occupied.OrderBy(item => item.Start).ThenBy(item => item.End).Distinct().ToArray();
                        var confirmed = !_eventMonitoring || _lastObserved is not null && _lastObserved.SequenceEqual(occupied);
                        if (_eventMonitoring && (_lastObserved is null || !_lastObserved.SequenceEqual(occupied)))
                            _changedAt = _clock.GetTimestamp(); // even a missed event enters short polling
                        var safeBounds = confirmed || _snapshot is null ? occupied
                            : MergeOccupancy(_snapshot.Occupied, occupied);
                        // New occupancy shrinks free space immediately. Removed
                        // occupancy is released only after a matching second read.
                        _snapshot = new Snapshot(safeBounds, started);
                        _lastObserved = occupied;
                        _readFailed = false;
                        _uncertainSince = null;
                        accepted = true;
                    }
                }
            }
            if (accepted) SnapshotAvailable?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (exception is ElementNotAvailableException or COMException or
                                          InvalidOperationException or ArgumentException)
        {
            lock (_gate)
            {
                if (!_disposed && _generation == generation && _request == request && _changeVersion == changeVersion)
                    MarkReadFailure(started);
            }
        }
        finally
        {
            lock (_gate) { _queryRunning = false; }
        }
    }

    private void MarkReadFailure(long started)
    {
        _readFailed = true;
        _uncertainSince ??= started;
        _lastObserved = null;
    }

    private static TaskbarInterval[] MergeOccupancy(TaskbarInterval[] previous, TaskbarInterval[] current)
    {
        // A moving button can produce many distinct intermediate rectangles.
        // Merge overlaps instead of retaining every historical rectangle.
        var merged = new List<TaskbarInterval>();
        foreach (var interval in previous.Concat(current).OrderBy(item => item.Start).ThenBy(item => item.End))
        {
            if (merged.Count > 0 && interval.Start <= merged[^1].End)
                merged[^1] = new TaskbarInterval(merged[^1].Start, Math.Max(merged[^1].End, interval.End));
            else merged.Add(interval);
        }
        return merged.ToArray();
    }

    internal static TaskbarInterval[]? ReadOccupiedBounds(
        IntPtr taskbar, Rect taskbarBounds, CancellationToken token) =>
        NativeTaskbarAutomation.ReadOccupiedBounds(taskbar, taskbarBounds, token);

    // Orientation is derived from Bounds, just as in LyricsLayoutMetrics. A turn
    // between horizontal and vertical necessarily changes this request identity.
    private readonly record struct Request(IntPtr Taskbar, Rect Bounds);
    private sealed record Snapshot(TaskbarInterval[] Occupied, long Timestamp);
}
