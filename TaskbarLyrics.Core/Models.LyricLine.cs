namespace TaskbarLyrics.Core.Models;

public sealed record LyricLine(TimeSpan Timestamp, string Text, string? Translation = null, List<LyricSyllable>? Syllables = null);
