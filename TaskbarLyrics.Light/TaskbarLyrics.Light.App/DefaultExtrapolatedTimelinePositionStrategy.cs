namespace TaskbarLyrics.Light.App;

public sealed class DefaultExtrapolatedTimelinePositionStrategy : ITimelinePositionStrategy
{
    public string Name => "DefaultExtrapolated";

    public bool CanApply(SmtcTimelineDiagnostics diagnostics)
    {
        return true;
    }

    public TimeSpan SelectPosition(SmtcTimelineDiagnostics diagnostics)
    {
        if (!diagnostics.IsPlaying)
        {
            return diagnostics.RawPosition;
        }

        if (diagnostics.ExtrapolatedPosition < TimeSpan.Zero)
        {
            return diagnostics.RawPosition;
        }

        return diagnostics.ExtrapolatedPosition;
    }
}
