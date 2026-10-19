namespace TaskbarLyrics.App;

internal sealed record LyricsLayoutMetrics
{
    private const double BaseHostHorizontalPadding = 8;
    private const double BaseHostVerticalPadding = 3;
    private const double BaseMinimumContentHeight = 30;
    private const double BaseViewportDescenderBuffer = 2;
    private const double BaseLayoutHorizontalPadding = 0;
    private const double BaseLyricsPaneTopPadding = 3;
    private const double BaseLyricsPaneHorizontalPadding = 3;
    private const double BasePrimaryOffsetY = 1;
    private const double BaseSecondaryOffsetY = 1;
    private const double BaseLineTextBottomPadding = 2;
    private const double BaseSurfaceRadius = 8;
    private const double BaseLayerTransitionOffset = 2;
    private const double BaseCoverFallbackFontSize = 13;
    private const double BaseSpectrumWidth = 210;
    private const double BaseSpectrumHeight = 22;
    private const double BaseSpectrumGap = 3;
    private const double BaseSpectrumBarWidth = 3;
    private const double BaseSpectrumBarHeight = 8;
    private const double BaseSpectrumLowHeight = 6;
    private const double BaseSpectrumHighHeight = 18;
    private const double BaseSpectrumMiddleHeight = 11;
    private const double BaseWindowVerticalSpacing = 10;
    private const double BaseTextVerticalSpacing = 12;
    private const double BaseWindowMinimumHeight = 36;
    private const double SingleLineFontSizeMultiplier = 1.4;

    public required double ScalePercent { get; init; }

    public required double FontSize { get; init; }

    public required double CoverSize { get; init; }

    public required double CoverGap { get; init; }

    public required double CoverCornerRadius { get; init; }

    public required double HostHorizontalPadding { get; init; }

    public required double HostVerticalPadding { get; init; }

    public required double MinimumContentHeight { get; init; }

    public required double ViewportDescenderBuffer { get; init; }

    public required double LayoutHorizontalPadding { get; init; }

    public required double LyricsPaneTopPadding { get; init; }

    public required double LyricsPaneRightPadding { get; init; }

    public required double LyricsPaneLeftPadding { get; init; }

    public required double PrimaryOffsetY { get; init; }

    public required double SecondaryOffsetY { get; init; }

    public required double LineTextBottomPadding { get; init; }

    public required double SurfaceRadius { get; init; }

    public required double LayerTransitionOffset { get; init; }

    public required double CoverFallbackFontSize { get; init; }

    public required double SpectrumWidth { get; init; }

    public required double SpectrumHeight { get; init; }

    public required double SpectrumGap { get; init; }

    public required double SpectrumBarWidth { get; init; }

    public required double SpectrumBarHeight { get; init; }

    public required double SpectrumLowHeight { get; init; }

    public required double SpectrumHighHeight { get; init; }

    public required double SpectrumMiddleHeight { get; init; }

    public required double DesiredWindowHeight { get; init; }

    public double? LyricsPaneHeight { get; init; }

    public bool IsVertical { get; init; }

    public bool IsSingleLine { get; init; }

