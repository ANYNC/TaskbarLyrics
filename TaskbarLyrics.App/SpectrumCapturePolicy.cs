namespace TaskbarLyrics.App;

internal static class SpectrumCapturePolicy
{
    public static bool ShouldCapture(
        bool audioAccessGranted,
        bool previewEnabled,
        bool lyricsWindowVisible,
        bool spectrumContentVisible)
    {
        return audioAccessGranted &&
            (previewEnabled || (lyricsWindowVisible && spectrumContentVisible));
    }
}
