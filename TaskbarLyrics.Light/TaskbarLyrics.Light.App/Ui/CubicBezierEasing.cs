using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace TaskbarLyrics.Light.App.Ui;

/// <summary>
/// 对应 CSS cubic-bezier(x1, y1, x2, y2)
/// </summary>
public sealed class CubicBezierEasing : EasingFunctionBase
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; } = 1;

    public CubicBezierEasing(double x1, double y1, double x2, double y2)
    {
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
    }

    public CubicBezierEasing() { }

    protected override Freezable CreateInstanceCore() => new CubicBezierEasing();

    protected override double EaseInCore(double normalizedTime)
    {
        if (normalizedTime <= 0) return 0;
        if (normalizedTime >= 1) return 1;

        var t = normalizedTime;
        for (var i = 0; i < 8; i++)
        {
            var x = SampleCurveX(t) - normalizedTime;
            if (Math.Abs(x) < 1e-5)
            {
                break;
            }

            var dx = SampleCurveDerivativeX(t);
            if (Math.Abs(dx) < 1e-6)
            {
                break;
            }

            t -= x / dx;
        }

        return SampleCurveY(t);
    }

    private double SampleCurveX(double t) =>
        ((1 - 3 * X2 + 3 * X1) * t + (3 * X2 - 6 * X1)) * t * t + (3 * X1) * t;

    private double SampleCurveY(double t) =>
        ((1 - 3 * Y2 + 3 * Y1) * t + (3 * Y2 - 6 * Y1)) * t * t + (3 * Y1) * t;

    private double SampleCurveDerivativeX(double t) =>
        (3 * (1 - 3 * X2 + 3 * X1) * t + 2 * (3 * X2 - 6 * X1)) * t + (3 * X1);
}
