using System.Globalization;
using System.Windows.Media;

namespace TaskbarLyrics.App;

internal static class LyricsStyleScriptFactory
{
    public static string Create(AppSettings settings, double pixelsPerDip)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.NormalizeLyricsTextAlignment();
        settings.NormalizeCoverPosition();
        // The floating surface picks white or black from its own background density;
        // both the lyric text and the word-scan/spectrum accents derive from this colour.
        var foregroundColor = ParseColor(settings.ResolveEffectiveForegroundColor());
        var primaryColor = ForegroundColorPolicy.CreatePrimaryColor(foregroundColor);
        var secondaryColor = ForegroundColorPolicy.CreateSecondaryColor(foregroundColor);
        var translationColor = ForegroundColorPolicy.CreateTranslationColor(foregroundColor);
        var wordScanOverlayColor = ForegroundColorPolicy.CreateWordScanOverlayColor(foregroundColor);
        var metrics = LyricsLayoutMetrics.Create(settings, pixelsPerDip);
        var floatingWindow = settings.UseFloatingWindow;
        var floatingBackground = settings.FloatingWindowBackground ?? new FloatingWindowBackgroundSettings();
        var showBackground = floatingWindow ? floatingBackground.ShowBackground : settings.ShowBackground;
        var backgroundTone = floatingWindow ? floatingBackground.Tone : settings.BackgroundTone;
        var showBorder = floatingWindow ? floatingBackground.ShowBorder : settings.ShowBorder;
        var stylePayload = new
        {
            fontFamily = AppSettings.NormalizeFontFamily(settings.FontFamily),
            layoutScalePercent = metrics.ScalePercent,
            fontSize = metrics.FontSize,
            showCover = settings.ShowCover,
            coverPosition = settings.CoverPosition,
            enableControlPanel = settings.EnableControlPanel && !floatingWindow,
            freeDrag = floatingWindow && settings.FloatingWindowFreeDrag && !settings.FloatingWindowClickThrough,
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
            wordScanOverlayColor = ToCssColor(wordScanOverlayColor),
            surfaceColor = showBackground ? CreateSurfaceColor(backgroundTone) : "transparent",
            surfaceShadow = showBorder
                ? "inset 0 0 0 1px rgba(255, 255, 255, 0.16)"
                : "none",
            textShadow = settings.ShowTextShadow
                ? "0 1px 2px rgba(0, 0, 0, 0.36)"
                : "none",
            textAlignment = settings.LyricsTextAlignment
        };

        return WebViewMessageScriptFactory.Dispatch("taskbarLyrics", "style", stylePayload);
    }

    private static string CreateSurfaceColor(double tone)
    {
        var clamped = AppSettings.ClampBackgroundTone(tone);
        if (clamped == 0)
        {
            return "transparent";
        }

        var channel = clamped < 0 ? "255, 255, 255" : "0, 0, 0";
        var alpha = Math.Abs(clamped) / 100.0;
        return $"rgba({channel}, {alpha.ToString("0.###", CultureInfo.InvariantCulture)})";
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
