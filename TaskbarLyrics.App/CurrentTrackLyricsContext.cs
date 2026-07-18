using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.App;

internal sealed record CurrentTrackLyricsContext(TrackInfo Track, string LyricSource);
