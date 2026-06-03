namespace TaskbarLyrics.Core.Models;

public sealed record LyricDisplayFrame(
    string CurrentLine,
    string NextLine,
    string Title,
    double LineProgress = 0.0,
    int CurrentLineIndex = -1);
