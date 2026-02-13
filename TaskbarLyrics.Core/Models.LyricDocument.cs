namespace TaskbarLyrics.Core.Models;

public sealed class LyricDocument
{
    public LyricDocument(IEnumerable<LyricLine> lines)
    {
        Lines = lines.OrderBy(x => x.Timestamp).ToArray();
    }

    public IReadOnlyList<LyricLine> Lines { get; }
}
