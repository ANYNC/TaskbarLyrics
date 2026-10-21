using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

internal sealed class FloatingMaterialWindow : IFloatingPanelMaterial
{
    private double _blurAmount = 24;
    private double _scale = 1;
    private readonly HwndSource _source;
    private readonly DesktopWindowTarget _target;
    private readonly ContainerVisual _root;
    private readonly LayerVisual _surfaceLayer;
    private readonly FloatingMaterialFallbackWindow _shadowWindow;
    private CompositionScopedBatch? _fadeBatch;
    private readonly SpriteVisual _blur;
    private readonly SpriteVisual _tint;
    private readonly CompositionBrush _backdrop;
    private CompositionEffectFactory? _blurFactory;
    private CompositionEffectBrush? _blurBrush;
    private readonly CompositionColorBrush _tintBrush;
    private readonly CompositionRoundedRectangleGeometry _geometry;
    private readonly CompositionGeometricClip _clip;
    private readonly Compositor _compositor;
    private bool _visible;
    private bool _disposed;
    public IntPtr Handle => _source.Handle;
    internal bool UsesBackdrop { get; }

    internal FloatingMaterialWindow(FloatingMaterialCompositor context)
    {
        _compositor = context.Compositor;
        try
        {
            // Backdrop sampling requires a non-layered HWND. Restrict it to the card;
            // a separate, input-transparent layered window draws the outer shadow.
            _source = new HwndSource(new HwndSourceParameters("TaskbarLyrics floating material")
            {
                PositionX = -32000,
                PositionY = -32000,
                Width = 1,
                Height = 1,
                WindowStyle = unchecked((int)0x80000000),
                ExtendedWindowStyle = 0x08200080
            });
            _source.AddHook(WndProc);
            _source.DpiChanged += OnDpiChanged;
            _source.CompositionTarget.BackgroundColor = System.Windows.Media.Colors.Transparent;
            _shadowWindow = new FloatingMaterialFallbackWindow(shadowOnly: true);
            var enabled = 1;
            UsesBackdrop = !SystemParameters.HighContrast && DwmSetWindowAttribute(Handle, 17, ref enabled, 4) >= 0;
            _compositor.As<IDesktopInterop>().CreateDesktopWindowTarget(Handle, true, out var pointer);
            try { _target = MarshalInterface<DesktopWindowTarget>.FromAbi(pointer); }
            finally { Marshal.Release(pointer); }
            _root = _compositor.CreateContainerVisual();
            // Keep the non-layered host-backdrop HWND; fade its Composition content instead.
            // Layering the HWND would change backdrop sampling and input behavior.
            _root.Opacity = 0;
            _target.Root = _root;
            _surfaceLayer = _compositor.CreateLayerVisual();
            _blur = _compositor.CreateSpriteVisual();
            _backdrop = UsesBackdrop ? _compositor.CreateHostBackdropBrush() : _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 32, 33, 36));
            _blur.Brush = _backdrop;
            if (UsesBackdrop)
            {
                try
                {
                    _blurFactory = new CompositionGaussianBlurEffect().CreateFactory(_compositor);
                    _blurBrush = _blurFactory.CreateBrush();
                    _blurBrush.SetSourceParameter(CompositionGaussianBlurEffect.SourceParameter, _backdrop);
                    UpdateBlurAmount();
                    _blur.Brush = _blurBrush;
                }
                catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException)
                {
                    DisableEnhancedBlur(ex);
                }
            }
            _blur.Opacity = 1;
            _tintBrush = _compositor.CreateColorBrush();
            _tint = _compositor.CreateSpriteVisual();
            _tint.Brush = _tintBrush;
            _geometry = _compositor.CreateRoundedRectangleGeometry();
            _clip = _compositor.CreateGeometricClip(_geometry);
            _blur.Clip = _clip;
            _tint.Clip = _clip;
            _surfaceLayer.Children.InsertAtTop(_blur);
            _surfaceLayer.Children.InsertAtTop(_tint);
            _root.Children.InsertAtTop(_surfaceLayer);
        }
        catch { Dispose(); throw; }
    }

    public void Position(Rect cardBounds, double scale, IntPtr lyricsHandle)
    {
        var size = new Vector2((float)cardBounds.Width, (float)cardBounds.Height);
        _root.Size = size;
        _surfaceLayer.Size = size;
        _blur.Size = _tint.Size = _geometry.Size = size;
        _geometry.CornerRadius = new Vector2((float)(12 * scale));
        _scale = scale;
        UpdateBlurAmount();
        // Keep the material below the unchanged lyrics and its owned media-control window.
        TaskbarNativeMethods.SetWindowPos(Handle, lyricsHandle,
            (int)Math.Round(cardBounds.Left), (int)Math.Round(cardBounds.Top),
            (int)Math.Round(cardBounds.Width), (int)Math.Round(cardBounds.Height),
            TaskbarNativeMethods.SWP_NOACTIVATE);
        // Leave a small guard around the antialiased 12-DIP visual corner.
        var diameter = (int)Math.Round(20 * scale);
        var region = CreateRoundRectRgn(0, 0, (int)Math.Round(cardBounds.Width) + 1,
            (int)Math.Round(cardBounds.Height) + 1, diameter, diameter);
        if (region == IntPtr.Zero) throw new InvalidOperationException("Cannot create floating card input region.");
        // USER32 owns the region after a successful SetWindowRgn call.
        if (SetWindowRgn(Handle, region, true) == 0)
        {
            DeleteObject(region);
            throw new InvalidOperationException("Cannot apply floating card input region.");
        }
        _shadowWindow.Position(cardBounds, scale, Handle);
    }

    public bool EnsureBelow(IntPtr lyricsHandle) => FloatingPanelWindowOrder.EnsureBelow(Handle, lyricsHandle)
        && _shadowWindow.EnsureBelow(Handle);

    public void Configure(bool light, double brightness, double tintOpacity, double blurAmount)
    {
        _blurAmount = double.IsFinite(blurAmount) ? Math.Clamp(blurAmount, 0, 64) : 24;
        UpdateBlurAmount();
        brightness = Math.Clamp(brightness, 0, 1);
        tintOpacity = Math.Clamp(tintOpacity, 0, 1);
        var level = (byte)Math.Round(light ? 180 + 75 * brightness : 24 + 140 * brightness);
        var alpha = (byte)Math.Round(255 * tintOpacity);
        _tintBrush.Color = SystemParameters.HighContrast
            ? Windows.UI.Color.FromArgb(255, System.Windows.SystemColors.WindowColor.R, System.Windows.SystemColors.WindowColor.G, System.Windows.SystemColors.WindowColor.B)
            : Windows.UI.Color.FromArgb(alpha, level, level, level);
        _shadowWindow.Configure(light, brightness, tintOpacity, blurAmount);
    }

    private void UpdateBlurAmount()
    {
        // Direct2D limits Gaussian standard deviation to 250 physical pixels.
        _blurBrush?.Properties.InsertScalar(CompositionGaussianBlurEffect.AmountProperty, (float)Math.Min(250, _blurAmount * _scale));
    }

    public Task PrepareAsync(CancellationToken cancellationToken) =>
        _compositor.RequestCommitAsync().AsTask(cancellationToken);

    public void SetVisible(bool visible, bool immediate = false)
    {
        if (_visible == visible && !immediate) return;
        _visible = visible;
        CancelFadeCompletion();
        _shadowWindow.SetVisible(visible, immediate);
        if (visible)
        {
            TaskbarNativeMethods.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                TaskbarNativeMethods.SWP_NOMOVE | TaskbarNativeMethods.SWP_NOSIZE |
                TaskbarNativeMethods.SWP_NOZORDER | TaskbarNativeMethods.SWP_NOACTIVATE | 0x0040);
        }
        if (_blurFactory?.LoadStatus is CompositionEffectFactoryLoadStatus.Other or CompositionEffectFactoryLoadStatus.EffectTooComplex)
            DisableEnhancedBlur(_blurFactory.ExtendedError);
        if (immediate || !FloatingPanelAnimation.IsEnabled)
        {
            _root.Opacity = visible ? 1 : 0;
            if (!visible) TaskbarNativeMethods.ShowWindow(Handle, 0);
            return;
        }
        _fadeBatch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _fadeBatch.Completed += FadeCompleted;
        using var animation = _compositor.CreateScalarKeyFrameAnimation();
        using var easing = _compositor.CreateCubicBezierEasingFunction(new Vector2(1f / 3, 1), new Vector2(2f / 3, 1));
        animation.Duration = FloatingPanelAnimation.Duration;
        // Sample the compositor's current presentation value when replacing an in-flight fade.
        animation.InsertExpressionKeyFrame(0, "this.StartingValue");
        animation.InsertKeyFrame(1, visible ? 1 : 0, easing);
        _root.StartAnimation(nameof(Visual.Opacity), animation);
        _fadeBatch.End();
    }

    private void FadeCompleted(object sender, CompositionBatchCompletedEventArgs args)
    {
        if (!_source.Dispatcher.CheckAccess())
        {
            _source.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => FinishFade(sender)));
            return;
        }
        FinishFade(sender);
    }

    private void FinishFade(object sender)
    {
        if (_disposed || !ReferenceEquals(sender, _fadeBatch)) return;
        CancelFadeCompletion();
        _root.Opacity = _visible ? 1 : 0;
        if (!_visible) TaskbarNativeMethods.ShowWindow(Handle, 0);
    }

    private void CancelFadeCompletion()
    {
        if (_fadeBatch is not { } batch) return;
        _fadeBatch = null;
        batch.Completed -= FadeCompleted;
        batch.Dispose();
    }

    private static void OnDpiChanged(object sender, HwndDpiChangedEventArgs e)
    {
        // This HWND has no WPF visual tree: Position owns its physical bounds and Composition geometry.
        // WPF's suggested rectangle would scale those target-screen pixels a second time on monitor changes.
        e.Handled = true;
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021) // WM_MOUSEACTIVATE
        {
            // Keep a background click from raising this HWND above the lyrics until the next order repair.
            handled = true;
            return new IntPtr(3); // MA_NOACTIVATE
        }
        if (message == 0x0084)
        {
            handled = true;
            return new IntPtr(1);
        }
        return IntPtr.Zero;
    }

    private void DisableEnhancedBlur(Exception? error)
    {
        _blur.Brush = _backdrop;
        _blurBrush?.Dispose(); _blurBrush = null;
        _blurFactory?.Dispose(); _blurFactory = null;
        Log.Diagnostic("FLOATING_MATERIAL", $"Enhanced blur unavailable; retaining host backdrop: {error?.GetType().Name}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelFadeCompletion();
        _shadowWindow?.Dispose();
        if (_source is not null) _source.DpiChanged -= OnDpiChanged;
        _source?.RemoveHook(WndProc);
        if (_target is not null) _target.Root = null;
        _root?.Children.RemoveAll();
        _surfaceLayer?.Children.RemoveAll();
        _clip?.Dispose(); _geometry?.Dispose();
        _tint?.Dispose(); _blur?.Dispose(); _tintBrush?.Dispose();
        _blurBrush?.Dispose(); _blurFactory?.Dispose();
        _backdrop?.Dispose();
        _surfaceLayer?.Dispose(); _root?.Dispose(); _target?.Dispose(); _source?.Dispose();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopInterop
    {
        void CreateDesktopWindowTarget(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool topmost, out IntPtr target);
    }

    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
}
