namespace TaskbarLyrics.App;

public sealed record SmtcTimelineDiagnostics(
    DateTimeOffset CapturedAtUtc,
    string SourceAppUserModelId,
    string NormalizedSource,
    string ResolvedSource,
    bool IsPlaying,
    TimeSpan RawPosition,
    DateTimeOffset LastUpdatedTimeUtc,
    TimeSpan LastUpdateAge,
    TimeSpan ExtrapolatedPosition,
    TimeSpan SelectedPosition,
    string StrategyName,
    string Title,
    string Artist,
    bool IsFallbackSnapshot);
