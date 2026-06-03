namespace TaskbarLyrics.Core.Models;

public sealed record LyricSyllable(TimeSpan RelativeOffset, TimeSpan Duration, string Text);
