using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Abstractions;

public interface ILyricResolutionTraceSink
{
    void RequestPrepared(LyricResolutionRequestTrace request);

    void CandidateEvaluated(LyricResolutionCandidateTrace candidate);

    void SourceCompleted(LyricResolutionSourceTrace source);

    void SelectionCompleted(LyricResolutionSelectionTrace selection);
}
