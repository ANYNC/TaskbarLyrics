using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Core.Services;

public sealed class LyricSyncService : IDisposable
{
    public const string SearchingText = "正在检索歌词...";
    public const string NoLyricsText = "暂未找到歌词";
    private static readonly TimeSpan StartupLineGuardPositionThreshold = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DefaultMetadataStabilizationDelay = TimeSpan.FromMilliseconds(750);

    private readonly ILyricResolutionCoordinator _coordinator;
    private readonly Func<string?, bool> _shouldShowTranslation;
    private readonly Func<string?, TimeSpan> _getPlayerLeadTime;
    private readonly Func<TrackInfo?, string?, TimeSpan> _getTrackLeadTime;
    private readonly TimeSpan _metadataStabilizationDelay;
    private TrackInfo? _currentTrack;
    private string? _currentTrackId;
    private LyricDocument? _currentDocument;
    private string? _currentLyricSourceApp;
    private LyricAcquisitionKind _currentLyricAcquisition = LyricAcquisitionKind.Unknown;
    private long _currentLyricFetchElapsedMilliseconds;
    private DateTimeOffset? _currentLyricResolvedAtUtc;
    private bool _isUpdating;
    private CancellationTokenSource? _searchCts;
    private Task? _searchTask;
    private bool _isDisposed;
    private bool _durationCorrectionConsumed;
    private int _lastEmittedLineIndex = -1;
    private long _documentLoadedTicks;
    private TimeSpan? _lastSearchDuration;

    public string? CurrentLyricSourceApp => _currentLyricSourceApp;
    public LyricAcquisitionKind CurrentLyricAcquisition => _currentLyricAcquisition;
    public long CurrentLyricFetchElapsedMilliseconds => _currentLyricFetchElapsedMilliseconds;
    public DateTimeOffset? CurrentLyricResolvedAtUtc => _currentLyricResolvedAtUtc;

    public LyricSyncService(
        ILyricResolutionCoordinator coordinator,
        Func<string?, bool>? shouldShowTranslation = null,
        Func<string?, TimeSpan>? getPlayerLeadTime = null,
        Func<TrackInfo?, string?, TimeSpan>? getTrackLeadTime = null,
        TimeSpan? metadataStabilizationDelay = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _shouldShowTranslation = shouldShowTranslation ?? (_ => true);
        _getPlayerLeadTime = getPlayerLeadTime ?? (_ => TimeSpan.Zero);
        _getTrackLeadTime = getTrackLeadTime ?? ((_, _) => TimeSpan.Zero);
        _metadataStabilizationDelay = metadataStabilizationDelay ?? DefaultMetadataStabilizationDelay;
        if (_metadataStabilizationDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(metadataStabilizationDelay),
                "Metadata stabilization delay cannot be negative.");
        }
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
            _lastSearchDuration = null;
            _durationCorrectionConsumed = false;
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
            _lastSearchDuration = null;
            _durationCorrectionConsumed = false;
            _lastEmittedLineIndex = -1;
            StartLyricsUpdate(trackId);
        }
        else
        {
            _currentTrack = snapshot.Track;
            if (ShouldCorrectDuration(snapshot.Track.Duration))
            {
                _durationCorrectionConsumed = true;
                _currentDocument = null;
                _currentLyricSourceApp = null;
                _currentLyricAcquisition = LyricAcquisitionKind.Searching;
                _currentLyricFetchElapsedMilliseconds = 0;
                _currentLyricResolvedAtUtc = null;
                _lastEmittedLineIndex = -1;
                StartLyricsUpdate(trackId);
            }
        }

        if (_currentDocument == null || _currentDocument.Lines.Count == 0)
        {
            return Task.FromResult(new LyricDisplayFrame(
                _isUpdating ? SearchingText : NoLyricsText,
                "",
                _currentTrack?.Title ?? "",
                0, -1));
        }

        // Both offsets shift the playback position; lyric timestamps stay immutable.
        var sourceLead = _getPlayerLeadTime(_currentTrack?.SourceApp);
        var trackLead = _getTrackLeadTime(_currentTrack, _currentLyricSourceApp);
        var position = snapshot.Position + sourceLead + trackLead;

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

    private void StartLyricsUpdate(string trackId)
    {
        CancelPendingSearch();
        _searchCts = new CancellationTokenSource();
        var cts = _searchCts;
        _isUpdating = true;
        _searchTask = UpdateLyricsAsync(trackId, cts);
    }

    private async Task UpdateLyricsAsync(string trackId, CancellationTokenSource cts)
    {
        TrackInfo? searchTrack = null;
        try
        {
            await Task.Delay(_metadataStabilizationDelay, cts.Token);
            if (_currentTrackId != trackId || _currentTrack is not { } track)
            {
                return;
            }

            searchTrack = track;
            _lastSearchDuration = NormalizeDuration(track.Duration);
            var resolved = await _coordinator.ResolveAsync(track, cts.Token);

            if (cts.IsCancellationRequested) return;
            var document = resolved is null
                ? null
                : ResolvedLyricsCompatibilityProjector.ToLyricDocument(
                    resolved,
                    includeInformationLines: false);
            if (resolved is not null && document is { Lines.Count: > 0 } && _currentTrackId == trackId)
            {
                _currentDocument = document;
                _currentLyricSourceApp = resolved.ProviderId.Value;
                _currentLyricAcquisition = resolved.Acquisition;
                _currentLyricFetchElapsedMilliseconds = ReadElapsedMilliseconds(resolved.Diagnostics);
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
        catch (Exception exception)
        {
            Log.Warn($"Lyrics update failed for '{searchTrack?.Title}' - '{searchTrack?.Artist}': {exception}");
            if (_currentTrackId == trackId)
            {
                _currentLyricAcquisition = LyricAcquisitionKind.NotFound;
                _currentLyricFetchElapsedMilliseconds = 0;
                _currentLyricResolvedAtUtc = DateTimeOffset.UtcNow;
            }
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

    private bool ShouldCorrectDuration(TimeSpan latestDuration)
    {
        if (_durationCorrectionConsumed || _lastSearchDuration is not { } searchedDuration)
        {
            return false;
        }

        var normalizedLatestDuration = NormalizeDuration(latestDuration);
        if (normalizedLatestDuration <= TimeSpan.Zero)
        {
            return false;
        }

        return searchedDuration <= TimeSpan.Zero ||
               (normalizedLatestDuration - searchedDuration).Duration() >=
               LyricMatchingPolicy.DurationConflictThreshold;
    }

    private static TimeSpan NormalizeDuration(TimeSpan duration)
    {
        return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
    }

    public static string BuildStableTrackIdentity(TrackInfo track)
    {
        // Keep the visible-song identity stable while SMTC fills SongId and Duration.
        // A material Duration correction is handled separately and at most once.
        return $"{NormalizeIdentityPart(track.SourceApp)}|{NormalizeIdentityPart(track.Title)}|{NormalizeIdentityPart(track.Artist)}";
    }

    private static string NormalizeIdentityPart(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
    }

    private static long ReadElapsedMilliseconds(IReadOnlyDictionary<string, string> diagnostics)
    {
        return diagnostics.TryGetValue("elapsedMs", out var value) &&
               long.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var elapsed)
            ? Math.Max(0, elapsed)
            : 0;
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
        _coordinator.Dispose();
    }

}
