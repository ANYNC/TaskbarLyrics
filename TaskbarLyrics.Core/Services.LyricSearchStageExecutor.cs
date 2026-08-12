using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Services;

internal static class LyricSearchStageExecutor
{
    public static async Task<IReadOnlyList<SourceTrackCandidate>> ExecuteAsync(
        LyricSearchPlan plan,
        Func<SearchQueryVariant, CancellationToken, Task<IReadOnlyList<SourceTrackCandidate>>> retrieveCandidatesAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(retrieveCandidatesAsync);

        var allCandidates = new Dictionary<string, SourceTrackCandidate>(StringComparer.Ordinal);
        foreach (var variant in plan.Variants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = await retrieveCandidatesAsync(variant, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(candidates);

            var stageCandidates = DeduplicateCandidates(candidates);
            foreach (var candidate in stageCandidates)
            {
                allCandidates.TryAdd(candidate.CandidateId, candidate);
            }

            if (HasAdmittedCandidate(plan.OriginalTrack, stageCandidates, cancellationToken))
            {
                return stageCandidates;
            }
        }

        return allCandidates.Values.ToArray();
    }

    private static SourceTrackCandidate[] DeduplicateCandidates(
        IReadOnlyList<SourceTrackCandidate> candidates)
    {
        var uniqueCandidates = new Dictionary<string, SourceTrackCandidate>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            uniqueCandidates.TryAdd(candidate.CandidateId, candidate);
        }

        return uniqueCandidates.Values.ToArray();
    }

    private static bool HasAdmittedCandidate(
        TrackIdentity originalTrack,
        IReadOnlyList<SourceTrackCandidate> candidates,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (LyricIdentityEvaluator.Evaluate(originalTrack, candidate).IsAdmitted)
            {
                return true;
            }
        }

        return false;
    }
}
