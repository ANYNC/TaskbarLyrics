using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

// Own a top-level notification HWND independently of Explorer's child windows.
// A message-only HWND does not receive broadcast display/taskbar notifications.
internal sealed class DisplayEnvironmentObserver : IDisposable
{
    private const int WmDisplayChange = 0x007E;
    private const int WmPowerBroadcast = 0x0218;
    private const int PbtResumeAutomatic = 0x0012;
    private const int PbtPowerSettingChange = 0x8013;
    private const int RefreshAttempts = 8;
    private static readonly Guid SessionDisplayStatus = new("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");
    private readonly HwndSource _source;
    private readonly DispatcherTimer _timer;
    private readonly Action _refresh;
    private readonly uint _taskbarCreated = TaskbarNativeMethods.RegisterWindowMessage("TaskbarCreated");
    private IntPtr _powerNotification;
    private int _remainingAttempts;
    private bool _disposed;
    private int _notificationCount;
    private string _lastReason = "Unknown";

    internal DisplayEnvironmentObserver(Action refresh)
    {
        _refresh = refresh;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _timer.Tick += OnRefreshTick;
        _source = new HwndSource(new HwndSourceParameters("TaskbarLyrics.DisplayEnvironment")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, hidden top-level window
            ExtendedWindowStyle = 0x08000080, // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            Width = 0,
            Height = 0
        });
        _source.AddHook(WndProc);
        var setting = SessionDisplayStatus;
        _powerNotification = RegisterPowerSettingNotification(_source.Handle, ref setting, 0);
        if (_powerNotification == IntPtr.Zero)
            Log.Warn($"Display power notification unavailable: {Marshal.GetLastWin32Error()}");
        if (LyricsDisplayDiagnostics.Enabled)
            Log.Diagnostic("DISPLAY_NOTIFY", $"Started Pid={Environment.ProcessId} " +
                $"Build={typeof(DisplayEnvironmentObserver).Assembly.ManifestModule.ModuleVersionId} " +
                $"Hwnd=0x{_source.Handle:X} PowerRegistered={_powerNotification != IntPtr.Zero}");
    }

    internal void RequestRefresh(string reason = "AttachmentOrPreference")
    {
        if (_disposed) return;
        _source.Dispatcher.VerifyAccess();
        _notificationCount++;
        if (LyricsDisplayDiagnostics.Enabled && (!_timer.IsEnabled || reason != _lastReason))
            Log.Diagnostic("DISPLAY_NOTIFY", $"Request Reason={reason} Count={_notificationCount} Pending={_timer.IsEnabled}");
        _lastReason = reason;
        _remainingAttempts = RefreshAttempts;
        // Keep the next scheduled pass during an event storm so it cannot starve.
        if (_timer.IsEnabled) return;
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Start();
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (--_remainingAttempts == 0) _timer.Stop();
        else _timer.Interval = TimeSpan.FromMilliseconds(500);
        if (LyricsDisplayDiagnostics.Enabled)
            Log.Diagnostic("DISPLAY_NOTIFY", $"Pass Reason={_lastReason} Notifications={_notificationCount} Remaining={_remainingAttempts}");
        // Display configuration can settle before Explorer creates its new taskbars.
        // Bound the follow-up passes; the normal attachment check remains the fallback.
        try { _refresh(); }
        catch (Exception exception) { Log.Error($"Display recovery failed: {exception}"); }
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_disposed) return IntPtr.Zero;
        if (message == WmDisplayChange)
            RequestRefresh($"WM_DISPLAYCHANGE Data=0x{lParam:X}");
        else if ((uint)message == _taskbarCreated)
            RequestRefresh("TaskbarCreated");
        else if (message == WmPowerBroadcast && wParam.ToInt64() == PbtResumeAutomatic)
            RequestRefresh("ResumeAutomatic");
        else if (message == WmPowerBroadcast && wParam.ToInt64() == PbtPowerSettingChange && lParam != IntPtr.Zero)
        {
            // POWERBROADCAST_SETTING: GUID + DWORD DataLength + variable byte Data[].
            var setting = Marshal.PtrToStructure<Guid>(lParam);
            if (setting == SessionDisplayStatus && Marshal.ReadInt32(lParam, 16) == sizeof(uint) &&
                Marshal.ReadInt32(lParam, 20) == 1)
                RequestRefresh("SessionDisplayOn");
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _source.Dispatcher.VerifyAccess();
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnRefreshTick;
        if (_powerNotification != IntPtr.Zero)
        {
            _ = UnregisterPowerSettingNotification(_powerNotification);
            _powerNotification = IntPtr.Zero;
        }
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid setting, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr notification);
}
