using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;

namespace TaskbarLyrics.App;

// A separate, cover-sized input window keeps an unlock entry while the lyrics HWND passes through.
internal sealed class FloatingCoverUnlockWindow : Window
{
    private readonly Action _unlock;

    internal FloatingCoverUnlockWindow(Window owner, Action unlock, Action<bool>? hoverChanged = null)
    {
        _unlock = unlock;
        Owner = owner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = owner.Topmost;
        Cursor = Cursors.Hand;
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
        MouseEnter += (_, _) => hoverChanged?.Invoke(true);
        MouseLeave += (_, _) => hoverChanged?.Invoke(false);
        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            TaskbarPlacementService.ApplyToolWindowStyle(source.Handle);
            source.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (message != 0x0021) return IntPtr.Zero; // WM_MOUSEACTIVATE
                handled = true;
                return new IntPtr(3); // MA_NOACTIVATE
            });
        };
        MouseLeftButtonUp += (_, e) => { e.Handled = true; _unlock(); };
    }

    internal void Position(Rect bounds, double scale)
    {
        Width = bounds.Width / scale;
        Height = bounds.Height / scale;
        Left = bounds.Left / scale;
        Top = bounds.Top / scale;
        Topmost = Owner.Topmost;
        if (!IsVisible) Show();
        TaskbarNativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
            (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
            (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height),
            TaskbarNativeMethods.SWP_NOACTIVATE | TaskbarNativeMethods.SWP_NOZORDER);
    }
}
