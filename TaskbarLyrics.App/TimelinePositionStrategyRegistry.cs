namespace TaskbarLyrics.App;

public sealed class TimelinePositionStrategyRegistry
{
    private readonly IReadOnlyList<ITimelinePositionStrategy> _strategies;
    private readonly ITimelinePositionStrategy _defaultStrategy;
    private string _lastTrackIdentity = string.Empty;
    private DateTimeOffset _lastTimelineUpdatedAtUtc;
    private TimeSpan _lastSelectedPosition;
    private bool _hasTimelineState;
    private bool _wasPlaying;
    private TimelineRefreshWaitState _timelineRefreshWaitState;

    public TimelinePositionStrategyRegistry(
        IReadOnlyList<ITimelinePositionStrategy> strategies,
        ITimelinePositionStrategy defaultStrategy)
    {
        _strategies = strategies;
        _defaultStrategy = defaultStrategy;
    }

    public (string StrategyName, TimeSpan Position) Select(SmtcTimelineDiagnostics diagnostics)
    {
        var selection = SelectCore(diagnostics);
        var position = PreservePositionWhilePlaybackTransitionIsStale(diagnostics, selection.Position);
        return (selection.StrategyName, position);
    }

    private (string StrategyName, TimeSpan Position) SelectCore(SmtcTimelineDiagnostics diagnostics)
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

    private TimeSpan PreservePositionWhilePlaybackTransitionIsStale(
        SmtcTimelineDiagnostics diagnostics,
        TimeSpan selectedPosition)
    {
        var trackIdentity = string.Join(
            "|",
            diagnostics.ResolvedSource,
            diagnostics.Title,
            diagnostics.Artist);
        if (!_hasTimelineState ||
            !string.Equals(trackIdentity, _lastTrackIdentity, StringComparison.Ordinal))
        {
            _hasTimelineState = true;
            _lastTrackIdentity = trackIdentity;
            _lastTimelineUpdatedAtUtc = diagnostics.LastUpdatedTimeUtc;
            _lastSelectedPosition = selectedPosition;
            _wasPlaying = diagnostics.IsPlaying;
            _timelineRefreshWaitState = TimelineRefreshWaitState.None;
            return selectedPosition;
        }

        if (diagnostics.IsPlaying)
        {
            if (!_wasPlaying)
            {
                _timelineRefreshWaitState =
                    diagnostics.LastUpdatedTimeUtc <= _lastTimelineUpdatedAtUtc
                        ? TimelineRefreshWaitState.WaitingForResumedTimeline
                        : TimelineRefreshWaitState.None;
                _wasPlaying = true;
            }

            if (_timelineRefreshWaitState == TimelineRefreshWaitState.WaitingForResumedTimeline &&
                diagnostics.LastUpdatedTimeUtc <= _lastTimelineUpdatedAtUtc)
            {
                return _lastSelectedPosition;
            }

            _lastTimelineUpdatedAtUtc = diagnostics.LastUpdatedTimeUtc;
            _lastSelectedPosition = selectedPosition;
            _timelineRefreshWaitState = TimelineRefreshWaitState.None;
            return selectedPosition;
        }

        if (_wasPlaying)
        {
            _timelineRefreshWaitState =
                diagnostics.LastUpdatedTimeUtc <= _lastTimelineUpdatedAtUtc
                    ? TimelineRefreshWaitState.WaitingForPausedTimeline
                    : TimelineRefreshWaitState.None;
            _wasPlaying = false;
        }

        if (_timelineRefreshWaitState == TimelineRefreshWaitState.WaitingForPausedTimeline &&
            diagnostics.LastUpdatedTimeUtc <= _lastTimelineUpdatedAtUtc)
        {
            _lastSelectedPosition = selectedPosition > _lastSelectedPosition
                ? selectedPosition
                : _lastSelectedPosition;
            return _lastSelectedPosition;
        }

        _timelineRefreshWaitState = TimelineRefreshWaitState.None;
        _lastTimelineUpdatedAtUtc = diagnostics.LastUpdatedTimeUtc;
        _lastSelectedPosition = selectedPosition;
        return selectedPosition;
    }

    public static TimelinePositionStrategyRegistry CreateDefault()
    {
        var fallback = new DefaultExtrapolatedTimelinePositionStrategy();
        return new TimelinePositionStrategyRegistry(
            new ITimelinePositionStrategy[]
            {
                new CommonExtrapolatedTimelinePositionStrategy()
            },
            fallback);
    }

    private enum TimelineRefreshWaitState
    {
        None,
        WaitingForPausedTimeline,
        WaitingForResumedTimeline
    }
}
