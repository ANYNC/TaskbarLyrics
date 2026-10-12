using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace TaskbarLyrics.App;

internal sealed class FloatingMaterialWindow : IFloatingPanelMaterial
{
    private const int ShadowPaddingDip = 20;
    private readonly HwndSource _source;
    private readonly DesktopWindowTarget _target;
    private readonly ContainerVisual _root;
    private readonly SpriteVisual _blur;
    private readonly SpriteVisual _tint;
    private readonly CompositionBrush _backdrop;
    private readonly CompositionColorBrush _tintBrush;
    private readonly CompositionRoundedRectangleGeometry _geometry;
    private readonly CompositionGeometricClip _clip;
    private readonly DropShadow _shadow;
    private readonly ScalarKeyFrameAnimation _fade;
    private readonly Compositor _compositor;
    private Rect _bounds;
    private bool _disposed;
    public IntPtr Handle => _source.Handle;
    internal bool UsesBackdrop { get; }

    internal FloatingMaterialWindow(FloatingMaterialCompositor context)
    {
        _compositor = context.Compositor;
        try
        {
            // A non-layered composition surface can sample the desktop independently of the lyrics HWND.
            _source = new HwndSource(new HwndSourceParameters("TaskbarLyrics floating material")
            {
                PositionX = -32000,
                PositionY = -32000,
                Width = 1,
                Height = 1,
                WindowStyle = unchecked((int)0x80000000),
                ExtendedWindowStyle = 0x00200080 | 0x08000000
            });
            _source.AddHook(WndProc);
            _source.CompositionTarget.BackgroundColor = System.Windows.Media.Colors.Transparent;
            var enabled = 1;
            UsesBackdrop = !SystemParameters.HighContrast && DwmSetWindowAttribute(Handle, 17, ref enabled, 4) >= 0;
            _compositor.As<IDesktopInterop>().CreateDesktopWindowTarget(Handle, true, out var pointer);
            try { _target = MarshalInterface<DesktopWindowTarget>.FromAbi(pointer); }
            finally { Marshal.Release(pointer); }
            _root = _compositor.CreateContainerVisual();
            _root.Opacity = 0;
            _target.Root = _root;
            _blur = _compositor.CreateSpriteVisual();
            _backdrop = UsesBackdrop ? _compositor.CreateHostBackdropBrush() : _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 32, 33, 36));
            _blur.Brush = _backdrop;
            _blur.Opacity = 0.9f;
            _tintBrush = _compositor.CreateColorBrush();
            _tint = _compositor.CreateSpriteVisual();
            _tint.Brush = _tintBrush;
            _geometry = _compositor.CreateRoundedRectangleGeometry();
            _clip = _compositor.CreateGeometricClip(_geometry);
            _blur.Clip = _clip;
            _tint.Clip = _clip;
            _shadow = _compositor.CreateDropShadow();
            _shadow.Color = Windows.UI.Color.FromArgb(255, 0, 0, 0);
            _shadow.Opacity = 0.22f;
            _blur.Shadow = _shadow;
            _root.Children.InsertAtTop(_blur);
            _root.Children.InsertAtTop(_tint);
            _fade = _compositor.CreateScalarKeyFrameAnimation();
            _fade.Duration = TimeSpan.FromMilliseconds(180);
        }
        catch { Dispose(); throw; }
    }

    public void Position(Rect cardBounds, double scale, IntPtr lyricsHandle)
    {
        _bounds = cardBounds;
        var padding = (int)Math.Round(ShadowPaddingDip * scale);
        var size = new Vector2((float)cardBounds.Width, (float)cardBounds.Height);
        _root.Size = size + new Vector2(padding * 2);
        _blur.Offset = _tint.Offset = new Vector3(padding, padding, 0);
        _blur.Size = _tint.Size = _geometry.Size = size;
        _geometry.CornerRadius = new Vector2((float)(8 * scale));
        _shadow.BlurRadius = (float)(24 * scale);
        _shadow.Offset = new Vector3(0, (float)(4 * scale), 0);
        // Keep the material below the unchanged lyrics and its owned media-control window.
        TaskbarNativeMethods.SetWindowPos(Handle, lyricsHandle,
            (int)Math.Round(cardBounds.Left) - padding, (int)Math.Round(cardBounds.Top) - padding,
            (int)Math.Round(cardBounds.Width) + padding * 2, (int)Math.Round(cardBounds.Height) + padding * 2,
            TaskbarNativeMethods.SWP_NOACTIVATE | 0x0040);
    }

    public void Configure(bool light, double brightness, double tintOpacity)
    {
        brightness = Math.Clamp(brightness, 0, 1);
        tintOpacity = Math.Clamp(tintOpacity, 0, 1);
        var level = (byte)Math.Round(light ? 180 + 75 * brightness : 24 + 140 * brightness);
        var alpha = (byte)Math.Round(255 * tintOpacity);
        _tintBrush.Color = SystemParameters.HighContrast
            ? Windows.UI.Color.FromArgb(255, System.Windows.SystemColors.WindowColor.R, System.Windows.SystemColors.WindowColor.G, System.Windows.SystemColors.WindowColor.B)
            : Windows.UI.Color.FromArgb(alpha, level, level, level);
    }

    public void SetVisible(bool visible, bool immediate = false)
    {
        if (immediate || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
        {
            _root.StopAnimation("Opacity");
            _root.Opacity = visible ? 1 : 0;
            return;
        }
        _fade.InsertExpressionKeyFrame(0, "this.StartingValue");
        _fade.InsertKeyFrame(1, visible ? 1 : 0);
        _root.StartAnimation("Opacity", _fade);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0084)
        {
            var x = (short)(lParam.ToInt64() & 0xffff);
            var y = (short)((lParam.ToInt64() >> 16) & 0xffff);
            handled = true;
            return _bounds.Contains(x, y) ? new IntPtr(1) : new IntPtr(-1);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source?.RemoveHook(WndProc);
        if (_target is not null) _target.Root = null;
        _root?.Children.RemoveAll();
        _fade?.Dispose(); _shadow?.Dispose(); _clip?.Dispose(); _geometry?.Dispose();
        _tint?.Dispose(); _blur?.Dispose(); _tintBrush?.Dispose(); _backdrop?.Dispose();
        _root?.Dispose(); _target?.Dispose(); _source?.Dispose();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopInterop
    {
        void CreateDesktopWindowTarget(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool topmost, out IntPtr target);
    }
}
