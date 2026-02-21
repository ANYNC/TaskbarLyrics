namespace TaskbarLyrics.App;

public sealed class SpotifyExtrapolatedTimelinePositionStrategy : ITimelinePositionStrategy
{
    private static readonly TimeSpan MaxExtrapolationAge = TimeSpan.FromSeconds(8);

    public string Name => "SpotifyNeteaseExtrapolated";

    public bool CanApply(SmtcTimelineDiagnostics diagnostics)
    {
        return ContainsSpotify(diagnostics.SourceAppUserModelId) ||
               ContainsSpotify(diagnostics.NormalizedSource) ||
               ContainsSpotify(diagnostics.ResolvedSource) ||
               ContainsNetease(diagnostics.SourceAppUserModelId) ||
               ContainsNetease(diagnostics.NormalizedSource) ||
               ContainsNetease(diagnostics.ResolvedSource);
    }

    public TimeSpan SelectPosition(SmtcTimelineDiagnostics diagnostics)
    {
        if (!diagnostics.IsPlaying)
        {
            return diagnostics.RawPosition;
        }

        if (diagnostics.LastUpdateAge < TimeSpan.Zero ||
            diagnostics.LastUpdateAge > MaxExtrapolationAge)
        {
            return diagnostics.RawPosition;
        }

        return diagnostics.ExtrapolatedPosition;
    }

    private static bool ContainsSpotify(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains("spotify", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsNetease(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("netease", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("163music", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("music.163", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "Netease", StringComparison.OrdinalIgnoreCase);
    }
}
