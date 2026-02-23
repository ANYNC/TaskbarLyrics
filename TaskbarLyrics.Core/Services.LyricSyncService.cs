using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Services;

public sealed class LyricSyncService
{
    private static readonly TimeSpan QqMusicLineSwitchLead = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan NeteaseLineSwitchLead = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan SpotifyLineSwitchLead = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan DefaultLineSwitchLead = TimeSpan.FromMilliseconds(300);
    private readonly ILyricProviderRegistry _lyricProviderRegistry;
    private string? _currentTrackId;
    private LyricDocument? _currentDocument;
    private string? _currentLyricSourceApp;
    private string? _loadingTrackId;
    private Task<LyricResolveResult>? _loadingTask;

    public LyricSyncService(ILyricProviderRegistry lyricProviderRegistry)
    {
        _lyricProviderRegistry = lyricProviderRegistry;
    }

    public string? CurrentLyricSourceApp => _currentLyricSourceApp;

    public async Task<string> GetCurrentLineAsync(PlaybackSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var frame = await GetDisplayFrameAsync(snapshot, cancellationToken);
        return frame.CurrentLine;
    }

    public async Task<LyricDisplayFrame> GetDisplayFrameAsync(
        PlaybackSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (snapshot.Track is null)
        {
            _currentTrackId = null;
            _currentDocument = null;
            _currentLyricSourceApp = null;
            _loadingTrackId = null;
            _loadingTask = null;
            return new LyricDisplayFrame(string.Empty, string.Empty);
        }

        if (!string.Equals(_currentTrackId, snapshot.Track.Id, StringComparison.Ordinal))
        {
            // Track switched: clear stale lyrics immediately and start loading for new track.
            _currentTrackId = snapshot.Track.Id;
            _currentDocument = null;
            _currentLyricSourceApp = null;
            _loadingTrackId = snapshot.Track.Id;
            _loadingTask = _lyricProviderRegistry.ResolveLyricsAsync(snapshot.Track, cancellationToken);
        }

        await TryApplyLoadedLyricsAsync(snapshot.Track.SourceApp);

        if (_currentDocument is null || _currentDocument.Lines.Count == 0)
        {
            return new LyricDisplayFrame(string.Empty, string.Empty);
        }

        var lines = _currentDocument.Lines;
        var currentIndex = -1;
        var timelinePosition = snapshot.Position;
        var displayPosition = timelinePosition + GetLineSwitchLead(_currentLyricSourceApp);
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Timestamp <= displayPosition)
            {
                currentIndex = i;
            }
            else
            {
                break;
            }
        }

        if (currentIndex < 0)
        {
            var firstLine = lines[0];
            if (firstLine.Timestamp > TimeSpan.Zero && timelinePosition < firstLine.Timestamp)
            {
                return new LyricDisplayFrame("...", firstLine.Text, 0, -1);
            }

            return new LyricDisplayFrame(firstLine.Text, lines.Count > 1 ? lines[1].Text : string.Empty, 0, 0);
        }

        var currentText = lines[currentIndex].Text;
        var nextText = currentIndex + 1 < lines.Count ? lines[currentIndex + 1].Text : string.Empty;
        var progress = 0.0;

        if (currentIndex + 1 < lines.Count)
        {
            var start = lines[currentIndex].Timestamp;
            var end = lines[currentIndex + 1].Timestamp;
            var segment = end - start;
            if (segment > TimeSpan.Zero)
            {
                var elapsed = timelinePosition - start;
                progress = Math.Clamp(elapsed.TotalMilliseconds / segment.TotalMilliseconds, 0, 1);
            }
        }

        return new LyricDisplayFrame(currentText, nextText, progress, currentIndex);
    }

    private async Task TryApplyLoadedLyricsAsync(string sourceApp)
    {
        if (_loadingTask is null || !_loadingTask.IsCompleted || string.IsNullOrWhiteSpace(_loadingTrackId))
        {
            return;
        }

        var loadingTrackId = _loadingTrackId;
        var loaded = await _loadingTask;

        _loadingTask = null;
        _loadingTrackId = null;

        if (!string.Equals(_currentTrackId, loadingTrackId, StringComparison.Ordinal))
        {
            return;
        }

        _currentDocument = loaded.Document ?? BuildFallbackDocument(sourceApp);
        _currentLyricSourceApp = loaded.SourceApp;
    }

    private static LyricDocument BuildFallbackDocument(string sourceApp)
    {
        var header = string.Equals(sourceApp, "QQMusic", StringComparison.OrdinalIgnoreCase)
            ? "QQMusic adapter fallback"
            : string.Equals(sourceApp, "Netease", StringComparison.OrdinalIgnoreCase)
                ? "Netease adapter fallback"
                : "Lyrics fallback";

        return new LyricDocument(new[]
        {
            new LyricLine(TimeSpan.Zero, header),
            new LyricLine(TimeSpan.FromSeconds(5), "Lyrics source is not ready yet"),
            new LyricLine(TimeSpan.FromSeconds(10), "You can continue playback while adapter initializes")
        });
    }

    private static TimeSpan GetLineSwitchLead(string? lyricSourceApp)
    {
        if (string.IsNullOrWhiteSpace(lyricSourceApp))
        {
            return DefaultLineSwitchLead;
        }

        if (string.Equals(lyricSourceApp, "QQMusic", StringComparison.OrdinalIgnoreCase))
        {
            return QqMusicLineSwitchLead;
        }

        if (string.Equals(lyricSourceApp, "Netease", StringComparison.OrdinalIgnoreCase))
        {
            return NeteaseLineSwitchLead;
        }

        if (lyricSourceApp.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
        {
            return SpotifyLineSwitchLead;
        }

        return DefaultLineSwitchLead;
    }
}
