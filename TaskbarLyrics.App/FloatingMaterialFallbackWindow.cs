using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace TaskbarLyrics.App;

internal sealed class FloatingMaterialFallbackWindow : Window, IFloatingPanelMaterial
{
    private const int ShadowPaddingDip = 36;
    private readonly Border _surface = new() { CornerRadius = new CornerRadius(12) };
    private readonly Grid _visualRoot = new() { Opacity = 0 };
    private readonly Grid _shadowLayer = new() { Margin = new Thickness(-ShadowPaddingDip), IsHitTestVisible = false };
    private readonly DropShadowEffect _shadow = new()
    {
        Color = System.Windows.Media.Colors.Black,
        BlurRadius = 20,
        ShadowDepth = 4,
        Direction = 270
    };
    private HwndSource? _source;
    private bool _disposed;
    public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

    internal FloatingMaterialFallbackWindow(bool shadowOnly = false)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        var grid = new Grid { Margin = new Thickness(ShadowPaddingDip) };
        _shadowLayer.Children.Add(new Border
        {
            Margin = new Thickness(ShadowPaddingDip),
            CornerRadius = new CornerRadius(12),
            Background = System.Windows.Media.Brushes.Black,
            Effect = _shadow
        });
        _visualRoot.Children.Add(_shadowLayer);
        _visualRoot.Children.Add(_surface);
        _surface.Visibility = shadowOnly ? Visibility.Hidden : Visibility.Visible;
        grid.Children.Add(_visualRoot);
        _surface.CacheMode = new BitmapCache();
        Content = grid;
        SourceInitialized += OnSourceInitialized;
    }

    public void Position(Rect cardBounds, double scale, IntPtr lyricsHandle)
    {
        // Remove the caster and inner shadow so the translucent surface stays unchanged.
        var card = new Rect(ShadowPaddingDip, ShadowPaddingDip, cardBounds.Width / scale, cardBounds.Height / scale);
        var shadowClip = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, card.Width + ShadowPaddingDip * 2, card.Height + ShadowPaddingDip * 2)),
            new RectangleGeometry(card, 12, 12));
        shadowClip.Freeze();
        _shadowLayer.Clip = shadowClip;
        Width = cardBounds.Width / scale + ShadowPaddingDip * 2;
        Height = cardBounds.Height / scale + ShadowPaddingDip * 2;
        Left = cardBounds.Left / scale - ShadowPaddingDip;
        Top = cardBounds.Top / scale - ShadowPaddingDip;
        Show();
        var padding = (int)Math.Round(ShadowPaddingDip * scale);
        TaskbarNativeMethods.SetWindowPos(Handle, lyricsHandle, (int)Math.Round(cardBounds.Left) - padding,
            (int)Math.Round(cardBounds.Top) - padding, (int)Math.Round(cardBounds.Width) + padding * 2,
            (int)Math.Round(cardBounds.Height) + padding * 2, TaskbarNativeMethods.SWP_NOACTIVATE);
    }
    public bool EnsureBelow(IntPtr lyricsHandle) => FloatingPanelWindowOrder.EnsureBelow(Handle, lyricsHandle);
    public void Configure(bool light, double brightness, double tintOpacity, double blurAmount)
    {
        var level = (byte)Math.Round(light ? 180 + 75 * Math.Clamp(brightness, 0, 1) : 24 + 140 * Math.Clamp(brightness, 0, 1));
        _surface.Background = SystemParameters.HighContrast ? System.Windows.SystemColors.WindowBrush
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)Math.Round(Math.Clamp(tintOpacity, 0, 1) * 255), level, level, level));
        _shadow.Opacity = SystemParameters.HighContrast ? 0 : light ? 0.16 : 0.30;
        _shadowLayer.Visibility = SystemParameters.HighContrast ? Visibility.Hidden : Visibility.Visible;
    }
    public void SetVisible(bool visible, bool immediate = false)
    {
        var from = _visualRoot.Opacity;
        _visualRoot.BeginAnimation(OpacityProperty, null);
        _visualRoot.Opacity = visible ? 1 : 0;
        if (!immediate && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
            _visualRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(from, visible ? 1 : 0, TimeSpan.FromMilliseconds(180)));
    }
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        TaskbarPlacementService.ApplyToolWindowStyle(Handle);
        NativeWindowInteraction.SetClickThrough(Handle, enabled: true);
        _source = HwndSource.FromHwnd(Handle);
        _source?.AddHook(WndProc);
    }
    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return new IntPtr(3); }
        if (message != 0x0084) return IntPtr.Zero;
        handled = true;
        return new IntPtr(-1);
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
