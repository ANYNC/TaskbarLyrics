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
                _request = request;
                _generation++;
                _snapshot = null;
                _hasRequested = false;
            }

            if (!_queryRunning && (!_hasRequested || _clock.GetElapsedTime(_lastRequest) >= RefreshInterval))
            {
                _lastRequest = _clock.GetTimestamp();
                _hasRequested = true;
                _queryRunning = true;
                var started = _lastRequest;
                var token = _stop.Token;
                var generation = _generation;
                _pendingRead = Task.Run(() => Refresh(request, started, generation, token), CancellationToken.None);
                TaskObserver.Observe(_pendingRead, "taskbar occupied-space query");
            }

            return _snapshot is { } snapshot &&
                   _clock.GetElapsedTime(snapshot.Timestamp) <= SnapshotLifetime
                ? snapshot.Occupied
                : null;
        }
    }

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

    private void Refresh(Request request, long started, long generation, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var occupied = _read(request.Taskbar, request.Bounds, token);
            var accepted = false;
            lock (_gate)
            {
                if (!_disposed && _generation == generation && _request == request && occupied is not null)
                {
                    // Age from query start: a delayed Explorer response must not
                    // make geometry captured several seconds ago appear fresh.
                    _snapshot = new Snapshot(occupied, started);
                    accepted = true;
                }
            }
            if (accepted) SnapshotAvailable?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (exception is ElementNotAvailableException or COMException or
                                          InvalidOperationException or ArgumentException)
        {
            // Keep a matching last-known snapshot only until SnapshotLifetime.
        }
        finally
        {
            lock (_gate) { _queryRunning = false; }
        }
    }

    internal static TaskbarInterval[]? ReadOccupiedBounds(
        IntPtr taskbar, Rect taskbarBounds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var root = AutomationElement.FromHandle(taskbar);
        var owner = root.Current.ProcessId;
        var explorerOnly = new PropertyCondition(AutomationElement.ProcessIdProperty, owner);
        ControlType[] interactiveTypes =
            [ControlType.Button, ControlType.CheckBox, ControlType.RadioButton, ControlType.Edit, ControlType.ListItem];
        var controlsCondition = new AndCondition(explorerOnly,
            new PropertyCondition(AutomationElement.IsOffscreenProperty, false),
            new OrCondition(interactiveTypes.Select(type =>
                (System.Windows.Automation.Condition)new PropertyCondition(AutomationElement.ControlTypeProperty, type)).ToArray()));

        // Inspect Explorer-owned immediate children first. Searching the entire
        // taskbar subtree also walks embedded third-party/WebView windows.
        var hosts = root.FindAll(TreeScope.Children, explorerOnly);
        var occupied = new HashSet<TaskbarInterval>();
        var cache = new CacheRequest
        {
            AutomationElementMode = AutomationElementMode.None,
            TreeScope = TreeScope.Element
        };
        cache.Add(AutomationElement.BoundingRectangleProperty);
        for (var hostIndex = 0; hostIndex < hosts.Count; hostIndex++)
        {
            token.ThrowIfCancellationRequested();
            var host = hosts[hostIndex];
            var info = host.Current;
            if (interactiveTypes.Contains(info.ControlType) && !info.IsOffscreen) Add(info.BoundingRectangle);
            using var activation = cache.Activate();
            var controls = host.FindAll(TreeScope.Descendants, controlsCondition);
            for (var index = 0; index < controls.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                Add(controls[index].Cached.BoundingRectangle);
            }
        }

        // A completely missing taskbar tree is unknown, not an empty free bar.
        return occupied.Count > 0 ? occupied.ToArray() : null;

        void Add(Rect rect)
        {
            if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0 && rect.IntersectsWith(taskbarBounds))
                occupied.Add(taskbarBounds.Height > taskbarBounds.Width
                    ? new TaskbarInterval(rect.Top, rect.Bottom)
                    : new TaskbarInterval(rect.Left, rect.Right));
        }
    }

    // Orientation is derived from Bounds, just as in LyricsLayoutMetrics. A turn
    // between horizontal and vertical necessarily changes this request identity.
    private readonly record struct Request(IntPtr Taskbar, Rect Bounds);
    private sealed record Snapshot(TaskbarInterval[] Occupied, long Timestamp);
}
