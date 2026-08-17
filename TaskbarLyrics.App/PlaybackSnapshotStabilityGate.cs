using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;

namespace TaskbarLyrics.App;

internal enum PlaybackSnapshotGateAction
{
    Accept,
    Hold
}

internal enum PlaybackSnapshotGateReason
{
    InitialSnapshot,
    StableIdentity,
    NoPlayback,
    WeakMetadataChange,
    WeakMetadataChangeReplaced,
    StableIdentityRestored,
    QuietPeriodElapsed,
    MaximumHoldElapsed,
    StrongMetadataChange
}

internal enum PlaybackSnapshotStabilityState
{
    Empty,
    Stable,
    PendingWeakChange
}

internal readonly record struct PlaybackSnapshotGateDecision(
    PlaybackSnapshotGateAction Action,
    PlaybackSnapshot Snapshot,
    PlaybackSnapshotGateReason Reason);

/// <summary>
/// Prevents a short-lived, not-playing SMTC metadata regression from starting a
/// new lyric search while the current track identity is still stable.
/// </summary>
internal sealed class PlaybackSnapshotStabilityGate
{
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(750);
    public static readonly TimeSpan MaximumHoldDuration = TimeSpan.FromMilliseconds(1500);

    private readonly TimeSpan _quietPeriod;
    private readonly TimeSpan _maximumHoldDuration;
    private PlaybackSnapshotStabilityState _state;
    private TrackInfo? _stableTrack;
    private PlaybackSnapshot? _pendingSnapshot;
    private DateTimeOffset _quietDeadlineUtc;
    private DateTimeOffset _maximumHoldDeadlineUtc;

    public PlaybackSnapshotStabilityGate(
        TimeSpan? quietPeriod = null,
        TimeSpan? maximumHoldDuration = null)
    {
        _quietPeriod = quietPeriod ?? QuietPeriod;
        _maximumHoldDuration = maximumHoldDuration ?? MaximumHoldDuration;
        if (_quietPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(quietPeriod));
        }

