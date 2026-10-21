namespace TaskbarLyrics.App;

public sealed class AppleMusicTimelinePositionStrategy(TimeProvider? timeProvider = null) : ITimelinePositionStrategy
{
    private static readonly TimeSpan PositionResolution = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SeekThreshold = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SeekConfirmationTimeout = TimeSpan.FromSeconds(2);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private string? _trackIdentity;
    private long _sampleTimestamp;
    private TimeSpan _position;
    private TimeSpan _rawPosition;
    private DateTimeOffset _timelineUpdatedAtUtc;
    private bool _playing;
    private double _rate = 1;
    private int _driftDirection;
    private int _driftSamples;
    private TimeSpan? _seekTarget;
    private long _seekRequestedAt;

    public string Name => "AppleMusicContinuous";

    public bool CanApply(SmtcTimelineDiagnostics diagnostics) =>
        IsAppleMusic(diagnostics.SourceAppUserModelId) || IsAppleMusic(diagnostics.ResolvedSource) ||
        IsAppleMusic(diagnostics.NormalizedSource);

    public TimeSpan SelectPosition(SmtcTimelineDiagnostics diagnostics)
    {
        var now = _timeProvider.GetTimestamp();
        var identity = string.Join("|", diagnostics.SourceAppUserModelId, diagnostics.Title, diagnostics.Artist);
        if (_trackIdentity != identity)
        {
            Reset();
            _trackIdentity = identity;
            return Reanchor(diagnostics, now);
        }

        var elapsed = _timeProvider.GetElapsedTime(_sampleTimestamp, now);
        var projected = _position + (_playing && diagnostics.IsPlaying ? elapsed * _rate : TimeSpan.Zero);
        var refreshed = diagnostics.LastUpdatedTimeUtc > _timelineUpdatedAtUtc;
        if (_seekTarget is { } seekTarget)
        {
            if (refreshed && (diagnostics.RawPosition - projected).Duration() <= PositionResolution)
            {
                _seekTarget = null;
                _rawPosition = diagnostics.RawPosition;
                _timelineUpdatedAtUtc = diagnostics.LastUpdatedTimeUtc;
                return Commit(projected, now, diagnostics.IsPlaying);
            }
            if (_timeProvider.GetElapsedTime(_seekRequestedAt, now) >= SeekConfirmationTimeout)
            {
                _seekTarget = null;
                return Reanchor(diagnostics, now);
            }
            // Do not let an acknowledged seek get overwritten by the player's old timeline.
            return Commit(_playing ? projected : seekTarget, now, _playing && diagnostics.IsPlaying);
        }

        if (!diagnostics.IsPlaying)
        {
            ResetCorrection();
            if (refreshed)
            {
                if (diagnostics.RawPosition < _rawPosition ||
                    (diagnostics.RawPosition - _position).Duration() > PositionResolution)
                    _position = diagnostics.RawPosition;
                _rawPosition = diagnostics.RawPosition;
                _timelineUpdatedAtUtc = diagnostics.LastUpdatedTimeUtc;
            }
            return Commit(_position, now, playing: false);
        }
        if (!_playing)
        {
            if (!refreshed) return Commit(_position, now, playing: false);
            if (diagnostics.RawPosition < _rawPosition ||
                (diagnostics.RawPosition - _position).Duration() > PositionResolution)
                return Reanchor(diagnostics, now);
            _rawPosition = diagnostics.RawPosition;
            _timelineUpdatedAtUtc = diagnostics.LastUpdatedTimeUtc;
            return Commit(_position, now, playing: true);
        }

        if (refreshed)
        {
            // Apple Music refreshes LastUpdatedTime several times for the same whole-second position.
            // A timestamp-only update is not a new precise playback anchor.
            if (diagnostics.RawPosition != _rawPosition)
            {
                var error = diagnostics.RawPosition - projected;
                if (diagnostics.RawPosition < _rawPosition || error > SeekThreshold)
                    return Reanchor(diagnostics, now);
                UpdateCorrection(error);
                _rawPosition = diagnostics.RawPosition;
            }
            _timelineUpdatedAtUtc = diagnostics.LastUpdatedTimeUtc;
        }
        return Commit(projected, now, playing: true);
    }

    public bool TryApplySeek(string sourceAppUserModelId, TimeSpan position, DateTimeOffset timelineUpdatedAtUtc)
    {
        if (_trackIdentity is null || !IsAppleMusic(sourceAppUserModelId)) return false;
        _seekTarget = position;
        _seekRequestedAt = _timeProvider.GetTimestamp();
        _timelineUpdatedAtUtc = timelineUpdatedAtUtc;
        ResetCorrection();
        Commit(position, _seekRequestedAt, _playing);
        return true;
    }

    public void Reset()
    {
        _trackIdentity = null;
        _seekTarget = null;
        _playing = false;
        ResetCorrection();
    }

    private TimeSpan Reanchor(SmtcTimelineDiagnostics diagnostics, long now)
    {
        _rawPosition = diagnostics.RawPosition;
        _timelineUpdatedAtUtc = diagnostics.LastUpdatedTimeUtc;
        ResetCorrection();
        // The initial coarse position has at most one second of usable subsecond extrapolation.
        var age = diagnostics.IsPlaying && diagnostics.LastUpdateAge >= TimeSpan.Zero &&
            diagnostics.LastUpdateAge <= PositionResolution ? diagnostics.LastUpdateAge : TimeSpan.Zero;
        return Commit(diagnostics.RawPosition + age, now, diagnostics.IsPlaying);
    }

    private TimeSpan Commit(TimeSpan position, long now, bool playing)
    {
        _position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        _sampleTimestamp = now;
        _playing = playing;
        return _position;
    }

    private void UpdateCorrection(TimeSpan error)
    {
        // Treat a whole second of uncertainty as quantization, not clock drift. Require
        // three distinct raw positions before slewing, and never reverse the running clock.
        var direction = error > PositionResolution ? 1 : error < -PositionResolution ? -1 : 0;
        if (direction == 0) { ResetCorrection(); return; }
        if (direction != _driftDirection) _rate = 1;
        _driftSamples = direction == _driftDirection ? _driftSamples + 1 : 1;
        _driftDirection = direction;
        if (_driftSamples >= 3)
            _rate = Math.Clamp(1 + (error.TotalSeconds - direction) / 5, 0.95, 1.05);
    }

    private void ResetCorrection()
    {
        _rate = 1;
        _driftDirection = _driftSamples = 0;
    }

    private static bool IsAppleMusic(string? source) => PlayerSourceIdentity.NormalizeAdditional(source) == "AppleMusic";
}
