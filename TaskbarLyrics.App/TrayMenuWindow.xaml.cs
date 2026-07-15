using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Media = System.Windows.Media;
using Controls = System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace TaskbarLyrics.App;

public partial class TrayMenuWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WhMouseLowLevel = 14;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmRightButtonDown = 0x0204;
    private const int WmMiddleButtonDown = 0x0207;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const int MonitorDpiTypeEffective = 0;
    private const int VkEscape = 0x1B;

    private readonly Action _toggleLyricsWindow;
    private readonly Action<bool, SpectrumDisplayMode> _setSpectrumDisplayMode;
    private readonly SpectrumDisplayMode _spectrumDisplayMode;
    private readonly Action _openSettings;
    private readonly Action _openSmtcMonitor;
    private readonly Action _openSpectrumTuning;
    private readonly Action _exitApp;
    private readonly DispatcherTimer _spectrumPopupCloseTimer;
    private readonly LowLevelMouseProc _mouseHookCallback;
    private IntPtr _mouseHook;

    public TrayMenuWindow(
        Action toggleLyricsWindow,
        Action<bool, SpectrumDisplayMode> setSpectrumDisplayMode,
        bool isSpectrumEnabled,
        SpectrumDisplayMode spectrumDisplayMode,
        Action openSettings,
        Action openSmtcMonitor,
        Action openSpectrumTuning,
        Action exitApp)
    {
        InitializeComponent();
        AppIconProvider.ApplyWindowIcon(this);
        ApplyTheme();
        _toggleLyricsWindow = toggleLyricsWindow;
        _setSpectrumDisplayMode = setSpectrumDisplayMode;
        _spectrumDisplayMode = spectrumDisplayMode;
        _openSettings = openSettings;
        _openSmtcMonitor = openSmtcMonitor;
        _openSpectrumTuning = openSpectrumTuning;
        _exitApp = exitApp;
        _mouseHookCallback = OnLowLevelMouseEvent;
        SyncSpectrumModeChecks(isSpectrumEnabled, spectrumDisplayMode);
        SourceInitialized += OnSourceInitialized;
        _spectrumPopupCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(140)
        };
        _spectrumPopupCloseTimer.Tick += OnSpectrumPopupCloseTimerTick;
        Closed += (_, _) =>
        {
            UninstallMouseHook();
            _spectrumPopupCloseTimer.Stop();
            SpectrumModePopup.IsOpen = false;
        };
    }

    private void ApplyTheme()
    {
        var light = App.IsSystemUsingLightTheme();
        Resources["TrayMenuBackgroundBrush"] = new Media.SolidColorBrush(light
            ? Media.Color.FromRgb(248, 250, 252)
            : Media.Color.FromRgb(30, 30, 30));
        Resources["TrayMenuHoverBrush"] = new Media.SolidColorBrush(light
            ? Media.Color.FromRgb(229, 234, 242)
            : Media.Color.FromRgb(48, 48, 48));
        Resources["TrayMenuPressedBrush"] = new Media.SolidColorBrush(light
            ? Media.Color.FromRgb(218, 226, 237)
            : Media.Color.FromRgb(58, 58, 58));
        Resources["TrayMenuTextBrush"] = new Media.SolidColorBrush(light
            ? Media.Color.FromRgb(15, 23, 42)
            : Media.Colors.White);
        Resources["TrayMenuSeparatorBrush"] = new Media.SolidColorBrush(light
            ? Media.Color.FromRgb(218, 226, 237)
            : Media.Color.FromRgb(74, 74, 74));
    }

    public void ShowAtCursor()
    {
        var cursorPhysical = Forms.Cursor.Position;
        var dpi = GetDpiScaleForPoint(cursorPhysical);
        var cursorX = cursorPhysical.X / dpi.X;
        var cursorY = cursorPhysical.Y / dpi.Y;
        var screenPhysical = Forms.Screen.FromPoint(cursorPhysical).WorkingArea;
        var screenLeft = screenPhysical.Left / dpi.X;
        var screenTop = screenPhysical.Top / dpi.Y;
        var screenRight = screenPhysical.Right / dpi.X;
        var screenBottom = screenPhysical.Bottom / dpi.Y;
        const int gap = 8;
        var left = cursorX - Width + 22;
        var top = cursorY - Height - gap;

        if (left < screenLeft + gap)
        {
            left = cursorX - 22;
        }

        if (top < screenTop + gap)
        {
            top = cursorY + gap;
        }

        Left = Math.Clamp(left, screenLeft + gap, screenRight - Width - gap);
        Top = Math.Clamp(top, screenTop + gap, screenBottom - Height - gap);
        const double popupWidth = 210;
        const double popupGap = 1;
        var openPopupRight = Left + Width + popupGap + popupWidth <= screenRight - gap;
        SpectrumModePopup.Placement = openPopupRight ? PlacementMode.Right : PlacementMode.Left;
        SpectrumModePopup.HorizontalOffset = openPopupRight ? popupGap : -popupGap;
        Show();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLong(hwnd, GwlExStyle);
        _ = SetWindowLong(hwnd, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
        _mouseHook = SetWindowsHookEx(WhMouseLowLevel, _mouseHookCallback, GetModuleHandle(null), 0);
    }

    private IntPtr OnLowLevelMouseEvent(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && IsMouseButtonDownMessage(message))
        {
            var hookData = Marshal.PtrToStructure<LowLevelMouseHookData>(data);
            var clickPoint = new System.Drawing.Point(hookData.Point.X, hookData.Point.Y);
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (IsVisible && !IsPointInsideMenuSurface(clickPoint))
                {
                    Close();
                }
            }, DispatcherPriority.Input);
        }

        return CallNextHookEx(_mouseHook, code, message, data);
    }

    private static bool IsMouseButtonDownMessage(IntPtr message)
    {
        var value = message.ToInt64();
        return value is WmLeftButtonDown or WmRightButtonDown or WmMiddleButtonDown;
    }

    private bool IsPointInsideMenuSurface(System.Drawing.Point point)
    {
        return IsPointInsideElement(MenuSurface, point) ||
            SpectrumModePopup.IsOpen &&
            SpectrumModePopup.Child is FrameworkElement popupChild &&
            IsPointInsideElement(popupChild, point);
    }

    private static bool IsPointInsideElement(FrameworkElement element, System.Drawing.Point point)
    {
        var topLeft = element.PointToScreen(new System.Windows.Point(0, 0));
        var dpi = PresentationSource.FromVisual(element)?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
        var width = element.ActualWidth * dpi.M11;
        var height = element.ActualHeight * dpi.M22;
        return point.X >= topLeft.X &&
            point.X <= topLeft.X + width &&
            point.Y >= topLeft.Y &&
            point.Y <= topLeft.Y + height;
    }

    private void ToggleLyricsButton_Click(object sender, RoutedEventArgs e)
    {
        InvokeCommand(_toggleLyricsWindow);
    }

    private void SpectrumMenuButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _spectrumPopupCloseTimer.Stop();
        SpectrumModePopup.IsOpen = true;
    }

    private void SpectrumMenuButton_Click(object sender, RoutedEventArgs e)
    {
        _spectrumPopupCloseTimer.Stop();
        SpectrumModePopup.IsOpen = true;
    }

    private void SpectrumMenuButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ScheduleSpectrumPopupClose();
    }

    private void SpectrumModePopup_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _spectrumPopupCloseTimer.Stop();
    }

    private void SpectrumModePopup_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ScheduleSpectrumPopupClose();
    }

    private void ScheduleSpectrumPopupClose()
    {
        _spectrumPopupCloseTimer.Stop();
        _spectrumPopupCloseTimer.Start();
    }

    private void OnSpectrumPopupCloseTimerTick(object? sender, EventArgs e)
    {
        _spectrumPopupCloseTimer.Stop();
        if (!SpectrumMenuButton.IsMouseOver &&
            SpectrumModePopup.Child is FrameworkElement popupChild &&
            !popupChild.IsMouseOver)
        {
            SpectrumModePopup.IsOpen = false;
        }
    }

    private void SpectrumModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Controls.Button { Tag: string modeName })
        {
            return;
        }

        if (string.Equals(modeName, "Disabled", StringComparison.Ordinal))
        {
            InvokeCommand(() => _setSpectrumDisplayMode(false, _spectrumDisplayMode));
            return;
        }

        if (Enum.TryParse<SpectrumDisplayMode>(modeName, out var mode))
        {
            InvokeCommand(() => _setSpectrumDisplayMode(true, mode));
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        InvokeCommand(_openSettings);
    }

    private void SmtcMonitorButton_Click(object sender, RoutedEventArgs e)
    {
        InvokeCommand(_openSmtcMonitor);
    }

    private void SpectrumTuningButton_Click(object sender, RoutedEventArgs e)
    {
        InvokeCommand(_openSpectrumTuning);
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        InvokeCommand(_exitApp);
    }

    private void InvokeCommand(Action command)
    {
        SpectrumModePopup.IsOpen = false;
        Close();
        DismissTrayOverflow();
        command();
    }

    private void SyncSpectrumModeChecks(bool isEnabled, SpectrumDisplayMode mode)
    {
        SpectrumDisabledCheck.Visibility = isEnabled ? Visibility.Hidden : Visibility.Visible;
        SpectrumPureMusicCheck.Visibility = isEnabled && mode == SpectrumDisplayMode.PureMusicOnly
            ? Visibility.Visible
            : Visibility.Hidden;
        SpectrumNoLyricsCheck.Visibility = isEnabled && mode == SpectrumDisplayMode.PureMusicOrNoLyrics
            ? Visibility.Visible
            : Visibility.Hidden;
        SpectrumAlwaysCheck.Visibility = isEnabled && mode == SpectrumDisplayMode.Always
            ? Visibility.Visible
            : Visibility.Hidden;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId, LowLevelMouseProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    private static void DismissTrayOverflow()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return;
        }

        _ = GetWindowThreadProcessId(foreground, out var processId);
        if (processId == 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            if (!string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = PostMessage(foreground, WmKeyDown, (IntPtr)VkEscape, IntPtr.Zero);
            _ = PostMessage(foreground, WmKeyUp, (IntPtr)VkEscape, IntPtr.Zero);
        }
        catch (ArgumentException)
        {
            // The foreground process may exit between querying and sending the message.
        }
        catch (InvalidOperationException)
        {
            // Ignore transient shell process access failures.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Ignore protected or short-lived foreground processes.
        }
    }

    private void UninstallMouseHook()
    {
        if (_mouseHook == IntPtr.Zero)
        {
            return;
        }

        _ = UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero;
    }

    private static DpiScale GetDpiScaleForPoint(System.Drawing.Point point)
    {
        var monitor = MonitorFromPoint(new NativePoint(point.X, point.Y), MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero &&
            GetDpiForMonitor(monitor, MonitorDpiTypeEffective, out var dpiX, out var dpiY) == 0 &&
            dpiX > 0 &&
            dpiY > 0)
        {
            return new DpiScale(dpiX / 96.0, dpiY / 96.0);
        }

        return new DpiScale(1.0, 1.0);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseHookData
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    private delegate IntPtr LowLevelMouseProc(int code, IntPtr message, IntPtr data);

    private readonly record struct DpiScale(double X, double Y);
}
