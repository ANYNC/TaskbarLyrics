namespace TaskbarLyrics.App;

internal static class LyricsWebViewScriptFactory
{
    public static string SetLyrics(
        string current,
        string next,
        double lineProgress,
        int currentLineIndex,
        string? trackId,
        bool isPureMusic,
        bool isPlaying,
        double? wordScanProgress = null,
        string? currentTranslation = null,
        string? nextTranslation = null,
        bool translationMode = false,
        bool animateTransition = true,
        LyricsPresentationScene presentationScene = LyricsPresentationScene.Message)
    {
        return WebViewMessageScriptFactory.Dispatch("taskbarLyrics", "lyrics", new
        {
            current,
            next,
            progress = Math.Clamp(lineProgress, 0, 1),
            currentLineIndex,
            trackId = trackId ?? string.Empty,
            isPureMusic,
            isPlaying,
            currentTranslation = currentTranslation ?? string.Empty,
            nextTranslation = nextTranslation ?? string.Empty,
            translationMode,
            animateTransition,
            wordScanProgress = wordScanProgress.HasValue
                ? Math.Clamp(wordScanProgress.Value, 0, 1)
                : (double?)null,
            scene = presentationScene.ToWireValue()
        });
    }

    public static string SetCover(
        string? dataUri,
        string fallbackText,
        string fallbackColor,
        string? fallbackIconDataUri,
        string? trackId)
    {
        return WebViewMessageScriptFactory.Dispatch("taskbarLyrics", "cover", new
        {
            dataUri = dataUri ?? string.Empty,
            fallbackText,
            fallbackColor,
            fallbackIconDataUri = fallbackIconDataUri ?? string.Empty,
            trackId = trackId ?? string.Empty
        });
    }

    public static string SetSpectrum(IReadOnlyList<float> bars)
    {
        var values = bars.Select(value => Math.Clamp(value, 0f, 1f));
        return WebViewMessageScriptFactory.Dispatch("taskbarLyrics", "spectrum", values);
    }

    public static string SetControlPanelOpen(bool open) =>
        WebViewMessageScriptFactory.Dispatch("taskbarLyrics", "controlPanelState", new { open });

    public static string SetFloatingPanelTrack(
        string? title,
        string? artist,
        bool hasTrack,
        bool isPlaying,
        bool translationEnabled) =>
        WebViewMessageScriptFactory.Dispatch("taskbarLyrics", "panelTrack", new
        {
            title = title ?? string.Empty,
            artist = artist ?? string.Empty,
            hasTrack,
            isPlaying,
            translationEnabled
        });

    public static string SetFloatingPanelTimeline(
        double positionSeconds,
        double durationSeconds,
        bool isPlaying,
        bool canSeek) =>
        WebViewMessageScriptFactory.Dispatch("taskbarLyrics", "panelTimeline", new
        {
            position = double.IsFinite(positionSeconds) ? Math.Max(0, positionSeconds) : 0,
            duration = double.IsFinite(durationSeconds) ? Math.Max(0, durationSeconds) : 0,
            isPlaying,
            canSeek
        });

    public static string SetFloatingPanelLayout(bool open) =>
        WebViewMessageScriptFactory.Dispatch("taskbarLyrics", "floatingPanelLayout", new { open });

    /// <summary>
    /// Pointer presence for the floating surface. The host only keeps the transparent-pixel
    /// fill while the cursor is inside the window, so hovering is reported here as well: a
    /// cursor that does not move after the fill arrives would otherwise produce no enter event.
    /// </summary>
    public static string SetFloatingPointer(bool inside) =>
        WebViewMessageScriptFactory.Dispatch("taskbarLyrics", "floatingPointer", new { inside });

    /// <summary>
    /// Reveal progress driven by the host. The panel height and the native window size are
    /// then the same fraction of one animation; a CSS transition alone ran on its own clock
    /// and the panel visibly outran the window it lives in.
    /// </summary>
    public static string SetFloatingPanelProgress(double progress) =>
        WebViewMessageScriptFactory.Dispatch("taskbarLyrics", "floatingPanelProgress", new
        {
            progress = double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0
        });


    public static string SetSpectrumTuning(SpectrumTuningSettings settings)
    {
        var payload = new
        {
            rise = settings.FrontendRise,
            fall = settings.FrontendFall,
            minHeight = settings.MinBarHeight,
            heightRange = settings.BarHeightRange,
            opacity = settings.BarOpacity,
            barCount = settings.BarCount
        };
        return WebViewMessageScriptFactory.Dispatch("taskbarLyrics", "spectrumTuning", payload);
    }
}
