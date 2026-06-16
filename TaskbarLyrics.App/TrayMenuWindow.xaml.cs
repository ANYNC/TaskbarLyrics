using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Media = System.Windows.Media;
using Forms = System.Windows.Forms;

namespace TaskbarLyrics.App;

public partial class TrayMenuWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const int MonitorDpiTypeEffective = 0;
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;
    private const int VkMButton = 0x04;
    private const byte VkEscape = 0x1B;
    private const int KeyEventKeyUp = 0x0002;

    private readonly Action _toggleLyricsWindow;
    private readonly Action _openSettings;
    private readonly Action _exitApp;
    private readonly DispatcherTimer _closeTimer;
    private int _graceTicks = 3;

    public TrayMenuWindow(Action toggleLyricsWindow, Action openSettings, Action exitApp)
    {
        InitializeComponent();
        AppIconProvider.ApplyWindowIcon(this);
        ApplyTheme();
        _toggleLyricsWindow = toggleLyricsWindow;
        _openSettings = openSettings;
        _exitApp = exitApp;
        SourceInitialized += OnSourceInitialized;
        _closeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        _closeTimer.Tick += OnCloseTimerTick;
        Closed += (_, _) => _closeTimer.Stop();
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
        Show();
        _closeTimer.Start();
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
    }

    private void OnCloseTimerTick(object? sender, EventArgs e)
    {
        if (_graceTicks > 0)
        {
            _graceTicks--;
            return;
        }

        if (IsCursorInsideWindow())
        {
            return;
        }

        if (IsMouseButtonPressed())
        {
            Close();
        }
    }

    private bool IsCursorInsideWindow()
    {
        var cursor = Forms.Cursor.Position;
        var topLeft = PointToScreen(new System.Windows.Point(0, 0));
        var dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
        var width = ActualWidth * dpi.M11;
        var height = ActualHeight * dpi.M22;
        return cursor.X >= topLeft.X &&
            cursor.X <= topLeft.X + width &&
            cursor.Y >= topLeft.Y &&
            cursor.Y <= topLeft.Y + height;
    }

    private void ToggleLyricsButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        DismissTrayOverflow();
        _toggleLyricsWindow();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        DismissTrayOverflow();
        _openSettings();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        DismissTrayOverflow();
        _exitApp();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, int flags, UIntPtr extraInfo);

    private static bool IsMouseButtonPressed()
    {
        return (GetAsyncKeyState(VkLButton) & 0x8000) != 0 ||
            (GetAsyncKeyState(VkRButton) & 0x8000) != 0 ||
            (GetAsyncKeyState(VkMButton) & 0x8000) != 0;
    }

    private static void DismissTrayOverflow()
    {
        keybd_event(VkEscape, 0, 0, UIntPtr.Zero);
        keybd_event(VkEscape, 0, KeyEventKeyUp, UIntPtr.Zero);
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
    private readonly struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public readonly int X;
        public readonly int Y;
    }

    private readonly record struct DpiScale(double X, double Y);
}
