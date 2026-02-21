namespace TaskbarLyrics.App;

public sealed class TimelinePositionStrategyRegistry
{
    private readonly IReadOnlyList<ITimelinePositionStrategy> _strategies;
    private readonly ITimelinePositionStrategy _defaultStrategy;

    public TimelinePositionStrategyRegistry(
        IReadOnlyList<ITimelinePositionStrategy> strategies,
        ITimelinePositionStrategy defaultStrategy)
    {
        _strategies = strategies;
        _defaultStrategy = defaultStrategy;
    }

    public (string StrategyName, TimeSpan Position) Select(SmtcTimelineDiagnostics diagnostics)
    {
        foreach (var strategy in _strategies)
        {
            if (!strategy.CanApply(diagnostics))
            {
                continue;
            }

            return (strategy.Name, strategy.SelectPosition(diagnostics));
        }

        return (_defaultStrategy.Name, _defaultStrategy.SelectPosition(diagnostics));
    }

    public static TimelinePositionStrategyRegistry CreateDefault()
    {
        var fallback = new DefaultRawTimelinePositionStrategy();
        return new TimelinePositionStrategyRegistry(
            new ITimelinePositionStrategy[]
            {
                new SpotifyExtrapolatedTimelinePositionStrategy()
            },
            fallback);
    }
}
