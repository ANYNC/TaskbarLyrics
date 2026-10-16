using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using SystemColors = System.Windows.SystemColors;

namespace TaskbarLyrics.App;

internal partial class FloatingResizeGripWindow : Window
{
    internal event Action<Point>? AdjustmentStarted;
    internal event Action<Point>? AdjustmentMoved;
    internal event Action<bool>? AdjustmentCompleted;

    internal FloatingResizeGripWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
        SourceInitialized += (_, _) => TaskbarPlacementService.ApplyToolWindowStyle(new WindowInteropHelper(this).Handle);
    }

    internal void Position(Rect card, double scale, bool visible, bool light)
    {
        if (!visible) { Hide(); return; }
        Resources["GripForeground"] = SystemParameters.HighContrast ? SystemColors.WindowTextBrush
            : new SolidColorBrush(light ? Color.FromRgb(28, 29, 32) : Color.FromRgb(246, 247, 248));
        Topmost = Owner.Topmost;
        Left = card.Right / scale - Width;
        Top = card.Top / scale;
        if (!IsVisible) Show();
        TaskbarNativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
            (int)Math.Round(card.Right - Width * scale), (int)Math.Round(card.Top),
            (int)Math.Round(Width * scale), (int)Math.Round(Height * scale),
            TaskbarNativeMethods.SWP_NOACTIVATE | TaskbarNativeMethods.SWP_NOZORDER);
    }

    internal void CancelAdjustment()
    {
        if (Grip.IsDragging) Grip.CancelDrag();
    }

    private void OnDragStarted(object sender, DragStartedEventArgs e)
    {
        Activate();
        Grip.Focus();
        if (NativeWindowInteraction.GetCursorPosition() is { } position) AdjustmentStarted?.Invoke(position);
    }
    private void OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        // The grip HWND moves during preview; WPF's cached local mouse coordinates can lag behind it.
        if (NativeWindowInteraction.GetCursorPosition() is { } position) AdjustmentMoved?.Invoke(position);
    }
    private void OnDragCompleted(object sender, DragCompletedEventArgs e) => AdjustmentCompleted?.Invoke(e.Canceled);
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !Grip.IsDragging) return;
        e.Handled = true;
        CancelAdjustment();
    }
    protected override void OnClosed(EventArgs e)
    {
        AdjustmentStarted = null;
        AdjustmentMoved = null;
        AdjustmentCompleted = null;
        base.OnClosed(e);
    }
}