        if (_maximumHoldDuration < _quietPeriod)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumHoldDuration),
                "The maximum hold duration must be at least the quiet period.");
        }
    }

    public PlaybackSnapshotStabilityState State => _state;

    public PlaybackSnapshotGateDecision Observe(
        PlaybackSnapshot snapshot,
        PlaybackInputKind inputKind,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (inputKind == PlaybackInputKind.NoPlayback || snapshot.Track is null)
        {
            return AcceptNoPlayback(snapshot);
        }

        if (_state == PlaybackSnapshotStabilityState.Empty)
        {
            return AcceptStable(snapshot, PlaybackSnapshotGateReason.InitialSnapshot);
        }

        if (_state == PlaybackSnapshotStabilityState.PendingWeakChange)
        {
            return ObservePendingWeakChange(snapshot, nowUtc);
        }

        if (ShouldHoldWeakChange(snapshot))
        {
            StartPendingWeakChange(snapshot, nowUtc);
            return new PlaybackSnapshotGateDecision(
                PlaybackSnapshotGateAction.Hold,
                snapshot,
                PlaybackSnapshotGateReason.WeakMetadataChange);
        }

        return AcceptStable(snapshot, GetAcceptReason(snapshot));
    }

    private PlaybackSnapshotGateDecision ObservePendingWeakChange(
        PlaybackSnapshot snapshot,
        DateTimeOffset nowUtc)
    {
        if (HasDifferentReliableSongIds(_stableTrack!, snapshot.Track!))
        {
            return AcceptStable(snapshot, PlaybackSnapshotGateReason.StrongMetadataChange);
        }

        if (HasSameStableIdentity(snapshot.Track!))
        {
            _pendingSnapshot = null;
            _state = PlaybackSnapshotStabilityState.Stable;
            return new PlaybackSnapshotGateDecision(
                PlaybackSnapshotGateAction.Accept,
                snapshot,
                PlaybackSnapshotGateReason.StableIdentityRestored);
        }

        if (!ShouldHoldWeakChange(snapshot))
        {
            return AcceptStable(snapshot, PlaybackSnapshotGateReason.StrongMetadataChange);
        }

        if (nowUtc >= _maximumHoldDeadlineUtc)
        {
            return AcceptStable(snapshot, PlaybackSnapshotGateReason.MaximumHoldElapsed);
        }

        if (IsSubstantivePendingChange(snapshot))
        {
            _pendingSnapshot = snapshot;
            _quietDeadlineUtc = nowUtc + _quietPeriod;
            return new PlaybackSnapshotGateDecision(
                PlaybackSnapshotGateAction.Hold,
                snapshot,
                PlaybackSnapshotGateReason.WeakMetadataChangeReplaced);
        }

        if (nowUtc >= _quietDeadlineUtc)
        {
            return AcceptStable(snapshot, PlaybackSnapshotGateReason.QuietPeriodElapsed);
        }

        return new PlaybackSnapshotGateDecision(
            PlaybackSnapshotGateAction.Hold,
            snapshot,
            PlaybackSnapshotGateReason.WeakMetadataChange);
    }

    private void StartPendingWeakChange(PlaybackSnapshot snapshot, DateTimeOffset nowUtc)
    {
        _state = PlaybackSnapshotStabilityState.PendingWeakChange;
        _pendingSnapshot = snapshot;
        _quietDeadlineUtc = nowUtc + _quietPeriod;
        _maximumHoldDeadlineUtc = nowUtc + _maximumHoldDuration;
    }

    private PlaybackSnapshotGateDecision AcceptNoPlayback(PlaybackSnapshot snapshot)
    {
        ClearAcceptedIdentity();
        return new PlaybackSnapshotGateDecision(
            PlaybackSnapshotGateAction.Accept,
            snapshot,
            PlaybackSnapshotGateReason.NoPlayback);
    }

    private PlaybackSnapshotGateDecision AcceptStable(
        PlaybackSnapshot snapshot,
        PlaybackSnapshotGateReason reason)
    {
        _stableTrack = snapshot.Track;
        _pendingSnapshot = null;
        _state = PlaybackSnapshotStabilityState.Stable;
        return new PlaybackSnapshotGateDecision(
            PlaybackSnapshotGateAction.Accept,
            snapshot,
            reason);
    }

    private void ClearAcceptedIdentity()
    {
        _stableTrack = null;
        _pendingSnapshot = null;
        _quietDeadlineUtc = default;
        _maximumHoldDeadlineUtc = default;
        _state = PlaybackSnapshotStabilityState.Empty;
    }

    private bool ShouldHoldWeakChange(PlaybackSnapshot snapshot)
    {
        if (_stableTrack is null || snapshot.Track is null || snapshot.IsPlaying)
        {
            return false;
        }

        var candidate = snapshot.Track;
        if (!HasSameSourceAndTitle(_stableTrack, candidate) ||
            HasDifferentReliableSongIds(_stableTrack, candidate) ||
            HasSameStableIdentity(candidate))
        {
            return false;
        }

        return IsArtistFormattingChange(_stableTrack.Artist, candidate.Artist) ||
               IsArtistInformationDegradation(_stableTrack.Artist, candidate.Artist);
    }

    private bool HasSameStableIdentity(TrackInfo candidate)
    {
        return _stableTrack is not null &&
               string.Equals(
                   LyricSyncService.BuildStableTrackIdentity(_stableTrack),
                   LyricSyncService.BuildStableTrackIdentity(candidate),
                   StringComparison.Ordinal);
    }

    private bool IsSubstantivePendingChange(PlaybackSnapshot snapshot)
    {
        if (_pendingSnapshot?.Track is not { } pendingTrack || snapshot.Track is not { } candidate)
        {
            return true;
        }

        return !string.Equals(
            LyricSyncService.BuildStableTrackIdentity(pendingTrack),
            LyricSyncService.BuildStableTrackIdentity(candidate),
            StringComparison.Ordinal);
    }

    private PlaybackSnapshotGateReason GetAcceptReason(PlaybackSnapshot snapshot)
    {
        if (snapshot.Track is null)
        {
            return PlaybackSnapshotGateReason.NoPlayback;
        }

        return HasSameStableIdentity(snapshot.Track) &&
               !HasDifferentReliableSongIds(_stableTrack!, snapshot.Track)
            ? PlaybackSnapshotGateReason.StableIdentity
            : PlaybackSnapshotGateReason.StrongMetadataChange;
    }

    private static bool HasSameSourceAndTitle(TrackInfo current, TrackInfo candidate)
    {
        return string.Equals(
                   NormalizeGeneral(current.SourceApp),
                   NormalizeGeneral(candidate.SourceApp),
                   StringComparison.Ordinal) &&
               string.Equals(
                   NormalizeGeneral(current.Title),
                   NormalizeGeneral(candidate.Title),
                   StringComparison.Ordinal);
    }

    private static bool HasDifferentReliableSongIds(TrackInfo current, TrackInfo candidate)
    {
        return !string.IsNullOrWhiteSpace(current.SongId) &&
               !string.IsNullOrWhiteSpace(candidate.SongId) &&
               !string.Equals(
                   NormalizeGeneral(current.SongId),
                   NormalizeGeneral(candidate.SongId),
                   StringComparison.Ordinal);
    }

    private static bool IsArtistFormattingChange(string? current, string? candidate)
    {
        return !string.IsNullOrWhiteSpace(current) &&
               !string.IsNullOrWhiteSpace(candidate) &&
               string.Equals(
                   NormalizeArtist(current),
                   NormalizeArtist(candidate),
                   StringComparison.Ordinal);
    }

    private static bool IsArtistInformationDegradation(string? current, string? candidate)
    {
        var currentNormalized = NormalizeArtist(current);
        if (currentNormalized.Length == 0)
        {
            return false;
        }

        var candidateNormalized = NormalizeArtist(candidate);
        if (candidateNormalized.Length == 0 ||
            string.Equals(candidateNormalized, "UNKNOWN ARTIST", StringComparison.Ordinal))
        {
            return !string.Equals(currentNormalized, "UNKNOWN ARTIST", StringComparison.Ordinal);
        }

        var currentArtists = SplitArtists(currentNormalized);
        var candidateArtists = SplitArtists(candidateNormalized);
        if (candidateArtists.Length == 0 || candidateArtists.Length >= currentArtists.Length)
        {
            return false;
        }

        return candidateArtists.All(candidateArtist =>
            currentArtists.Contains(candidateArtist, StringComparer.Ordinal));
    }

    private static string NormalizeGeneral(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            ' ',
            value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
    }

    private static string NormalizeArtist(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = new System.Text.StringBuilder(value.Length);
        var pendingWhitespace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingWhitespace = true;
                continue;
            }

            if (IsArtistSeparator(character))
            {
                while (normalized.Length > 0 && normalized[^1] == ' ')
                {
                    normalized.Length--;
                }

                normalized.Append(character);
                pendingWhitespace = false;
                continue;
            }

            if (pendingWhitespace && normalized.Length > 0 &&
                !IsArtistSeparator(normalized[^1]))
            {
                normalized.Append(' ');
            }

            normalized.Append(char.ToUpperInvariant(character));
            pendingWhitespace = false;
        }

        return normalized.ToString().Trim();
    }

    private static string[] SplitArtists(string normalizedArtist)
    {
        return normalizedArtist.Split(
            ArtistSeparators,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool IsArtistSeparator(char character)
    {
        return ArtistSeparators.Contains(character);
    }

    private static readonly char[] ArtistSeparators = [';', '/', ',', '、', '&'];
}
