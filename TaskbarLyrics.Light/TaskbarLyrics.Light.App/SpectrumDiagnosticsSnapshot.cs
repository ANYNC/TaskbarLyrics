namespace TaskbarLyrics.Light.App;

public sealed record SpectrumCaptureDiagnostics(
    bool IsAvailable,
    int SampleRate,
    int Channels,
    string Format,
    float InputPeak,
    DateTimeOffset? LastAudioUtc,
    string LastError);

public sealed record SpectrumDiagnosticsSnapshot(
    DateTimeOffset CapturedAtUtc,
    bool IsPureMusicMode,
    bool IsPlaying,
    bool IsCaptureAvailable,
    int SampleRate,
    int Channels,
    string Format,
    float InputPeak,
    float OutputPeak,
    DateTimeOffset? LastAudioUtc,
    string LastError)
{
    public static SpectrumDiagnosticsSnapshot Empty { get; } = new(
        DateTimeOffset.UtcNow,
        false,
        false,
        false,
        0,
        0,
        "Waiting",
        0,
        0,
        null,
        string.Empty);
}

public static class SpectrumDiagnosticsState
{
    private static readonly object Sync = new();
    private static SpectrumDiagnosticsSnapshot _current = SpectrumDiagnosticsSnapshot.Empty;

    public static SpectrumDiagnosticsSnapshot Current
    {
        get
        {
            lock (Sync)
            {
                return _current;
            }
        }
    }

    public static void Update(SpectrumDiagnosticsSnapshot snapshot)
    {
        lock (Sync)
        {
            _current = snapshot;
        }
    }
}
