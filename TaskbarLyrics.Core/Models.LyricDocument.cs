namespace TaskbarLyrics.Core.Models;

public sealed class LyricDocument
{
    public LyricDocument(IEnumerable<LyricLine> lines, int bestScore = 0)
    {
        Lines = lines.OrderBy(x => x.Timestamp).ToArray();
        BestScore = bestScore;
    }

    public IReadOnlyList<LyricLine> Lines { get; }
    public int BestScore { get; }
}
