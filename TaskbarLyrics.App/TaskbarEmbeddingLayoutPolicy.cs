namespace TaskbarLyrics.App;

internal readonly record struct TaskbarEmbeddingDisplayConstraint(
    double MaxWidth,
    double MaxHeight,
    double PixelsPerDip);

internal readonly record struct TaskbarEmbeddingConstraints(
    double MaxWidth,
    double MaxHeight)
{
    public bool IsSupported =>
        double.IsFinite(MaxWidth) &&
        double.IsFinite(MaxHeight) &&
        MaxWidth > 0 &&
        MaxHeight > 0;

    public IReadOnlyList<TaskbarEmbeddingDisplayConstraint> Displays { get; init; } = [];

    public static TaskbarEmbeddingConstraints FromDisplays(
        IReadOnlyList<DisplayMonitor> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        if (displays.Count == 0)
        {
            return new TaskbarEmbeddingConstraints(0, 0);
        }

        var maxWidth = double.PositiveInfinity;
        var maxHeight = double.PositiveInfinity;
        var displayConstraints = new List<TaskbarEmbeddingDisplayConstraint>(displays.Count);
        foreach (var display in displays)
        {
            var pixelsPerDip = double.IsFinite(display.PixelsPerDip) && display.PixelsPerDip > 0
                ? display.PixelsPerDip
                : 1;
            var logicalDisplay = TaskbarPlacementService.ConvertPhysicalDisplayMetrics(
                display.Bounds,
                display.WorkArea,
                pixelsPerDip);
            var taskbar = TaskbarPlacementService.ResolveTaskbarBounds(logicalDisplay);
            maxWidth = Math.Min(maxWidth, taskbar.Width);
            maxHeight = Math.Min(maxHeight, taskbar.Height);
            displayConstraints.Add(new TaskbarEmbeddingDisplayConstraint(
                taskbar.Width,
                taskbar.Height,
                pixelsPerDip));
        }

        return new TaskbarEmbeddingConstraints(maxWidth, maxHeight)
        {
            Displays = displayConstraints
        };
    }
}

internal readonly record struct TaskbarEmbeddingInputBounds(
    bool IsSupported,
    double MaxTaskbarWidth,
    double MaxTaskbarHeight,
    double MaxScalePercent,
    double MaxFontSize,
    double MaxCoverSize,
    double MaxCoverGap,
    double MaxWindowWidth,
    double MinXOffset,
    double MaxXOffset,
    double MinYOffset,
    double MaxYOffset)
{
    public static TaskbarEmbeddingInputBounds Unconstrained(double fallbackWidth)
    {
        return new TaskbarEmbeddingInputBounds(
            false,
            fallbackWidth,
            100000,
            AppSettings.MaximumLyricsLayoutScalePercent,
            AppSettings.ExtendedFontSizeMax,
            AppSettings.ExtendedCoverSizeMax,
            AppSettings.CoverGapMax,
            AppSettings.MaximumWindowWidth,
            AppSettings.MinimumWindowOffset,
            AppSettings.MaximumWindowOffset,
            AppSettings.MinimumWindowOffset,
            AppSettings.MaximumWindowOffset);
    }
}

internal readonly record struct TaskbarEmbeddingLayoutResult(
    bool CanEmbed,
    bool Changed,
    string Message);

internal static class TaskbarEmbeddingLayoutPolicy
{
    private const double Epsilon = 0.01;
    private const double DefaultSafeScalePercent = AppSettings.DefaultLyricsLayoutScalePercent;

    public static TaskbarEmbeddingConstraints FromDisplays(
        IReadOnlyList<DisplayMonitor> displays) =>
        TaskbarEmbeddingConstraints.FromDisplays(displays);

    public static TaskbarEmbeddingConstraints FromDisplay(DisplayMonitor display) =>
        FromDisplays([display]);

    public static bool CanFit(
        AppSettings settings,
        TaskbarEmbeddingConstraints constraints)
    {
        return CanFitCore(settings, constraints, includeOffsets: true);
    }

