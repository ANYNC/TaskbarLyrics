using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Core.Services;

public sealed class LyricSyncService : IDisposable
{
    public const string SearchingText = "正在检索歌词...";
    public const string NoLyricsText = "暂未找到歌词";
    private static readonly TimeSpan StartupLineGuardPositionThreshold = TimeSpan.FromMilliseconds(500);

    private readonly ILyricProviderRegistry _registry;
    private readonly Func<string?, bool> _shouldShowTranslation;
    private readonly Func<string?, TimeSpan> _getPlayerLeadTime;
    private TrackInfo? _currentTrack;
    private string? _currentTrackId;
    private LyricDocument? _currentDocument;
    private string? _currentLyricSourceApp;
    private LyricAcquisitionKind _currentLyricAcquisition = LyricAcquisitionKind.Unknown;
    private long _currentLyricFetchElapsedMilliseconds;
    private DateTimeOffset? _currentLyricResolvedAtUtc;
    private bool _isUpdating;
    private CancellationTokenSource? _searchCts;
    private bool _isDisposed;
    private int _lastEmittedLineIndex = -1;
    private long _documentLoadedTicks;

    public string? CurrentLyricSourceApp => _currentLyricSourceApp;
    public LyricAcquisitionKind CurrentLyricAcquisition => _currentLyricAcquisition;
    public long CurrentLyricFetchElapsedMilliseconds => _currentLyricFetchElapsedMilliseconds;
    public DateTimeOffset? CurrentLyricResolvedAtUtc => _currentLyricResolvedAtUtc;

    public LyricSyncService(
        ILyricProviderRegistry registry,
        Func<string?, bool>? shouldShowTranslation = null,
        Func<string?, TimeSpan>? getPlayerLeadTime = null)
    {
        _registry = registry;
        _shouldShowTranslation = shouldShowTranslation ?? (_ => true);
        _getPlayerLeadTime = getPlayerLeadTime ?? (_ => TimeSpan.Zero);
    }

