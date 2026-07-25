using System.Text.Json;

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
        bool isPlaying)
    {
        return $"window.taskbarLyrics?.setLyrics({JsonSerializer.Serialize(current)}, {JsonSerializer.Serialize(next)}, " +
               $"{JsonSerializer.Serialize(Math.Clamp(lineProgress, 0, 1))}, {JsonSerializer.Serialize(currentLineIndex)}, " +
               $"{JsonSerializer.Serialize(trackId ?? string.Empty)}, {JsonSerializer.Serialize(isPureMusic)}, {JsonSerializer.Serialize(isPlaying)});";
    }

    public static string SetCover(string? dataUri, string fallbackText, string fallbackColor, string? trackId)
    {
        return $"window.taskbarLyrics?.setCover({JsonSerializer.Serialize(dataUri ?? string.Empty)}, " +
               $"{JsonSerializer.Serialize(fallbackText)}, {JsonSerializer.Serialize(fallbackColor)}, " +
               $"{JsonSerializer.Serialize(trackId ?? string.Empty)});";
    }

    public static string SetSpectrum(IReadOnlyList<float> bars)
    {
        var values = bars.Select(value => Math.Clamp(value, 0f, 1f));
        return $"window.taskbarLyrics?.setSpectrum({JsonSerializer.Serialize(values)});";
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
        return $"window.taskbarLyrics?.setSpectrumTuning({JsonSerializer.Serialize(payload)});";
    }
}
