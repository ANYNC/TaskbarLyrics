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

    public static string SetCover(string? dataUri, string fallbackText, string fallbackColor, string? trackId)
    {
        return WebViewMessageScriptFactory.Dispatch("taskbarLyrics", "cover", new
        {
            dataUri = dataUri ?? string.Empty,
            fallbackText,
            fallbackColor,
            trackId = trackId ?? string.Empty
        });
    }

    public static string SetSpectrum(IReadOnlyList<float> bars)
    {
        var values = bars.Select(value => Math.Clamp(value, 0f, 1f));
        return WebViewMessageScriptFactory.Dispatch("taskbarLyrics", "spectrum", values);
    }

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
