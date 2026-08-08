using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Core.Services;

public static class LyricIdentityEvaluator
{
    public static LyricCandidateEvaluation Evaluate(
        TrackIdentity originalTrack,
        SourceTrackCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(originalTrack);
        ArgumentNullException.ThrowIfNull(candidate);

        var track = new TrackInfo(
            originalTrack.TrackId,
            originalTrack.Title,
            string.Join(", ", originalTrack.Artists),
            originalTrack.Album,
            originalTrack.SourceApp,
            originalTrack.Duration,
            originalTrack.SongId);
        var score = LyricMatcher.Score(
            track,
            candidate.Title,
            string.Join(", ", candidate.Artists),
            (int)Math.Round(candidate.Duration.TotalSeconds),
            candidate.Album);
        return score >= LyricMatchingPolicy.MinimumAcceptedMatchScore
            ? LyricCandidateEvaluation.Accepted(score)
            : LyricCandidateEvaluation.Rejected(
                score,
                score == 0 ? "identity-conflict" : "below-admission-threshold");
    }
}

public static class ProviderSongIdPolicy
{
    public static bool CanUseDirectSongId(
        TrackIdentity track,
        LyricProviderId providerId)
    {
        if (string.IsNullOrWhiteSpace(track.SongId))
        {
            return false;
        }

        return LyricSourceRoutingPolicy.TryGetOfficialProvider(track.SourceApp, out var officialProvider) &&
               string.Equals(officialProvider, providerId.Value, StringComparison.OrdinalIgnoreCase);
    }
}
