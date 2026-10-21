using System.Windows;
using System.Windows.Media.Animation;

namespace TaskbarLyrics.App;

internal static class FloatingPanelAnimation
{
    internal static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(180);
    internal static bool IsEnabled => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;

    internal static void Fade(UIElement visual, bool visible, bool immediate, Action? completed = null, bool preserveDuration = false)
    {
        var from = visual.Opacity;
        var to = visible ? 1.0 : 0.0;
        visual.BeginAnimation(UIElement.OpacityProperty, null);
        // A new WPF clock may remain inactive until the next render tick.
        // Keep its base value at the sampled opacity so reversal cannot flash the endpoint.
        visual.Opacity = from;
        if (immediate || !IsEnabled || !preserveDuration && Math.Abs(from - to) < 0.001)
        {
            visual.Opacity = to;
            completed?.Invoke();
            return;
        }
        var animation = new DoubleAnimation(from, to, Duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        if (completed is not null) animation.Completed += (_, _) => completed();
        visual.BeginAnimation(UIElement.OpacityProperty, animation);
    }
}
