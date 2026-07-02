using System.Windows.Media.Animation;
using TaskbarLyrics.Light.App.Ui;

namespace TaskbarLyrics.Light.App;

internal static class LightMotion
{
    public static TimeSpan AnchorDebounce { get; } = TimeSpan.FromMilliseconds(45);

    public static TimeSpan PreviewDebounce { get; } = TimeSpan.FromMilliseconds(45);

    public static int LyricsTransitionMs(AnimationIntensity intensity) => intensity switch
    {
        AnimationIntensity.Reduced => 260,
        AnimationIntensity.Smooth => 720,
        _ => 560
    };

    public static int LayerFadeMs(AnimationIntensity intensity) => intensity switch
    {
        AnimationIntensity.Reduced => 160,
        AnimationIntensity.Smooth => 420,
        _ => 300
    };

    public static int CoverFadeMs(AnimationIntensity intensity) => intensity switch
    {
        AnimationIntensity.Reduced => 220,
        AnimationIntensity.Smooth => 620,
        _ => 440
    };

    public static int ColorTransitionMs(AnimationIntensity intensity) => intensity switch
    {
        AnimationIntensity.Reduced => 110,
        AnimationIntensity.Smooth => 360,
        _ => 260
    };

    public static int ProgressVisibilityMs(AnimationIntensity intensity) => intensity switch
    {
        AnimationIntensity.Reduced => 120,
        AnimationIntensity.Smooth => 360,
        _ => 240
    };

    public static double ProgressFollowRate(AnimationIntensity intensity) => intensity switch
    {
        AnimationIntensity.Reduced => 18,
        AnimationIntensity.Smooth => 8.5,
        _ => 12
    };

    public static int WindowVisibilityMs(AnimationIntensity intensity) => intensity switch
    {
        AnimationIntensity.Reduced => 120,
        AnimationIntensity.Smooth => 240,
        _ => 180
    };

    public static int MenuOpenMs(AnimationIntensity intensity) => intensity switch
    {
        AnimationIntensity.Reduced => 90,
        AnimationIntensity.Smooth => 180,
        _ => 130
    };

    public static int SubmenuOpenMs(AnimationIntensity intensity) => intensity switch
    {
        AnimationIntensity.Reduced => 80,
        AnimationIntensity.Smooth => 150,
        _ => 110
    };

    public static IEasingFunction CreateMoveEase() => new CubicBezierEasing(0.22, 0.72, 0.24, 1);

    public static IEasingFunction CreateFadeEase() => new CubicBezierEasing(0.16, 0.78, 0.22, 1);

    public static IEasingFunction CreateSoftEase() => new CubicBezierEasing(0.24, 0.64, 0.28, 1);

    public static double MoveEase(double progress) => Ease(0.22, 0.72, 0.24, 1, progress);

    public static double FadeEase(double progress) => Ease(0.16, 0.78, 0.22, 1, progress);

    public static double SoftEase(double progress) => Ease(0.24, 0.64, 0.28, 1, progress);

    private static double Ease(double x1, double y1, double x2, double y2, double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        if (progress <= 0) return 0;
        if (progress >= 1) return 1;

        var t = progress;
        for (var i = 0; i < 8; i++)
        {
            var x = SampleCurve(x1, x2, t) - progress;
            if (Math.Abs(x) < 1e-5)
            {
                break;
            }

            var dx = SampleCurveDerivative(x1, x2, t);
            if (Math.Abs(dx) < 1e-6)
            {
                break;
            }

            t -= x / dx;
            t = Math.Clamp(t, 0, 1);
        }

        return Math.Clamp(SampleCurve(y1, y2, t), 0, 1);
    }

    private static double SampleCurve(double a1, double a2, double t) =>
        ((1 - 3 * a2 + 3 * a1) * t + (3 * a2 - 6 * a1)) * t * t + (3 * a1) * t;

    private static double SampleCurveDerivative(double a1, double a2, double t) =>
        (3 * (1 - 3 * a2 + 3 * a1) * t + 2 * (3 * a2 - 6 * a1)) * t + (3 * a1);
}
