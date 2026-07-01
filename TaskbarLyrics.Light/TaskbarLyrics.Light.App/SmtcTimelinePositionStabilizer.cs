namespace TaskbarLyrics.Light.App;

internal sealed class SmtcTimelinePositionStabilizer
{
    private static readonly TimeSpan TrackSwitchGuardDuration = TimeSpan.FromMilliseconds(850);
    private static readonly TimeSpan TrackSwitchResidualThreshold = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan RegressionTolerance = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan SmallSeekThreshold = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ForwardSpikeTolerance = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RawConfirmationTolerance = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan MaxContinuousTickGap = TimeSpan.FromSeconds(2);

    private string _trackKey = string.Empty;
    private DateTimeOffset _trackChangedAtUtc;
    private DateTimeOffset _lastAcceptedAtUtc;
    private TimeSpan _lastAcceptedPosition;
    private int _regressionSamples;
    private bool _hasAcceptedPosition;

    public TimeSpan Stabilize(
        SmtcTimelineDiagnostics diagnostics,
        TimeSpan selectedPosition,
        TimeSpan duration,
        out bool adjusted)
    {
        var originalPosition = selectedPosition;
        selectedPosition = ClampToTrackBounds(selectedPosition, duration);
        adjusted = selectedPosition != originalPosition;

        var trackKey = BuildTrackKey(diagnostics);
        if (trackKey.Length == 0 ||
            diagnostics.IsFallbackSnapshot ||
            IsWeakMetadata(diagnostics))
        {
            return selectedPosition;
        }

        var trackChanged = !_hasAcceptedPosition ||
            !string.Equals(trackKey, _trackKey, StringComparison.Ordinal);

        if (trackChanged)
        {
            var hadPreviousTrack = _hasAcceptedPosition && _trackKey.Length > 0;
            _trackKey = trackKey;
            _trackChangedAtUtc = diagnostics.CapturedAtUtc;
            _regressionSamples = 0;

            if (hadPreviousTrack && LooksLikeResidualTrackSwitchPosition(diagnostics, selectedPosition))
            {
                adjusted = true;
                Accept(trackKey, diagnostics.CapturedAtUtc, TimeSpan.Zero);
                return TimeSpan.Zero;
            }

            Accept(trackKey, diagnostics.CapturedAtUtc, selectedPosition);
            return selectedPosition;
        }

        if (!diagnostics.IsPlaying)
        {
            Accept(trackKey, diagnostics.CapturedAtUtc, selectedPosition);
            return selectedPosition;
        }

        var elapsed = diagnostics.CapturedAtUtc - _lastAcceptedAtUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed > MaxContinuousTickGap)
        {
            Accept(trackKey, diagnostics.CapturedAtUtc, selectedPosition);
            return selectedPosition;
        }

        var expectedPosition = ClampToTrackBounds(_lastAcceptedPosition + elapsed, duration);

        if (IsInsideTrackSwitchGuard(diagnostics.CapturedAtUtc) &&
            _lastAcceptedPosition <= TimeSpan.FromSeconds(1) &&
            LooksLikeResidualTrackSwitchPosition(diagnostics, selectedPosition))
        {
            adjusted = true;
            Accept(trackKey, diagnostics.CapturedAtUtc, expectedPosition);
            return expectedPosition;
        }

        var regression = expectedPosition - selectedPosition;
        if (regression > RegressionTolerance && regression < SmallSeekThreshold)
        {
            _regressionSamples++;
            if (_regressionSamples <= 2)
            {
                adjusted = true;
                Accept(trackKey, diagnostics.CapturedAtUtc, expectedPosition);
                return expectedPosition;
            }
        }
        else
        {
            _regressionSamples = 0;
        }

        var forwardLead = selectedPosition - expectedPosition;
        var rawLead = diagnostics.RawPosition - expectedPosition;
        if (forwardLead > ForwardSpikeTolerance && rawLead < RawConfirmationTolerance)
        {
            adjusted = true;
            Accept(trackKey, diagnostics.CapturedAtUtc, expectedPosition);
            return expectedPosition;
        }

        Accept(trackKey, diagnostics.CapturedAtUtc, selectedPosition);
        return selectedPosition;
    }

    private bool IsInsideTrackSwitchGuard(DateTimeOffset capturedAtUtc)
    {
        var sinceTrackChanged = capturedAtUtc - _trackChangedAtUtc;
        return sinceTrackChanged >= TimeSpan.Zero && sinceTrackChanged <= TrackSwitchGuardDuration;
    }

    private static bool LooksLikeResidualTrackSwitchPosition(
        SmtcTimelineDiagnostics diagnostics,
        TimeSpan selectedPosition)
    {
        return selectedPosition > TrackSwitchResidualThreshold &&
            diagnostics.RawPosition > TrackSwitchResidualThreshold;
    }

    private void Accept(string trackKey, DateTimeOffset capturedAtUtc, TimeSpan position)
    {
        _trackKey = trackKey;
        _lastAcceptedAtUtc = capturedAtUtc;
        _lastAcceptedPosition = position;
        _hasAcceptedPosition = true;
    }

    private static TimeSpan ClampToTrackBounds(TimeSpan position, TimeSpan duration)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (duration > TimeSpan.Zero && position > duration)
        {
            return duration;
        }

        return position;
    }

    private static string BuildTrackKey(SmtcTimelineDiagnostics diagnostics)
    {
        var source = Normalize(diagnostics.ResolvedSource);
        var title = Normalize(diagnostics.Title);
        var artist = Normalize(diagnostics.Artist);
        if (source.Length == 0 && title.Length == 0 && artist.Length == 0)
        {
            return string.Empty;
        }

        return $"{source}|{title}|{artist}";
    }

    private static bool IsWeakMetadata(SmtcTimelineDiagnostics diagnostics)
    {
        return IsWeakTitle(diagnostics.Title);
    }

    private static bool IsWeakTitle(string? value)
    {
        var title = value?.Trim();
        return string.IsNullOrEmpty(title) ||
            title.Equals("Unknown Title", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
    }
}
