using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;

namespace TaskbarLyrics.App;

internal readonly record struct LyricsSearchingPresentation(string Current, string Next);

internal static class LyricsSearchingPresentationPolicy
{
    public static LyricsSearchingPresentation Create(TrackInfo? track)
    {
        var title = track?.Title?.Trim() ?? string.Empty;
        var artist = track?.Artist?.Trim() ?? string.Empty;
        var current = title.Length == 0
            ? artist
            : artist.Length == 0
                ? title
                : $"{title} - {artist}";

        if (current.Length == 0)
        {
            current = LyricSyncService.SearchingText;
        }

        return new LyricsSearchingPresentation(current, LyricSyncService.SearchingText);
    }
}