    public Task<LyricDisplayFrame> GetDisplayFrameAsync(PlaybackSnapshot snapshot)
    {
        if (snapshot.Track == null)
        {
            CancelPendingSearch();
            _currentTrack = null;
            _currentTrackId = null;
            _currentDocument = null;
            _currentLyricSourceApp = null;
            _currentLyricAcquisition = LyricAcquisitionKind.Unknown;
            _currentLyricFetchElapsedMilliseconds = 0;
            _currentLyricResolvedAtUtc = null;
            _lastEmittedLineIndex = -1;
            return Task.FromResult(new LyricDisplayFrame("", "", "", 0, -1));
        }

        var trackId = BuildStableTrackIdentity(snapshot.Track);
        if (trackId != _currentTrackId)
        {
            _currentTrack = snapshot.Track;
            _currentTrackId = trackId;
            _currentDocument = null;
            _currentLyricSourceApp = null;
            _currentLyricAcquisition = LyricAcquisitionKind.Searching;
            _currentLyricFetchElapsedMilliseconds = 0;
            _currentLyricResolvedAtUtc = null;
            _lastEmittedLineIndex = -1;
            _ = UpdateLyricsAsync(snapshot.Track, trackId);
        }

        if (_currentDocument == null || _currentDocument.Lines.Count == 0)
        {
            return Task.FromResult(new LyricDisplayFrame(
                _isUpdating ? SearchingText : NoLyricsText,
                "",
                _currentTrack?.Title ?? "",
                0, -1));
        }

        // Apply player-specific compensation
        var sourceLead = _getPlayerLeadTime(_currentTrack?.SourceApp);
        var position = snapshot.Position + sourceLead;

        var lines = _currentDocument.Lines;
        var currentIdx = FindCurrentLineIndex(lines, position);

        // Grace period: for the first 300ms after lyrics load, SMTC position
        // is often stale or over-extrapolated (residual from the previous track).
        // Force lineIndex to 0 to avoid showing the wrong starting line.
        var msSinceLoad = Environment.TickCount64 - _documentLoadedTicks;
        if (msSinceLoad < 300 &&
            _lastEmittedLineIndex < 0 &&
            position <= StartupLineGuardPositionThreshold)
        {
            currentIdx = currentIdx < 0 ? -1 : 0;
        }

        if (currentIdx >= 0)
        {
            _lastEmittedLineIndex = currentIdx;
        }

        var displayIdx = currentIdx < 0 ? 0 : currentIdx;

        if (displayIdx == 0 && currentIdx == -1)
        {
            // If before first line, show the first line as prepared current
            var firstLine = lines[0];
            string firstText = firstLine.Text;
            if (CanShowTranslation() && !string.IsNullOrWhiteSpace(firstLine.Translation))
            {
                firstText += " (" + firstLine.Translation + ")";
            }

            var nextTxt = lines.Count > 1 ? lines[1].Text : "";
            if (CanShowTranslation() && lines.Count > 1 && !string.IsNullOrWhiteSpace(lines[1].Translation))
            {
                nextTxt += " (" + lines[1].Translation + ")";
            }

            return Task.FromResult(new LyricDisplayFrame(firstText, nextTxt, _currentTrack?.Title ?? "", 0, 0, _currentDocument.IsPureMusic));
        }

        var currentLine = lines[displayIdx];
        var nextLine = (displayIdx + 1 < lines.Count) ? lines[displayIdx + 1] : null;

        // Smart text merging: if translation exists, append it.
        // This ensures the "NextLine" correctly shows the next lyric for animation,
        // while still making translations visible in the taskbar's limited space.
        string currentText = currentLine.Text;
        if (CanShowTranslation() && !string.IsNullOrWhiteSpace(currentLine.Translation))
        {
            // We use a small space and parens for a clean look in the taskbar
            currentText += " (" + currentLine.Translation + ")";
        }

        string nextText = nextLine?.Text ?? "";
        if (CanShowTranslation() && nextLine != null && !string.IsNullOrWhiteSpace(nextLine.Translation))
        {
            nextText += " (" + nextLine.Translation + ")";
        }

        // Calculate progress within line for syllable animation
        double progress = 0;
        if (nextLine != null)
        {
            var duration = nextLine.Timestamp - currentLine.Timestamp;
            var elapsed = position - currentLine.Timestamp;
            if (duration > TimeSpan.Zero)
            {
                progress = Math.Clamp(elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            }
        }

        return Task.FromResult(new LyricDisplayFrame(
            currentText,
            nextText,
            _currentTrack?.Title ?? "",
            progress,
            displayIdx,
            _currentDocument.IsPureMusic
        ));
    }

    private async Task UpdateLyricsAsync(TrackInfo track, string trackId)
    {
        // Cancel any ongoing search for the previous track immediately
        CancelPendingSearch();
        _searchCts = new CancellationTokenSource();
        var cts = _searchCts;

        _isUpdating = true;

        try
        {
            var results = await _registry.ResolveLyricsAsync(track, cts.Token);

            if (cts.IsCancellationRequested) return;
            // Pick the best match
            var bestResult = results
                .Where(r => r.Document != null && r.Document.Lines.Count > 0)
                .OrderByDescending(r => r.Document!.BestScore)
                .ThenBy(r => r.SourceApp == "QQMusic" || r.SourceApp == "Netease" ? 0 : 1) 
                .FirstOrDefault();

            if (bestResult != null && _currentTrackId == trackId)
            {
                _currentDocument = bestResult.Document;
                _currentLyricSourceApp = bestResult.SourceApp;
                _currentLyricAcquisition = bestResult.Acquisition;
                _currentLyricFetchElapsedMilliseconds = bestResult.ElapsedMilliseconds;
                _currentLyricResolvedAtUtc = DateTimeOffset.UtcNow;
                _documentLoadedTicks = Environment.TickCount64;
                _lastEmittedLineIndex = -1;
            }
            else if (_currentTrackId == trackId)
            {
                _currentLyricAcquisition = LyricAcquisitionKind.NotFound;
                _currentLyricFetchElapsedMilliseconds = 0;
                _currentLyricResolvedAtUtc = DateTimeOffset.UtcNow;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // A newer track replaced this request.
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                _searchCts = null;
                _isUpdating = false;
            }

            cts.Dispose();
        }
    }

    public static string BuildStableTrackIdentity(TrackInfo track)
    {
        // SMTC metadata can arrive in waves: SongId and Duration are often filled
        // or corrected after lyrics have already loaded. They should not reset the
        // lyric document for the same visible song.
        return $"{NormalizeIdentityPart(track.SourceApp)}|{NormalizeIdentityPart(track.Title)}|{NormalizeIdentityPart(track.Artist)}";
    }

    private static string NormalizeIdentityPart(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
    }

    private bool CanShowTranslation()
    {
        return _shouldShowTranslation(_currentLyricSourceApp);
    }

    private static int FindCurrentLineIndex(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        var currentIdx = -1;
        TimeSpan? currentTimestamp = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var timestamp = lines[i].Timestamp;
            if (timestamp > position)
            {
                break;
            }

            if (currentTimestamp != timestamp)
            {
                currentTimestamp = timestamp;
                currentIdx = i;
            }
        }

        return currentIdx;
    }

    private void CancelPendingSearch()
    {
        var cts = _searchCts;
        _searchCts = null;
        _isUpdating = false;
        cts?.Cancel();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        CancelPendingSearch();
    }

}
