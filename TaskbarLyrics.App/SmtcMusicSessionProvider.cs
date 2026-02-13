using System.Diagnostics;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using Windows.Media.Control;

namespace TaskbarLyrics.App;

public sealed class SmtcMusicSessionProvider : IMusicSessionProvider
{
    private readonly SemaphoreSlim _managerLock = new(1, 1);
    private readonly Stopwatch _fallbackTimeline = Stopwatch.StartNew();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public async Task<PlaybackSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var manager = await GetManagerAsync(cancellationToken);
        if (manager is null)
        {
            return BuildProcessFallbackSnapshot();
        }

        var session = SelectSession(manager);
        if (session is null)
        {
            return BuildProcessFallbackSnapshot();
        }

        var playbackInfo = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var sourceApp = NormalizeSource(session.SourceAppUserModelId);

        if (!IsSupportedSource(sourceApp))
        {
            return BuildProcessFallbackSnapshot();
        }

        var isPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var position = timeline.Position;

        string title = string.Empty;
        string artist = string.Empty;

        try
        {
            var media = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken);
            title = media?.Title?.Trim() ?? string.Empty;
            artist = media?.Artist?.Trim() ?? string.Empty;
        }
        catch
        {
            // Some players intermittently fail media property fetch; keep timeline/source fallback.
        }

        TrackInfo? track = null;
        if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(artist) || IsSupportedSource(sourceApp))
        {
            var safeTitle = string.IsNullOrWhiteSpace(title) ? "Unknown Title" : title;
            var safeArtist = string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist;
            var trackId = $"{sourceApp}|{safeTitle}|{safeArtist}";
            track = new TrackInfo(trackId, safeTitle, safeArtist, sourceApp);
        }

        if (track is null)
        {
            return BuildProcessFallbackSnapshot();
        }

        return new PlaybackSnapshot(isPlaying, position, track);
    }

    private PlaybackSnapshot BuildProcessFallbackSnapshot()
    {
        if (IsAnyProcessRunning("cloudmusic", "neteasecloudmusic", "music.163"))
        {
            return new PlaybackSnapshot(
                IsPlaying: true,
                Position: TimeSpan.FromSeconds(_fallbackTimeline.Elapsed.TotalSeconds % 25),
                Track: new TrackInfo(
                    Id: "Netease|ProcessFallback",
                    Title: "Unknown Title",
                    Artist: "Unknown Artist",
                    SourceApp: "Netease"));
        }

        if (IsAnyProcessRunning("qqmusic"))
        {
            return new PlaybackSnapshot(
                IsPlaying: true,
                Position: TimeSpan.FromSeconds(_fallbackTimeline.Elapsed.TotalSeconds % 25),
                Track: new TrackInfo(
                    Id: "QQMusic|ProcessFallback",
                    Title: "Unknown Title",
                    Artist: "Unknown Artist",
                    SourceApp: "QQMusic"));
        }

        return new PlaybackSnapshot(false, TimeSpan.Zero, null);
    }

    private static GlobalSystemMediaTransportControlsSession? SelectSession(
        GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var sessions = manager.GetSessions();
        if (sessions is not null)
        {
            foreach (var candidate in sessions)
            {
                var source = NormalizeSource(candidate.SourceAppUserModelId);
                if (!IsSupportedSource(source))
                {
                    continue;
                }

                var playback = candidate.GetPlaybackInfo();
                if (playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    return candidate;
                }
            }

            foreach (var candidate in sessions)
            {
                var source = NormalizeSource(candidate.SourceAppUserModelId);
                if (IsSupportedSource(source))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private async Task<GlobalSystemMediaTransportControlsSessionManager?> GetManagerAsync(CancellationToken cancellationToken)
    {
        if (_manager is not null)
        {
            return _manager;
        }

        await _managerLock.WaitAsync(cancellationToken);
        try
        {
            if (_manager is not null)
            {
                return _manager;
            }

            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            return _manager;
        }
        catch
        {
            return null;
        }
        finally
        {
            _managerLock.Release();
        }
    }

    private static string NormalizeSource(string sourceAppUserModelId)
    {
        if (sourceAppUserModelId.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) ||
            sourceAppUserModelId.Contains("netease", StringComparison.OrdinalIgnoreCase) ||
            sourceAppUserModelId.Contains("163music", StringComparison.OrdinalIgnoreCase) ||
            sourceAppUserModelId.Contains("music.163", StringComparison.OrdinalIgnoreCase) ||
            sourceAppUserModelId.Contains("wyy", StringComparison.OrdinalIgnoreCase))
        {
            return "Netease";
        }

        if (sourceAppUserModelId.Contains("qqmusic", StringComparison.OrdinalIgnoreCase) ||
            sourceAppUserModelId.Contains("qq", StringComparison.OrdinalIgnoreCase))
        {
            return "QQMusic";
        }

        return sourceAppUserModelId;
    }

    private static bool IsSupportedSource(string sourceApp)
    {
        return string.Equals(sourceApp, "Netease", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sourceApp, "QQMusic", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAnyProcessRunning(params string[] names)
    {
        foreach (var rawName in names)
        {
            var name = rawName.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            try
            {
                if (Process.GetProcessesByName(name).Length > 0)
                {
                    return true;
                }
            }
            catch
            {
                // Ignore access/process-query failures and continue.
            }
        }

        return false;
    }
}
