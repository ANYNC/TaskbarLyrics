using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace TaskbarLyrics.App;

/// <summary>
/// Owns the hit-surface fill of one floating lyrics window.
///
/// A layered window drops mouse input on fully transparent pixels, so a surface without a
/// background needs an alpha-1 pixel everywhere it should receive the pointer. That pixel
/// darkens the window by one level out of 255 — faintly visible as a base tint over a light
/// desktop — so the fill is only held while the cursor is inside the window, which is the one
/// moment the pixels have to be hittable. Because a cursor that does not move after the fill
/// is applied produces no mouse event of its own, the page is told about the pointer directly.
/// </summary>
internal sealed class FloatingWindowHitSurfaceController : IDisposable
{
    private const int PollIntervalMs = 30;

    private readonly Window _window;
    private readonly Border _root;
    private readonly DispatcherTimer _timer;
    private bool _trackPointer;
    private bool _pointerInside;
    private bool _isDisposed;

    internal FloatingWindowHitSurfaceController(Window window, Border root)
    {
        _window = window;
        _root = root;
        // Normal priority: a starved poll would leave the surface unhittable, so the cursor
        // would feel like it is not reaching the window at all.
        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(PollIntervalMs)
        };
        _timer.Tick += OnTick;
    }

    /// <summary>Raised when the cursor enters (<c>true</c>) or leaves (<c>false</c>) the surface.</summary>
    internal event EventHandler<bool>? PointerInsideChanged;

    internal bool PointerInside => _pointerInside;

    /// <summary>Starts or stops tracking; call on every settings or layout change.</summary>
    internal void Apply(bool trackPointer)
    {
        if (_isDisposed)
        {
            return;
        }

        _trackPointer = trackPointer;
        if (!trackPointer)
        {
            _timer.Stop();
            SetPointerInside(false);
            return;
        }

        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }

        UpdatePointer();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    private void OnTick(object? sender, EventArgs e) => UpdatePointer();

    private void UpdatePointer() =>
        SetPointerInside(_trackPointer && _window.IsVisible && IsCursorInside());

    private bool IsCursorInside()
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        return hwnd != IntPtr.Zero &&
            TaskbarNativeMethods.GetWindowRect(hwnd, out var rect) &&
            rect.Right > rect.Left &&
            rect.Bottom > rect.Top &&
            GetCursorPos(out var cursor) &&
            FloatingWindowHitSurface.ContainsPoint(
                rect.Left,
                rect.Top,
                rect.Right,
                rect.Bottom,
                cursor.X,
                cursor.Y);
    }

    private void SetPointerInside(bool inside)
    {
        // The brushes are frozen singletons, so the surface is only touched on a change.
        var brush = inside
            ? (System.Windows.Media.Brush)FloatingWindowHitSurface.Fill
            : System.Windows.Media.Brushes.Transparent;
        if (!ReferenceEquals(_root.Background, brush))
        {
            _root.Background = brush;
        }

        if (inside == _pointerInside)
        {
            return;
        }

        _pointerInside = inside;
        PointerInsideChanged?.Invoke(this, inside);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out TaskbarNativeMethods.NativePoint point);
}
