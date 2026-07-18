using System.Text;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Media = System.Windows.Media;
using Controls = System.Windows.Controls;

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
    private const uint GetAncestorRoot = 2;
    private const uint PopupRightAlign = 0x0008;
    private const uint PopupBottomAlign = 0x0020;
    private const uint PopupVertical = 0x0040;
    private const uint PopupWorkArea = 0x00010000;
    private const uint SetWindowPositionNoActivate = 0x0010;
    private const uint SetWindowPositionNoOwnerZOrder = 0x0200;
    private static readonly IntPtr TopMostWindow = new(-1);

    private readonly Action _toggleLyricsWindow;
    private readonly Action<bool, SpectrumDisplayMode> _setSpectrumDisplayMode;
    private readonly SpectrumDisplayMode _spectrumDisplayMode;
    private readonly Action _openCurrentTrackOffsetSettings;
    private readonly Action _openSettings;
    private readonly Action _openSmtcMonitor;
    private readonly Action _openSpectrumTuning;
    private readonly Action _exitApp;
    private readonly DispatcherTimer _spectrumPopupCloseTimer;
    private readonly LowLevelMouseProc _mouseHookCallback;
    private IntPtr _mouseHook;
    private IntPtr _trayOverflowWindow;
    private IntPtr _trayOverflowInputWindow;

    public TrayMenuWindow(
        Action toggleLyricsWindow,
        Action<bool, SpectrumDisplayMode> setSpectrumDisplayMode,
        bool isSpectrumEnabled,
        SpectrumDisplayMode spectrumDisplayMode,
        Action openCurrentTrackOffsetSettings,
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
        _openCurrentTrackOffsetSettings = openCurrentTrackOffsetSettings;
        _openSettings = openSettings;
        _openSmtcMonitor = openSmtcMonitor;
        _openSpectrumTuning = openSpectrumTuning;
        _exitApp = exitApp;
        _mouseHookCallback = OnLowLevelMouseEvent;
        SyncSpectrumModeChecks(isSpectrumEnabled, spectrumDisplayMode);
        SourceInitialized += OnSourceInitialized;
        NativeWindowTheme.ThemeChanged += OnWindowThemeChanged;
        _spectrumPopupCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(140)
        };
        _spectrumPopupCloseTimer.Tick += OnSpectrumPopupCloseTimerTick;
        Closed += (_, _) =>
        {
            NativeWindowTheme.ThemeChanged -= OnWindowThemeChanged;
            UninstallMouseHook();
            _spectrumPopupCloseTimer.Stop();
            SpectrumModePopup.IsOpen = false;
        };
    }

    private void ApplyTheme()
    {
        var light = NativeWindowTheme.IsLight;
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
        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
        {
            SpectrumModePopup.PopupAnimation = PopupAnimation.None;
        }
    }

    private void OnWindowThemeChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted)
        {
            Dispatcher.BeginInvoke(ApplyTheme);
        }
    }

    public void ShowAt(System.Drawing.Point invocationPoint)
    {
        _trayOverflowWindow = FindTrayOverflowWindow(invocationPoint);
        _trayOverflowInputWindow = FindTrayOverflowInputWindow(_trayOverflowWindow);
        var placement = CalculatePlacement(invocationPoint);
        const double popupWidth = 210;
        const double popupGap = 1;
        var popupPhysicalWidth = ScaleToPhysical(popupWidth, placement.Dpi);
        var rightSpace = placement.WorkArea.Right - placement.Bounds.Right;
        var leftSpace = placement.Bounds.Left - placement.WorkArea.Left;
        var openPopupRight = rightSpace >= popupPhysicalWidth || rightSpace >= leftSpace;
        SpectrumModePopup.Placement = openPopupRight ? PlacementMode.Right : PlacementMode.Left;
        SpectrumModePopup.HorizontalOffset = openPopupRight ? popupGap : -popupGap;

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        PositionWindow(hwnd, placement.Bounds);
        var animateOpening = SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
        MenuSurface.Opacity = animateOpening ? 0 : 1;
        Show();
        PositionWindow(hwnd, placement.Bounds);
        if (animateOpening)
        {
            var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new QuadraticEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };
            MenuSurface.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
    }

    private PopupPlacement CalculatePlacement(System.Drawing.Point invocationPoint)
    {
        var anchor = new NativePoint(invocationPoint.X, invocationPoint.Y);
        var excludeBounds = new NativeRect(anchor.X, anchor.Y, anchor.X + 1, anchor.Y + 1);
        var monitor = MonitorFromPoint(anchor, MonitorDefaultToNearest);
        var monitorInfo = GetMonitorInformation(monitor);
        var dpi = GetEffectiveDpi(monitor);
        var size = new NativeSize(
            ScaleToPhysical(Width, dpi),
            ScaleToPhysical(Height, dpi));
        var edge = ResolveTaskbarEdge(monitorInfo, anchor);
        var flags = GetPlacementFlags(edge);

        if (!CalculatePopupWindowPosition(
                ref anchor,
                ref size,
                flags,
                ref excludeBounds,
                out var popupBounds))
        {
            popupBounds = ClampToWorkArea(anchor, size, monitorInfo.WorkArea);
        }

        return new PopupPlacement(popupBounds, monitorInfo.WorkArea, dpi);
    }

    private static MonitorInfo GetMonitorInformation(IntPtr monitor)
    {
        var info = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            throw new InvalidOperationException("无法获取托盘菜单所在显示器的信息。");
        }

        return info;
    }

    private static uint GetEffectiveDpi(IntPtr monitor)
    {
        return monitor != IntPtr.Zero &&
            GetDpiForMonitor(monitor, MonitorDpiTypeEffective, out var dpiX, out _) == 0 &&
            dpiX > 0
                ? dpiX
                : 96;
    }

    private static int ScaleToPhysical(double value, uint dpi)
    {
        return Math.Max(1, (int)Math.Ceiling(value * dpi / 96.0));
    }

    private static TaskbarEdge ResolveTaskbarEdge(MonitorInfo monitor, NativePoint anchor)
    {
        if (monitor.WorkArea.Bottom < monitor.MonitorArea.Bottom)
        {
            return TaskbarEdge.Bottom;
        }

        if (monitor.WorkArea.Top > monitor.MonitorArea.Top)
        {
            return TaskbarEdge.Top;
        }

        if (monitor.WorkArea.Left > monitor.MonitorArea.Left)
        {
            return TaskbarEdge.Left;
        }

        if (monitor.WorkArea.Right < monitor.MonitorArea.Right)
        {
            return TaskbarEdge.Right;
        }

        var distances = new (TaskbarEdge Edge, int Distance)[]
        {
            (TaskbarEdge.Left, Math.Abs(anchor.X - monitor.MonitorArea.Left)),
            (TaskbarEdge.Right, Math.Abs(monitor.MonitorArea.Right - anchor.X)),
            (TaskbarEdge.Top, Math.Abs(anchor.Y - monitor.MonitorArea.Top)),
            (TaskbarEdge.Bottom, Math.Abs(monitor.MonitorArea.Bottom - anchor.Y))
        };
        return distances.MinBy(entry => entry.Distance).Edge;
    }

    private static uint GetPlacementFlags(TaskbarEdge edge)
    {
        return edge switch
        {
            TaskbarEdge.Top => PopupRightAlign | PopupVertical | PopupWorkArea,
            TaskbarEdge.Left => PopupVertical | PopupWorkArea,
            TaskbarEdge.Right => PopupRightAlign | PopupVertical | PopupWorkArea,
            _ => PopupRightAlign | PopupBottomAlign | PopupVertical | PopupWorkArea
        };
    }

    private static NativeRect ClampToWorkArea(NativePoint anchor, NativeSize size, NativeRect workArea)
    {
        var left = Math.Clamp(anchor.X - size.Width, workArea.Left, workArea.Right - size.Width);
        var top = Math.Clamp(anchor.Y - size.Height, workArea.Top, workArea.Bottom - size.Height);
        return new NativeRect(left, top, left + size.Width, top + size.Height);
    }

    private static void PositionWindow(IntPtr hwnd, NativeRect bounds)
    {
        _ = SetWindowPos(
            hwnd,
            TopMostWindow,
            bounds.Left,
            bounds.Top,
            bounds.Right - bounds.Left,
            bounds.Bottom - bounds.Top,
            SetWindowPositionNoActivate | SetWindowPositionNoOwnerZOrder);
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

    private void TrackOffsetButton_Click(object sender, RoutedEventArgs e)
    {
        InvokeCommand(_openCurrentTrackOffsetSettings);
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
        var trayOverflowWindow = _trayOverflowWindow;
        var trayOverflowInputWindow = _trayOverflowInputWindow;
        SpectrumModePopup.IsOpen = false;
        Close();
        DismissTrayOverflow(trayOverflowWindow, trayOverflowInputWindow);
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    private static IntPtr FindTrayOverflowWindow(System.Drawing.Point invocationPoint)
    {
        var point = new NativePoint(invocationPoint.X, invocationPoint.Y);
        var root = GetAncestor(WindowFromPoint(point), GetAncestorRoot);
        if (IsTrayOverflowWindow(root))
        {
            return root;
        }

        var result = IntPtr.Zero;
        _ = EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) &&
                IsTrayOverflowWindow(hwnd) &&
                GetWindowRect(hwnd, out var bounds) &&
                bounds.Contains(point))
            {
                result = hwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static bool IsTrayOverflowWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var className = new StringBuilder(128);
        return GetClassName(hwnd, className, className.Capacity) > 0 &&
            className.ToString() is "TopLevelWindowForOverflowXamlIsland" or "NotifyIconOverflowWindow";
    }

    private static IntPtr FindTrayOverflowInputWindow(IntPtr trayOverflowWindow)
    {
        if (trayOverflowWindow == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var threadId = GetWindowThreadProcessId(trayOverflowWindow, out _);
        var info = new GuiThreadInfo
        {
            Size = Marshal.SizeOf<GuiThreadInfo>()
        };
        if (threadId == 0 ||
            !GetGUIThreadInfo(threadId, ref info) ||
            info.Focus == IntPtr.Zero ||
            GetAncestor(info.Focus, GetAncestorRoot) != trayOverflowWindow)
        {
            return trayOverflowWindow;
        }

        return info.Focus;
    }

    private static void DismissTrayOverflow(
        IntPtr trayOverflowWindow,
        IntPtr trayOverflowInputWindow)
    {
        if (trayOverflowWindow == IntPtr.Zero || !IsWindow(trayOverflowWindow))
        {
            return;
        }

        var inputWindow = trayOverflowInputWindow != IntPtr.Zero && IsWindow(trayOverflowInputWindow)
            ? trayOverflowInputWindow
            : trayOverflowWindow;
        _ = PostMessage(inputWindow, WmKeyDown, (IntPtr)VkEscape, IntPtr.Zero);
        _ = PostMessage(inputWindow, WmKeyUp, (IntPtr)VkEscape, IntPtr.Zero);
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

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CalculatePopupWindowPosition(
        ref NativePoint anchorPoint,
        ref NativeSize windowSize,
        uint flags,
        ref NativeRect excludeRect,
        out NativeRect popupWindowPosition);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

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
    private struct NativeSize
    {
        public NativeSize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public NativeRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly bool Contains(NativePoint point)
        {
            return point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public IntPtr Active;
        public IntPtr Focus;
        public IntPtr Capture;
        public IntPtr MenuOwner;
        public IntPtr MoveSize;
        public IntPtr Caret;
        public NativeRect CaretBounds;
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

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);

    private readonly record struct PopupPlacement(NativeRect Bounds, NativeRect WorkArea, uint Dpi);

    private enum TaskbarEdge
    {
        Left,
        Top,
        Right,
        Bottom
    }
}
