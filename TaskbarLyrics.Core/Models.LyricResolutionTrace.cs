namespace TaskbarLyrics.Core.Models;

public sealed record LyricResolutionRequestTrace(
    string RequestId,
    TrackInfo OriginalTrack,
    TrackInfo EffectiveTrack,
    LyricSearchPlan SearchPlan,
    bool IsPureMusic,
    string? PreferredProvider);

public sealed record LyricResolutionCandidateTrace(
    string RequestId,
    SourceTrackCandidate Candidate,
    LyricCandidateEvaluation Evaluation);

public sealed record LyricResolutionSourceTrace(
    string RequestId,
    LyricProviderId ProviderId,
    LyricSourceTerminalState State,
    string? Detail);

public sealed record LyricResolutionSelectionTrace(
    string RequestId,
    ResolvedLyrics? Lyrics);
