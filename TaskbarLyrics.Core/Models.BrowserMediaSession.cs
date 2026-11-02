namespace TaskbarLyrics.Core.Models;

public sealed record BrowserMediaSession(
    string Key, string ClientId, string Browser, string DocumentId, string MediaId, string Url,
    string Title, string Artist, string Album, bool IsPlaying, double PositionSeconds,
    double DurationSeconds, bool CanSeek, long Sequence)
{
    public BrowserMediaDetails? Details { get; init; }
}

public sealed record BrowserMediaDetails(int Version, long TrackVersion, string Source, string TitleSource,
    string PlaybackState, double? PositionSeconds, double? DurationSeconds, double PlaybackRate,
    double SampleAgeSeconds, IReadOnlyList<BrowserSeekRange> Seekable, IReadOnlyList<string> Actions,
    IReadOnlyList<BrowserArtwork> Artwork)
{
    public double? AnchorAgeSeconds { get; init; }
}

public sealed record BrowserSeekRange(double Start, double End);
public sealed record BrowserArtwork(string Src, string Sizes, string Type);

public static class BrowserSessionSelection
{
    public static BrowserMediaSession? Select(IEnumerable<BrowserMediaSession> sessions,
        IReadOnlyList<BrowserSiteRule> rules, string? currentKey)
    {
        return sessions.Select(session => (Session: session,
                Priority: Enumerable.Range(0, rules.Count).FirstOrDefault(
                    index => rules[index].Enabled && rules[index].Matches(session.Url), -1)))
            .Where(candidate => candidate.Priority >= 0 && !string.IsNullOrWhiteSpace(candidate.Session.Title))
            .OrderByDescending(candidate => candidate.Session.IsPlaying)
            .ThenBy(candidate => candidate.Priority)
            .ThenByDescending(candidate => candidate.Session.Details?.TitleSource != "document")
            .ThenByDescending(candidate => candidate.Session.Key == currentKey)
            .ThenBy(candidate => candidate.Session.Key, StringComparer.Ordinal)
            .Select(candidate => candidate.Session).FirstOrDefault();
    }

    public static bool PreferBrowser(bool browserPlaying, bool? otherPlaying, int browserPriority, int otherPriority) =>
        otherPlaying is null || browserPlaying && !otherPlaying.Value ||
        browserPlaying == otherPlaying.Value && browserPriority < otherPriority;
}
