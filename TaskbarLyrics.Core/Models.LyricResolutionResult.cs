namespace TaskbarLyrics.Core.Models;

public enum LyricResolutionStatus
{
    Succeeded,
    NotFound,
    TransientFailure,
    Failed,
    Unavailable
}

public sealed record LyricResolutionResult(ResolvedLyrics? Lyrics, LyricResolutionStatus Status)
{
    public static LyricResolutionResult FromLyrics(ResolvedLyrics? lyrics) =>
        new(lyrics, lyrics is null ? LyricResolutionStatus.NotFound : LyricResolutionStatus.Succeeded);
}
