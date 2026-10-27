using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

internal delegate void TaskbarWinEventCallback(IntPtr hook, uint kind, IntPtr window,
    int objectId, int childId, uint thread, uint timestamp);

internal interface ITaskbarEventApi
{
    uint GetProcessId(IntPtr window);
    bool IsChild(IntPtr parent, IntPtr window);
    IntPtr Subscribe(uint processId, TaskbarWinEventCallback callback);
    bool Unsubscribe(IntPtr hook);
}

// WinEvent registration and removal belong to the lyrics STA, which owns its
// message pump. No automation element or cross-apartment COM object is retained.
internal sealed class TaskbarChangeObserver : IDisposable
{
    private Dispatcher? _dispatcher;
    private readonly ITaskbarEventApi _api;
    private readonly TimeProvider _clock;
    private readonly Action _changed;
    private readonly Action _targetChanged;
    private readonly TaskbarWinEventCallback _callback;
    private IntPtr _taskbar, _hook;
    private uint _process;
    private long? _lastAttempt;
    private GCHandle _callbackRoot;
    private bool _disposed;
    private bool _reportedFailure;

    internal TaskbarChangeObserver(Action changed, Action targetChanged,
        ITaskbarEventApi? api = null, TimeProvider? clock = null)
    {
        _changed = changed;
        _targetChanged = targetChanged;
        _api = api ?? new NativeTaskbarEventApi();
        _clock = clock ?? TimeProvider.System;
        _callback = OnEvent;
    }

    internal bool UpdateTarget(IntPtr taskbar)
    {
        (_dispatcher ??= Dispatcher.CurrentDispatcher).VerifyAccess();
        if (_disposed) return false;
        var process = taskbar == IntPtr.Zero ? 0 : _api.GetProcessId(taskbar);
        if (process == (uint)Environment.ProcessId) process = 0; // never watch our own positioning
        if (_taskbar != taskbar || _process != process)
        {
            Detach();
            _taskbar = taskbar;
            _process = process;
        }
        if (process == 0) return false;
        if (_hook != IntPtr.Zero) return true;
        if (_lastAttempt is { } last && _clock.GetElapsedTime(last) < TimeSpan.FromSeconds(5)) return false;
        _lastAttempt = _clock.GetTimestamp();
        _callbackRoot = GCHandle.Alloc(_callback);
        try { _hook = _api.Subscribe(process, _callback); }
        catch { _callbackRoot.Free(); throw; }
        if (_hook != IntPtr.Zero) return true;
        _callbackRoot.Free();
        if (!_reportedFailure)
        {
            Log.Diagnostic("TASKBAR_SPACE", "Event subscription unavailable; using 500ms polling.");
            _reportedFailure = true;
        }
        return false;
    }

    private void OnEvent(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint timestamp)
    {
        if (_disposed || hook != _hook || _taskbar == IntPtr.Zero || window == IntPtr.Zero ||
            kind is not (0x8000 or 0x8001 or 0x8002 or 0x8003 or 0x8004 or 0x800B)) return;
        if (window == _taskbar || _api.GetProcessId(window) == _process && _api.IsChild(_taskbar, window))
        {
            if (kind == 0x8001 && window == _taskbar) _targetChanged();
            _changed();
        }
    }

    internal void Detach()
    {
        if (_hook != IntPtr.Zero)
        {
            _dispatcher!.VerifyAccess();
            // Keep the delegate rooted if removal fails; throwing must never
            // leave native code calling a collected managed callback.
            if (!_api.Unsubscribe(_hook)) throw new Win32Exception("Cannot remove taskbar event hook.");
            _hook = IntPtr.Zero;
            _callbackRoot.Free();
        }
        var hadTarget = _taskbar != IntPtr.Zero;
        _taskbar = IntPtr.Zero;
        _process = 0;
        _lastAttempt = null;
        _reportedFailure = false;
        if (hadTarget) _targetChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Detach();
        _disposed = true;
    }
}

internal sealed class NativeTaskbarEventApi : ITaskbarEventApi
{
    public uint GetProcessId(IntPtr window) => GetWindowThreadProcessId(window, out var process) == 0 ? 0 : process;
    public bool IsChild(IntPtr parent, IntPtr window) => IsChildWindow(parent, window);
    public IntPtr Subscribe(uint processId, TaskbarWinEventCallback callback) =>
        SetWinEventHook(0x8000, 0x800B, IntPtr.Zero, callback, processId, 0, 2); // out-of-context, skip own process
    public bool Unsubscribe(IntPtr hook) => UnhookWinEvent(hook);

    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", EntryPoint = "IsChild")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsChildWindow(IntPtr parent, IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module,
        TaskbarWinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(IntPtr hook);
}
