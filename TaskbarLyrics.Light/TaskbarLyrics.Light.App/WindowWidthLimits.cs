using System.Windows;

namespace TaskbarLyrics.Light.App;

internal static class WindowWidthLimits
{
    public const double Min = 320;

    private const double WorkAreaHorizontalMargin = 48;

    public static double GetMaxForScreen()
    {
        var workAreaWidth = SystemParameters.WorkArea.Width;
        if (workAreaWidth > WorkAreaHorizontalMargin + Min)
        {
            return workAreaWidth - WorkAreaHorizontalMargin;
        }

        return Math.Max(Min, SystemParameters.PrimaryScreenWidth - WorkAreaHorizontalMargin);
    }

    public static double Clamp(double width) => Math.Clamp(width, Min, GetMaxForScreen());
}
