using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Services;

public sealed class LyricSyncService
{
    private readonly ILyricProviderRegistry _lyricProviderRegistry;
    private string? _currentTrackId;
    private LyricDocument? _currentDocument;

    public LyricSyncService(ILyricProviderRegistry lyricProviderRegistry)
    {
        _lyricProviderRegistry = lyricProviderRegistry;
    }

    public async Task<string> GetCurrentLineAsync(PlaybackSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot.Track is null)
        {
            _currentTrackId = null;
            _currentDocument = null;
            return string.Empty;
        }

        if (!string.Equals(_currentTrackId, snapshot.Track.Id, StringComparison.Ordinal))
        {
            _currentTrackId = snapshot.Track.Id;
            _currentDocument = await _lyricProviderRegistry.GetLyricsAsync(snapshot.Track, cancellationToken);
        }

        if (_currentDocument is null || _currentDocument.Lines.Count == 0)
        {
            return string.Empty;
        }

        var current = _currentDocument.Lines
            .Where(line => line.Timestamp <= snapshot.Position)
            .LastOrDefault();

        return current?.Text ?? _currentDocument.Lines[0].Text;
    }
}
