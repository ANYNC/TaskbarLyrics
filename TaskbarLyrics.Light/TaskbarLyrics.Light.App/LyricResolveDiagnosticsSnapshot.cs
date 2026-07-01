namespace TaskbarLyrics.Light.App;

public sealed record LyricResolveDiagnosticsSnapshot(
    DateTimeOffset CapturedAtUtc,
    string TrackTitle,
    string TrackArtist,
    string TrackSourceApp,
    string SelectedSource,
    int BestScore,
    int LineCount,
    bool IsPureMusic,
    string Candidates,
    int AppliedOffsetMs,
    TimeSpan PlaybackPosition,
    int CurrentLineIndex,
    double LineProgress)
{
    public static LyricResolveDiagnosticsSnapshot Empty { get; } = new(
        DateTimeOffset.MinValue,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        0,
        0,
        false,
        string.Empty,
        0,
        TimeSpan.Zero,
        -1,
        0);
}

public static class LyricResolveDiagnosticsState
{
    private static readonly object Gate = new();
    private static LyricResolveDiagnosticsSnapshot _current = LyricResolveDiagnosticsSnapshot.Empty;

    public static LyricResolveDiagnosticsSnapshot Current
    {
        get
        {
            lock (Gate)
            {
                return _current;
            }
        }
    }

    public static void Update(LyricResolveDiagnosticsSnapshot snapshot)
    {
        lock (Gate)
        {
            _current = snapshot;
        }
    }
}
