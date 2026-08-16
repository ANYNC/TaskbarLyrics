using System.Globalization;
using System.Windows.Media;

namespace TaskbarLyrics.App;

internal static class LyricsStyleScriptFactory
{
    public static string Create(AppSettings settings, double pixelsPerDip)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.NormalizeLyricsTextAlignment();
        var primaryColor = ParseColor(settings.ForegroundColor);
        var secondaryColor = ForegroundColorPolicy.CreateSecondaryColor(primaryColor);
        var translationColor = ForegroundColorPolicy.CreateTranslationColor(primaryColor);
        var metrics = LyricsLayoutMetrics.Create(settings, pixelsPerDip);
        var stylePayload = new
        {
            fontFamily = AppSettings.NormalizeFontFamily(settings.FontFamily),
            layoutScalePercent = metrics.ScalePercent,
            fontSize = metrics.FontSize,
            showCover = settings.ShowCover,
            coverSize = metrics.CoverSize,
            coverGap = metrics.CoverGap,
            coverCornerRadius = metrics.CoverCornerRadius,
            viewportDescenderBuffer = metrics.ViewportDescenderBuffer,
            layoutHorizontalPadding = metrics.LayoutHorizontalPadding,
            lyricsPaneTopPadding = metrics.LyricsPaneTopPadding,
            lyricsPaneRightPadding = metrics.LyricsPaneRightPadding,
            lyricsPaneLeftPadding = metrics.LyricsPaneLeftPadding,
            primaryOffsetY = metrics.PrimaryOffsetY,
            secondaryOffsetY = metrics.SecondaryOffsetY,
            lineTextBottomPadding = metrics.LineTextBottomPadding,
            surfaceRadius = metrics.SurfaceRadius,
            layerTransitionOffset = metrics.LayerTransitionOffset,
            coverFallbackFontSize = metrics.CoverFallbackFontSize,
            spectrumWidth = metrics.SpectrumWidth,
            spectrumHeight = metrics.SpectrumHeight,
            spectrumGap = metrics.SpectrumGap,
            spectrumBarWidth = metrics.SpectrumBarWidth,
            spectrumBarHeight = metrics.SpectrumBarHeight,
            spectrumLowHeight = metrics.SpectrumLowHeight,
            spectrumHighHeight = metrics.SpectrumHighHeight,
            spectrumMiddleHeight = metrics.SpectrumMiddleHeight,
            fontWeight = settings.FontWeight,
            primaryColor = ToCssColor(primaryColor),
            secondaryColor = ToCssColor(secondaryColor),
            translationColor = ToCssColor(translationColor),
            surfaceColor = settings.ShowBackground
                ? $"rgba(18, 18, 24, {Math.Clamp(settings.BackgroundOpacity, 0, 1).ToString("0.####", CultureInfo.InvariantCulture)})"
                : "transparent",
            surfaceShadow = settings.ShowBorder
                ? "inset 0 0 0 1px rgba(255, 255, 255, 0.16)"
                : "none",
            textShadow = settings.ShowTextShadow
                ? "0 1px 2px rgba(0, 0, 0, 0.36)"
                : "none",
            textAlignment = settings.LyricsTextAlignment
        };

        return WebViewMessageScriptFactory.Dispatch("taskbarLyrics", "style", stylePayload);
    }

    private static System.Windows.Media.Color ParseColor(string color)
    {
        try
        {
            return new BrushConverter().ConvertFromString(color) is SolidColorBrush brush
                ? brush.Color
                : Colors.White;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or NotSupportedException)
        {
            return Colors.White;
        }
    }

    private static string ToCssColor(System.Windows.Media.Color color) =>
        $"rgba({color.R}, {color.G}, {color.B}, {(color.A / 255.0).ToString("0.###", CultureInfo.InvariantCulture)})";
}

internal readonly record struct LyricsPresentationCommand(string Slot, string Script);
