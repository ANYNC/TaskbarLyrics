namespace TaskbarLyrics.App;

internal sealed record LyricsLayoutMetrics
{
    private const double BaseHostHorizontalPadding = 8;
    private const double BaseHostVerticalPadding = 3;
    private const double BaseMinimumContentHeight = 30;
    private const double BaseViewportDescenderBuffer = 2;
    private const double BaseLayoutHorizontalPadding = 4;
    private const double BaseLyricsPaneTopPadding = 3;
    private const double BaseLyricsPaneRightPadding = 4;
    private const double BaseLyricsPaneLeftPadding = 2;
    private const double BasePrimaryOffsetY = 1;
    private const double BaseSecondaryOffsetY = 2;
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

    public static LyricsLayoutMetrics Create(AppSettings settings, double pixelsPerDip = 1)
    {
        ArgumentNullException.ThrowIfNull(settings);
        pixelsPerDip = NormalizePixelsPerDip(pixelsPerDip);

        var scalePercent = AppSettings.ClampLyricsLayoutScalePercent(settings.LyricsLayoutScalePercent);
        var scale = scalePercent / 100;
        var baseFontSize = AppSettings.ClampFontSize(settings.FontSize);
        var baseCoverSize = AppSettings.ClampCoverSize(settings.CoverSize);
        var fontSize = baseFontSize * scale;
        var coverSize = AlignPixel(baseCoverSize * scale, pixelsPerDip);
        var coverGap = AlignPixel(AppSettings.ClampCoverGap(settings.CoverGap) * scale, pixelsPerDip);
        var baseCornerRadius = AppSettings.ClampCoverCornerRadius(settings.CoverCornerRadius, baseCoverSize);
        var coverCornerRadius = Math.Min(
            AlignPixel(baseCornerRadius * scale, pixelsPerDip),
            AlignPixelDown(coverSize / 2, pixelsPerDip));
        var coverVerticalSpacing = AlignPixel(BaseWindowVerticalSpacing * scale, pixelsPerDip);
        var minimumWindowHeight = AlignPixel(BaseWindowMinimumHeight * scale, pixelsPerDip);
        var textHeight = fontSize * 2.15 + (BaseTextVerticalSpacing * scale);
        var contentHeight = settings.ShowCover
            ? Math.Max(textHeight, coverSize + coverVerticalSpacing)
            : textHeight;
        var desiredWindowHeight = AlignPixelUp(Math.Max(
            minimumWindowHeight,
            contentHeight), pixelsPerDip);

        return new LyricsLayoutMetrics
        {
            ScalePercent = scalePercent,
            FontSize = fontSize,
            CoverSize = coverSize,
            CoverGap = coverGap,
            CoverCornerRadius = coverCornerRadius,
            HostHorizontalPadding = AlignPixel(BaseHostHorizontalPadding * scale, pixelsPerDip),
            HostVerticalPadding = AlignPixel(BaseHostVerticalPadding * scale, pixelsPerDip),
            MinimumContentHeight = AlignPixel(BaseMinimumContentHeight * scale, pixelsPerDip),
            ViewportDescenderBuffer = AlignPixel(BaseViewportDescenderBuffer * scale, pixelsPerDip),
            LayoutHorizontalPadding = AlignPixel(BaseLayoutHorizontalPadding * scale, pixelsPerDip),
            LyricsPaneTopPadding = AlignPixel(BaseLyricsPaneTopPadding * scale, pixelsPerDip),
            LyricsPaneRightPadding = AlignPixel(BaseLyricsPaneRightPadding * scale, pixelsPerDip),
            LyricsPaneLeftPadding = AlignPixel(BaseLyricsPaneLeftPadding * scale, pixelsPerDip),
            PrimaryOffsetY = AlignPixel(BasePrimaryOffsetY * scale, pixelsPerDip),
            SecondaryOffsetY = AlignPixel(BaseSecondaryOffsetY * scale, pixelsPerDip),
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
        return Math.Ceiling(value * pixelsPerDip) / pixelsPerDip;
    }

    private static double NormalizePixelsPerDip(double pixelsPerDip)
    {
        return double.IsFinite(pixelsPerDip) && pixelsPerDip > 0 ? pixelsPerDip : 1;
    }
}
