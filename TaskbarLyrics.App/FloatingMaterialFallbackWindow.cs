using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace TaskbarLyrics.App;

internal sealed class FloatingMaterialFallbackWindow : Window, IFloatingPanelMaterial
{
    private readonly Border _surface = new() { CornerRadius = new CornerRadius(8), Opacity = 0 };
    private readonly Border _hitRegion = new() { Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0)) };
    private HwndSource? _source;
    private Rect _bounds;
    private bool _disposed;
    public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

    internal FloatingMaterialFallbackWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        var grid = new Grid { Margin = new Thickness(20) };
        grid.Children.Add(_hitRegion);
        grid.Children.Add(_surface);
        _surface.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.22, Color = System.Windows.Media.Colors.Black };
        _surface.CacheMode = new BitmapCache();
        Content = grid;
        SourceInitialized += OnSourceInitialized;
    }

    public void Position(Rect cardBounds, double scale, IntPtr lyricsHandle)
    {
        _bounds = cardBounds;
        Width = cardBounds.Width / scale + 40;
        Height = cardBounds.Height / scale + 40;
        Left = cardBounds.Left / scale - 20;
        Top = cardBounds.Top / scale - 20;
        Show();
        var padding = (int)Math.Round(20 * scale);
        TaskbarNativeMethods.SetWindowPos(Handle, lyricsHandle, (int)Math.Round(cardBounds.Left) - padding,
            (int)Math.Round(cardBounds.Top) - padding, (int)Math.Round(cardBounds.Width) + padding * 2,
            (int)Math.Round(cardBounds.Height) + padding * 2, TaskbarNativeMethods.SWP_NOACTIVATE);
    }
    public void Configure(bool light, double brightness, double tintOpacity)
    {
        var level = (byte)Math.Round(light ? 180 + 75 * Math.Clamp(brightness, 0, 1) : 24 + 140 * Math.Clamp(brightness, 0, 1));
        _surface.Background = SystemParameters.HighContrast ? System.Windows.SystemColors.WindowBrush
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)Math.Round(Math.Clamp(tintOpacity, 0, 1) * 255), level, level, level));
    }
    public void SetVisible(bool visible, bool immediate = false)
    {
        var from = _surface.Opacity;
        _surface.BeginAnimation(OpacityProperty, null);
        _surface.Opacity = visible ? 1 : 0;
        if (!immediate && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
            _surface.BeginAnimation(OpacityProperty, new DoubleAnimation(from, visible ? 1 : 0, TimeSpan.FromMilliseconds(180)));
    }
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        TaskbarPlacementService.ApplyToolWindowStyle(Handle);
        _source = HwndSource.FromHwnd(Handle);
        _source?.AddHook(WndProc);
    }
    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return new IntPtr(3); }
        if (message != 0x0084) return IntPtr.Zero;
        var x = (short)(lParam.ToInt64() & 0xffff);
        var y = (short)((lParam.ToInt64() >> 16) & 0xffff);
        handled = true;
        return _bounds.Contains(x, y) ? new IntPtr(1) : new IntPtr(-1);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SourceInitialized -= OnSourceInitialized;
        _source?.RemoveHook(WndProc);
        Close();
    }
}