    private static bool CanFitCore(
        AppSettings settings,
        TaskbarEmbeddingConstraints constraints,
        bool includeOffsets)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!constraints.IsSupported || constraints.MaxWidth < AppSettings.MinimumWindowWidth)
        {
            return false;
        }

        var effectiveWidth = AppSettings.CalculateEffectiveWindowWidth(
            settings.WindowWidth,
            settings.LyricsLayoutScalePercent);
        var displayConstraints = GetDisplayConstraints(constraints);
        foreach (var display in displayConstraints)
        {
            var metrics = LyricsLayoutMetrics.Create(settings, display.PixelsPerDip);
            if (effectiveWidth > display.MaxWidth + Epsilon ||
                metrics.DesiredWindowHeight > display.MaxHeight + Epsilon)
            {
                return false;
            }

            if (includeOffsets)
            {
                var horizontalRange = GetHorizontalOffsetRange(
                    display.MaxWidth,
                    effectiveWidth,
                    settings.HorizontalAnchor);
                var verticalRange = GetVerticalOffsetRange(
                    display.MaxHeight,
                    metrics.DesiredWindowHeight);
                if (settings.XOffset < horizontalRange.Min - Epsilon ||
                    settings.XOffset > horizontalRange.Max + Epsilon ||
                    settings.YOffset < verticalRange.Min - Epsilon ||
                    settings.YOffset > verticalRange.Max + Epsilon)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static TaskbarEmbeddingLayoutResult NormalizeForEmbedding(
        AppSettings settings,
        TaskbarEmbeddingConstraints constraints)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (CanFit(settings, constraints))
        {
            return new TaskbarEmbeddingLayoutResult(true, false, string.Empty);
        }

        var safeSettings = settings.Clone();
        ResetLayoutValues(safeSettings);
        if (!constraints.IsSupported || constraints.MaxWidth < AppSettings.MinimumWindowWidth)
        {
            return new TaskbarEmbeddingLayoutResult(
                false,
                false,
                "当前任务栏空间不足，无法嵌入显示。请切换到悬浮窗口。");
        }

        safeSettings.LyricsLayoutScalePercent = FindMaximumScalePercent(
            safeSettings,
            constraints,
            DefaultSafeScalePercent);
        safeSettings.WindowWidth = FindSafeBaseWindowWidth(
            safeSettings.LyricsLayoutScalePercent,
            constraints.MaxWidth);
        if (!CanFit(safeSettings, constraints))
        {
            return new TaskbarEmbeddingLayoutResult(
                false,
                false,
                "当前任务栏空间不足，无法嵌入显示。请切换到悬浮窗口。");
        }

        CopyLayoutValues(safeSettings, settings);
        return new TaskbarEmbeddingLayoutResult(
            true,
            true,
            "当前布局超出任务栏，已恢复默认尺寸与位置，并按任务栏空间调整。");
    }

    public static void ClampToConstraints(
        AppSettings settings,
        TaskbarEmbeddingConstraints constraints)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!constraints.IsSupported || constraints.MaxWidth < AppSettings.MinimumWindowWidth)
        {
            return;
        }

        settings.NormalizeLyricsLayout();
        settings.NormalizeWindowLayout();
        if (CanFit(settings, constraints))
        {
            return;
        }

        settings.LyricsLayoutScalePercent = FindMaximumScalePercent(
            settings,
            constraints,
            settings.LyricsLayoutScalePercent);
        settings.WindowWidth = Math.Clamp(
            Math.Min(
                settings.WindowWidth,
                constraints.MaxWidth / (settings.LyricsLayoutScalePercent / 100.0)),
            AppSettings.MinimumWindowWidth,
            AppSettings.MaximumWindowWidth);
        settings.FontSize = Math.Min(
            settings.FontSize,
            FindMaximumFontSize(settings, constraints));
        settings.CoverSize = Math.Min(
            settings.CoverSize,
            FindMaximumCoverSize(settings, constraints));
        settings.CoverGap = Math.Min(
            settings.CoverGap,
            FindMaximumCoverGap(settings, constraints));
        settings.CoverCornerRadius = AppSettings.ClampCoverCornerRadius(
            settings.CoverCornerRadius,
            settings.CoverSize);

        var effectiveWidth = AppSettings.CalculateEffectiveWindowWidth(
            settings.WindowWidth,
            settings.LyricsLayoutScalePercent);
        var horizontalRange = GetHorizontalOffsetRange(
            constraints.MaxWidth,
            effectiveWidth,
            settings.HorizontalAnchor);
        var verticalRange = GetStrictestVerticalOffsetRange(constraints, settings);
        settings.XOffset = Math.Clamp(settings.XOffset, horizontalRange.Min, horizontalRange.Max);
        settings.YOffset = Math.Clamp(settings.YOffset, verticalRange.Min, verticalRange.Max);
    }

    public static TaskbarEmbeddingInputBounds GetInputBounds(
        AppSettings settings,
        TaskbarEmbeddingConstraints constraints,
        double fallbackWidth)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!constraints.IsSupported || constraints.MaxWidth < AppSettings.MinimumWindowWidth)
        {
            return constraints.IsSupported
                ? new TaskbarEmbeddingInputBounds(
                    false,
                    constraints.MaxWidth,
                    constraints.MaxHeight,
                    AppSettings.MaximumLyricsLayoutScalePercent,
                    AppSettings.ExtendedFontSizeMax,
                    AppSettings.ExtendedCoverSizeMax,
                    AppSettings.CoverGapMax,
                    AppSettings.MaximumWindowWidth,
                    AppSettings.MinimumWindowOffset,
                    AppSettings.MaximumWindowOffset,
                    AppSettings.MinimumWindowOffset,
                    AppSettings.MaximumWindowOffset)
                : TaskbarEmbeddingInputBounds.Unconstrained(fallbackWidth);
        }

        var normalized = settings.Clone();
        normalized.NormalizeLyricsLayout();
        normalized.NormalizeWindowLayout();
        var maxScale = FindMaximumScalePercent(
            normalized,
            constraints,
            AppSettings.MaximumLyricsLayoutScalePercent);
        var maxFontSize = FindMaximumFontSize(normalized, constraints);
        var maxCoverSize = FindMaximumCoverSize(normalized, constraints);
        var maxCoverGap = FindMaximumCoverGap(normalized, constraints);
        var scale = normalized.LyricsLayoutScalePercent / 100.0;
        var maxWindowWidth = Math.Clamp(
            constraints.MaxWidth / scale,
            AppSettings.MinimumWindowWidth,
            AppSettings.MaximumWindowWidth);
        var effectiveWidth = Math.Min(
            AppSettings.CalculateEffectiveWindowWidth(normalized.WindowWidth, normalized.LyricsLayoutScalePercent),
            constraints.MaxWidth);
        var horizontalRange = GetHorizontalOffsetRange(
            constraints.MaxWidth,
            effectiveWidth,
            normalized.HorizontalAnchor);
        var verticalRange = GetStrictestVerticalOffsetRange(constraints, normalized);
        return new TaskbarEmbeddingInputBounds(
            constraints.MaxWidth >= AppSettings.MinimumWindowWidth,
            constraints.MaxWidth,
            constraints.MaxHeight,
            maxScale,
            maxFontSize,
            maxCoverSize,
            maxCoverGap,
            maxWindowWidth,
            horizontalRange.Min,
            horizontalRange.Max,
            verticalRange.Min,
            verticalRange.Max);
    }

    private static void ResetLayoutValues(AppSettings settings)
    {
        settings.FontSize = AppSettings.DefaultFontSize;
        settings.CoverSize = AppSettings.DefaultCoverSize;
        settings.CoverGap = AppSettings.DefaultCoverGap;
        settings.CoverCornerRadius = AppSettings.DefaultCoverCornerRadius;
        settings.LyricsLayoutScalePercent = AppSettings.DefaultLyricsLayoutScalePercent;
        settings.WindowWidth = AppSettings.DefaultWindowWidth;
        settings.XOffset = 0;
        settings.YOffset = 0;
    }

    private static void CopyLayoutValues(AppSettings source, AppSettings target)
    {
        target.FontSize = source.FontSize;
        target.CoverSize = source.CoverSize;
        target.CoverGap = source.CoverGap;
        target.CoverCornerRadius = source.CoverCornerRadius;
        target.LyricsLayoutScalePercent = source.LyricsLayoutScalePercent;
        target.WindowWidth = source.WindowWidth;
        target.XOffset = source.XOffset;
        target.YOffset = source.YOffset;
    }

    private static double FindMaximumScalePercent(
        AppSettings settings,
        TaskbarEmbeddingConstraints constraints,
        double upperBound)
    {
        var lowerBound = AppSettings.MinimumLyricsLayoutScalePercent;
        var upper = Math.Clamp(upperBound, lowerBound, AppSettings.MaximumLyricsLayoutScalePercent);
        var best = lowerBound;
        for (var step = 0; step <= 275; step++)
        {
            var candidate = lowerBound + step;
            if (candidate > upper + Epsilon)
            {
                break;
            }

            var candidateSettings = settings.Clone();
            candidateSettings.LyricsLayoutScalePercent = candidate;
            if (CanFitIgnoringOffsets(candidateSettings, constraints))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static double FindMaximumFontSize(
        AppSettings settings,
        TaskbarEmbeddingConstraints constraints)
    {
        var best = AppSettings.ExtendedFontSizeMin;
        for (var step = 0; step <= 900; step++)
        {
            var candidate = AppSettings.ExtendedFontSizeMin + (step / 10.0);
            var candidateSettings = settings.Clone();
            candidateSettings.FontSize = candidate;
            if (CanFitIgnoringOffsets(candidateSettings, constraints))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static double FindMaximumCoverSize(
        AppSettings settings,
        TaskbarEmbeddingConstraints constraints)
    {
        var best = AppSettings.ExtendedCoverSizeMin;
        for (var candidate = AppSettings.ExtendedCoverSizeMin;
             candidate <= AppSettings.ExtendedCoverSizeMax;
             candidate++)
        {
            var candidateSettings = settings.Clone();
            candidateSettings.CoverSize = candidate;
            candidateSettings.CoverCornerRadius = AppSettings.ClampCoverCornerRadius(
                candidateSettings.CoverCornerRadius,
                candidate);
            if (CanFitIgnoringOffsets(candidateSettings, constraints))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static double FindMaximumCoverGap(
        AppSettings settings,
        TaskbarEmbeddingConstraints constraints)
    {
        var metrics = LyricsLayoutMetrics.Create(settings);
        var availableWidth = constraints.MaxWidth -
            (2 * metrics.HostHorizontalPadding) -
            (2 * metrics.LayoutHorizontalPadding) -
            metrics.CoverSize;
        var scale = metrics.ScalePercent / 100.0;
        if (!double.IsFinite(availableWidth) || availableWidth <= 0 || scale <= 0)
        {
            return 0;
        }

        return Math.Clamp(
            availableWidth / scale,
            AppSettings.CoverGapMin,
            AppSettings.CoverGapMax);
    }

    private static bool CanFitIgnoringOffsets(
        AppSettings settings,
        TaskbarEmbeddingConstraints constraints)
    {
        return CanFitCore(settings, constraints, includeOffsets: false);
    }

    private static double FindSafeBaseWindowWidth(double scalePercent, double maxWidth)
    {
        var scale = AppSettings.ClampLyricsLayoutScalePercent(scalePercent) / 100.0;
        return Math.Clamp(
            Math.Min(AppSettings.DefaultWindowWidth, maxWidth / scale),
            AppSettings.MinimumWindowWidth,
            AppSettings.MaximumWindowWidth);
    }

    private static OffsetRange GetHorizontalOffsetRange(
        double taskbarWidth,
        double windowWidth,
        LyricsHorizontalAnchor anchor)
    {
        var remaining = Math.Max(0, taskbarWidth - windowWidth);
        return anchor switch
        {
            LyricsHorizontalAnchor.Left => new OffsetRange(0, remaining),
            LyricsHorizontalAnchor.Center => new OffsetRange(-remaining / 2, remaining / 2),
            _ => new OffsetRange(-remaining, 0)
        };
    }

    private static OffsetRange GetVerticalOffsetRange(double taskbarHeight, double windowHeight)
    {
        var remaining = Math.Max(0, taskbarHeight - windowHeight);
        return new OffsetRange(-remaining / 2, remaining / 2);
    }

    private static OffsetRange GetStrictestVerticalOffsetRange(
        TaskbarEmbeddingConstraints constraints,
        AppSettings settings)
    {
        var minimum = double.NegativeInfinity;
        var maximum = double.PositiveInfinity;
        foreach (var display in GetDisplayConstraints(constraints))
        {
            var metrics = LyricsLayoutMetrics.Create(settings, display.PixelsPerDip);
            var range = GetVerticalOffsetRange(display.MaxHeight, metrics.DesiredWindowHeight);
            minimum = Math.Max(minimum, range.Min);
            maximum = Math.Min(maximum, range.Max);
        }

        return minimum <= maximum
            ? new OffsetRange(minimum, maximum)
            : new OffsetRange(0, 0);
    }

    private readonly record struct OffsetRange(double Min, double Max);

    private static IReadOnlyList<TaskbarEmbeddingDisplayConstraint> GetDisplayConstraints(
        TaskbarEmbeddingConstraints constraints)
    {
        return constraints.Displays.Count > 0
            ? constraints.Displays
            : [new TaskbarEmbeddingDisplayConstraint(constraints.MaxWidth, constraints.MaxHeight, 1)];
    }
}
