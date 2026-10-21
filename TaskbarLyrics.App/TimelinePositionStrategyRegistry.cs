namespace TaskbarLyrics.App;

public sealed class TimelinePositionStrategyRegistry
{
    private static readonly TimeSpan MaximumDeferredPausedAdvance = TimeSpan.FromSeconds(1);

    private readonly IReadOnlyList<ITimelinePositionStrategy> _strategies;
    private readonly ITimelinePositionStrategy _defaultStrategy;
    private readonly object _sync = new();
    private ITimelinePositionStrategy? _activeStrategy;
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
        lock (_sync)
        {
            var selection = SelectCore(diagnostics);
            var position = PreservePositionWhilePlaybackTransitionIsStale(diagnostics, selection.Position);
            return (selection.StrategyName, position);
        }
    }

    private (string StrategyName, TimeSpan Position) SelectCore(SmtcTimelineDiagnostics diagnostics)
    {
        var strategy = _strategies.FirstOrDefault(strategy => strategy.CanApply(diagnostics)) ?? _defaultStrategy;
        if (_activeStrategy != strategy)
        {
            _activeStrategy?.Reset();
            strategy.Reset();
            _activeStrategy = strategy;
        }
        return (strategy.Name, strategy.SelectPosition(diagnostics));
    }

    internal void NotifySeek(string sourceAppUserModelId, TimeSpan position, DateTimeOffset timelineUpdatedAtUtc)
    {
        lock (_sync)
        {
            if (_activeStrategy?.TryApplySeek(sourceAppUserModelId, position, timelineUpdatedAtUtc) != true) return;
            _lastSelectedPosition = position;
            _lastTimelineUpdatedAtUtc = timelineUpdatedAtUtc;
            _timelineRefreshWaitState = TimelineRefreshWaitState.None;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _activeStrategy?.Reset();
            _activeStrategy = null;
            _hasTimelineState = false;
        }
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
            _wasPlaying = false;
            _timelineRefreshWaitState = TimelineRefreshWaitState.None;
        }

        if (diagnostics.LastUpdatedTimeUtc <= _lastTimelineUpdatedAtUtc)
        {
            return _lastSelectedPosition;
        }

        _lastTimelineUpdatedAtUtc = diagnostics.LastUpdatedTimeUtc;
        if (ShouldDeferPausedAdvance(selectedPosition))
        {
            return _lastSelectedPosition;
        }

        _timelineRefreshWaitState = TimelineRefreshWaitState.None;
        _lastSelectedPosition = selectedPosition;
        return selectedPosition;
    }

    private bool ShouldDeferPausedAdvance(TimeSpan selectedPosition)
    {
        var advance = selectedPosition - _lastSelectedPosition;
        return advance > TimeSpan.Zero && advance <= MaximumDeferredPausedAdvance;
    }

    public static TimelinePositionStrategyRegistry CreateDefault(TimeProvider? timeProvider = null)
    {
        var fallback = new DefaultExtrapolatedTimelinePositionStrategy();
        return new TimelinePositionStrategyRegistry(
            new ITimelinePositionStrategy[]
            {
                new AppleMusicTimelinePositionStrategy(timeProvider),
                new CommonExtrapolatedTimelinePositionStrategy()
            },
            fallback);
    }

    private enum TimelineRefreshWaitState
    {
        None,
        WaitingForResumedTimeline
    }
}
