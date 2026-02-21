namespace TaskbarLyrics.App;

public sealed class DefaultRawTimelinePositionStrategy : ITimelinePositionStrategy
{
    public string Name => "DefaultRaw";

    public bool CanApply(SmtcTimelineDiagnostics diagnostics)
    {
        return true;
    }

    public TimeSpan SelectPosition(SmtcTimelineDiagnostics diagnostics)
    {
        return diagnostics.RawPosition;
    }
}