    public static LyricsLayoutMetrics Create(AppSettings settings, double pixelsPerDip = 1, TaskbarBounds? taskbar = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        pixelsPerDip = NormalizePixelsPerDip(pixelsPerDip);

        var scalePercent = AppSettings.ClampLyricsLayoutScalePercent(settings.GetLyricsLayoutScalePercent());
        var scale = scalePercent / 100;
        var baseFontSize = AppSettings.ClampFontSize(settings.FontSize);
        var baseCoverSize = AppSettings.ClampCoverSize(settings.CoverSize);
        var fontSize = baseFontSize * scale * (settings.SingleLineLyrics ? SingleLineFontSizeMultiplier : 1);
        var coverSize = AlignPixel(baseCoverSize * scale, pixelsPerDip);
        var coverGap = AlignPixel(AppSettings.ClampCoverGap(settings.CoverGap) * scale, pixelsPerDip);
        var baseCornerRadius = AppSettings.ClampCoverCornerRadius(settings.CoverCornerRadius, baseCoverSize);
        var coverCornerRadius = Math.Min(
            AlignPixel(baseCornerRadius * scale, pixelsPerDip),
            AlignPixelDown(coverSize / 2, pixelsPerDip));
        var hostHorizontalPadding = AlignPixel(BaseHostHorizontalPadding * scale, pixelsPerDip);
        var embeddedCoverVerticalSpacing = AlignPixel(BaseWindowVerticalSpacing * scale, pixelsPerDip);
        var coverVerticalSpacing = settings.UseFloatingWindow
            ? 2 * hostHorizontalPadding
            : embeddedCoverVerticalSpacing;
        var minimumWindowHeight = AlignPixel(BaseWindowMinimumHeight * scale, pixelsPerDip);
        var textHeight = fontSize * (settings.SingleLineLyrics ? 1.15 : 2.15) + (BaseTextVerticalSpacing * scale);
        double CalculateWindowHeight(double verticalSpacing) => AlignPixelUp(Math.Max(minimumWindowHeight,
            settings.ShowCover ? Math.Max(textHeight, coverSize + verticalSpacing) : textHeight), pixelsPerDip);
        var desiredWindowHeight = CalculateWindowHeight(coverVerticalSpacing);
        var hostVerticalPadding = AlignPixel(BaseHostVerticalPadding * scale, pixelsPerDip);
        var minimumContentHeight = AlignPixel(BaseMinimumContentHeight * scale, pixelsPerDip);
        var viewportDescenderBuffer = AlignPixel(BaseViewportDescenderBuffer * scale, pixelsPerDip);

        var metrics = new LyricsLayoutMetrics
        {
            IsSingleLine = settings.SingleLineLyrics,
            ScalePercent = scalePercent,
            FontSize = fontSize,
            CoverSize = coverSize,
            CoverGap = coverGap,
            CoverCornerRadius = coverCornerRadius,
            HostHorizontalPadding = hostHorizontalPadding,
            HostVerticalPadding = hostVerticalPadding,
            MinimumContentHeight = minimumContentHeight,
            ViewportDescenderBuffer = viewportDescenderBuffer,
            // Keep floating-card padding outside the same text viewport used by the embedded layout.
            LyricsPaneHeight = settings.UseFloatingWindow && !settings.SingleLineLyrics
                ? Math.Max(minimumContentHeight, CalculateWindowHeight(embeddedCoverVerticalSpacing) - 2 * hostVerticalPadding)
                    + viewportDescenderBuffer
                : null,
            LayoutHorizontalPadding = AlignPixel(BaseLayoutHorizontalPadding * scale, pixelsPerDip),
            LyricsPaneTopPadding = settings.SingleLineLyrics ? 0 : AlignPixel(BaseLyricsPaneTopPadding * scale, pixelsPerDip),
            LyricsPaneRightPadding = AlignPixel(BaseLyricsPaneHorizontalPadding * scale, pixelsPerDip),
            LyricsPaneLeftPadding = AlignPixel(BaseLyricsPaneHorizontalPadding * scale, pixelsPerDip),
            PrimaryOffsetY = settings.SingleLineLyrics ? 0 : AlignPixel(BasePrimaryOffsetY * scale, pixelsPerDip),
            SecondaryOffsetY = settings.SingleLineLyrics ? 0 : AlignPixel(BaseSecondaryOffsetY * scale, pixelsPerDip),
            LineTextBottomPadding = AlignPixel(BaseLineTextBottomPadding * scale, pixelsPerDip),
            SurfaceRadius = AlignPixel(BaseSurfaceRadius * scale, pixelsPerDip),
            LayerTransitionOffset = AlignPixel(BaseLayerTransitionOffset * scale, pixelsPerDip),
            CoverFallbackFontSize = BaseCoverFallbackFontSize * scale,
            SpectrumWidth = AlignPixel(BaseSpectrumWidth * scale, pixelsPerDip),
            SpectrumHeight = AlignPixel(BaseSpectrumHeight * scale, pixelsPerDip),
            SpectrumGap = AlignPixel(BaseSpectrumGap * scale, pixelsPerDip),
            SpectrumBarWidth = AlignPixel(BaseSpectrumBarWidth * scale, pixelsPerDip),
            SpectrumBarHeight = AlignPixel(BaseSpectrumBarHeight * scale, pixelsPerDip),
            SpectrumLowHeight = AlignPixel(BaseSpectrumLowHeight * scale, pixelsPerDip),
            SpectrumHighHeight = AlignPixel(BaseSpectrumHighHeight * scale, pixelsPerDip),
            SpectrumMiddleHeight = AlignPixel(BaseSpectrumMiddleHeight * scale, pixelsPerDip),
            DesiredWindowHeight = desiredWindowHeight
        };

        if (settings.UseFloatingWindow || taskbar is not { Width: > 0, Height: > 0 } bounds)
            return metrics;

        if (bounds.IsVertical)
        {
            var horizontalPadding = Math.Min(hostHorizontalPadding, AlignPixelDown(bounds.Width / 12, pixelsPerDip));
            var fittedCover = Math.Min(coverSize, Math.Max(0, bounds.Width - 2 * horizontalPadding));
            var columnWidth = (bounds.Width - 2 * horizontalPadding - metrics.LyricsPaneLeftPadding -
                metrics.LyricsPaneRightPadding) / (settings.SingleLineLyrics ? 1 : 2);
            return metrics with
            {
                IsVertical = true,
                HostHorizontalPadding = horizontalPadding,
                CoverSize = fittedCover,
                CoverCornerRadius = Math.Min(coverCornerRadius, fittedCover / 2),
                FontSize = Math.Min(fontSize, Math.Max(1, (columnWidth - metrics.LineTextBottomPadding) / 1.12)),
                MinimumContentHeight = 0,
                DesiredWindowHeight = Math.Min(bounds.Height, AppSettings.CalculateEffectiveWindowWidth(settings.WindowWidth))
            };
        }

        // Fit complete rows and native input bounds within a short taskbar, retaining
        // the user's line-count choice and saved layout values.
        if (bounds.Height < 40)
        {
            var verticalPadding = Math.Min(metrics.HostVerticalPadding, AlignPixelDown(bounds.Height / 8, pixelsPerDip));
            var availableHeight = Math.Max(1 / pixelsPerDip, bounds.Height - 2 * verticalPadding);
            var fittedCover = Math.Min(coverSize, availableHeight);
            return metrics with
            {
                HostVerticalPadding = verticalPadding,
                MinimumContentHeight = 0,
                CoverSize = fittedCover,
                CoverCornerRadius = Math.Min(coverCornerRadius, fittedCover / 2),
                FontSize = Math.Min(fontSize, Math.Max(1, ((availableHeight - metrics.LyricsPaneTopPadding) /
                    (settings.SingleLineLyrics ? 1 : 2) - metrics.PrimaryOffsetY - metrics.LineTextBottomPadding) / 1.12)),
                DesiredWindowHeight = bounds.Height
            };
        }

        return metrics;
    }

    private static double AlignPixel(double value, double pixelsPerDip)
    {
        return Math.Round(value * pixelsPerDip, MidpointRounding.AwayFromZero) / pixelsPerDip;
    }

    private static double AlignPixelDown(double value, double pixelsPerDip)
    {
        return Math.Floor(value * pixelsPerDip) / pixelsPerDip;
    }

    private static double AlignPixelUp(double value, double pixelsPerDip)
    {
        return Math.Ceiling(Math.Round(value * pixelsPerDip, 8)) / pixelsPerDip;
    }

    private static double NormalizePixelsPerDip(double pixelsPerDip)
    {
        return double.IsFinite(pixelsPerDip) && pixelsPerDip > 0 ? pixelsPerDip : 1;
    }
}
