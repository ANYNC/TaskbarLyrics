using System.Diagnostics;
using System.Text.RegularExpressions;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace TaskbarLyrics.App;

public sealed class SmtcMusicSessionProvider : IMusicSessionProvider
{
    private static readonly string[] DefaultRecognitionOrder = { "QQMusic", "Netease", "Spotify" };
    private static readonly Regex TitleArtistRegex = new(
        @"^(?<title>.+?)\s*[-|—]\s*(?<artist>.+)$",
        RegexOptions.Compiled);

    private readonly SemaphoreSlim _managerLock = new(1, 1);
    private readonly TimelinePositionStrategyRegistry _timelineStrategyRegistry = TimelinePositionStrategyRegistry.CreateDefault();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private SmtcTimelineDiagnostics? _lastTimelineDiagnostics;
    private string? _currentLyricSourceApp;
    private string[] _recognitionOrder = DefaultRecognitionOrder;

    public void SetRecognitionOrder(IReadOnlyList<string>? order)
    {
        var normalized = NormalizeRecognitionOrder(order);
        _recognitionOrder = normalized.ToArray();
    }

    public SmtcTimelineDiagnostics? GetLastTimelineDiagnostics()
    {
        return _lastTimelineDiagnostics;
    }

    public void SetCurrentLyricSource(string? sourceApp)
    {
        _currentLyricSourceApp = sourceApp;
    }

    public string GetCurrentLyricSource()
    {
        return _currentLyricSourceApp ?? string.Empty;
    }

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
        var nowUtc = DateTimeOffset.UtcNow;

        var rawSource = NormalizeSource(session.SourceAppUserModelId);
        var sourceApp = ResolveSourceWithProcessFallback(rawSource);
        var sourceFromFallback = !IsSupportedSource(rawSource) && IsSupportedSource(sourceApp);

        var isPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var position = timeline.Position;
        var extrapolatedPosition = ComputeExtrapolatedPosition(timeline, isPlaying, nowUtc, out var lastUpdateAge);

        string title = string.Empty;
        string artist = string.Empty;
        byte[]? coverImageBytes = null;

        try
        {
            var media = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken);
            title = media?.Title?.Trim() ?? string.Empty;
            artist = media?.Artist?.Trim() ?? string.Empty;

            if (!sourceFromFallback)
            {
                coverImageBytes = await ReadCoverBytesAsync(media?.Thumbnail, cancellationToken);
            }
        }
        catch
        {
            // Keep fallback chain alive.
        }

        if (sourceApp.Equals("Netease", StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(title) || string.Equals(title, "Unknown Title", StringComparison.OrdinalIgnoreCase)))
        {
            if (TryInferTrackFromNeteaseProcess(out var inferredTitle, out var inferredArtist))
            {
                title = inferredTitle;
                artist = inferredArtist;
            }
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Unknown Title";
        }

        if (string.IsNullOrWhiteSpace(artist))
        {
            artist = "Unknown Artist";
        }

        var trackId = $"{sourceApp}|{title}|{artist}";
        var track = new TrackInfo(trackId, title, artist, sourceApp);
        var diagnostics = new SmtcTimelineDiagnostics(
            CapturedAtUtc: nowUtc,
            SourceAppUserModelId: session.SourceAppUserModelId ?? string.Empty,
            NormalizedSource: rawSource,
            ResolvedSource: sourceApp,
            IsPlaying: isPlaying,
            RawPosition: position,
            LastUpdatedTimeUtc: timeline.LastUpdatedTime,
            LastUpdateAge: lastUpdateAge,
            ExtrapolatedPosition: extrapolatedPosition,
            SelectedPosition: position,
            StrategyName: string.Empty,
            Title: title,
            Artist: artist,
            IsFallbackSnapshot: false);
        var selection = _timelineStrategyRegistry.Select(diagnostics);
        diagnostics = diagnostics with
        {
            SelectedPosition = selection.Position,
            StrategyName = selection.StrategyName
        };
        PublishDiagnostics(diagnostics);
        return new PlaybackSnapshot(
            IsPlaying: isPlaying,
            Position: selection.Position,
            Track: track,
            CoverImageBytes: coverImageBytes,
            RawPosition: position,
            ExtrapolatedPosition: extrapolatedPosition);
    }

    private PlaybackSnapshot BuildProcessFallbackSnapshot()
    {
        var preferredSource = DetectRunningSource();
        if (string.Equals(preferredSource, "Netease", StringComparison.OrdinalIgnoreCase))
        {
            if (TryInferTrackFromNeteaseProcess(out var inferredTitle, out var inferredArtist))
            {
                PublishFallbackDiagnostics(
                    sourceAppUserModelId: string.Empty,
                    resolvedSource: "Netease",
                    title: inferredTitle,
                    artist: inferredArtist);
                return new PlaybackSnapshot(
                    IsPlaying: false,
                    Position: TimeSpan.Zero,
                    Track: new TrackInfo(
                        Id: $"Netease|{inferredTitle}|{inferredArtist}",
                        Title: inferredTitle,
                        Artist: inferredArtist,
                        SourceApp: "Netease"),
                    CoverImageBytes: null);
            }

            PublishFallbackDiagnostics(
                sourceAppUserModelId: string.Empty,
                resolvedSource: "Netease",
                title: "Unknown Title",
                artist: "Unknown Artist");
            return new PlaybackSnapshot(
                IsPlaying: false,
                Position: TimeSpan.Zero,
                Track: new TrackInfo(
                    Id: "Netease|ProcessFallback",
                    Title: "Unknown Title",
                    Artist: "Unknown Artist",
                    SourceApp: "Netease"),
                CoverImageBytes: null);
        }

        if (string.Equals(preferredSource, "QQMusic", StringComparison.OrdinalIgnoreCase))
        {
            PublishFallbackDiagnostics(
                sourceAppUserModelId: string.Empty,
                resolvedSource: "QQMusic",
                title: "Unknown Title",
                artist: "Unknown Artist");
            return new PlaybackSnapshot(
                IsPlaying: false,
                Position: TimeSpan.Zero,
                Track: new TrackInfo(
                    Id: "QQMusic|ProcessFallback",
                    Title: "Unknown Title",
                    Artist: "Unknown Artist",
                    SourceApp: "QQMusic"),
                CoverImageBytes: null);
        }

        PublishFallbackDiagnostics(string.Empty, preferredSource ?? string.Empty, string.Empty, string.Empty);
        return new PlaybackSnapshot(false, TimeSpan.Zero, null);
    }

    private static TimeSpan ComputeExtrapolatedPosition(
        GlobalSystemMediaTransportControlsSessionTimelineProperties timeline,
        bool isPlaying,
        DateTimeOffset nowUtc,
        out TimeSpan lastUpdateAge)
    {
        lastUpdateAge = nowUtc - timeline.LastUpdatedTime;
        if (lastUpdateAge < TimeSpan.Zero)
        {
            lastUpdateAge = TimeSpan.Zero;
        }

        if (!isPlaying)
        {
            return timeline.Position;
        }

        return timeline.Position + lastUpdateAge;
    }

    private void PublishDiagnostics(SmtcTimelineDiagnostics diagnostics)
    {
        _lastTimelineDiagnostics = diagnostics;
    }

    private void PublishFallbackDiagnostics(
        string sourceAppUserModelId,
        string resolvedSource,
        string title,
        string artist)
    {
        PublishDiagnostics(new SmtcTimelineDiagnostics(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            SourceAppUserModelId: sourceAppUserModelId,
            NormalizedSource: string.Empty,
            ResolvedSource: resolvedSource,
            IsPlaying: false,
            RawPosition: TimeSpan.Zero,
            LastUpdatedTimeUtc: DateTimeOffset.UtcNow,
            LastUpdateAge: TimeSpan.Zero,
            ExtrapolatedPosition: TimeSpan.Zero,
            SelectedPosition: TimeSpan.Zero,
            StrategyName: "Fallback",
            Title: title,
            Artist: artist,
            IsFallbackSnapshot: true));
    }

    private GlobalSystemMediaTransportControlsSession? SelectSession(
        GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var sessions = manager.GetSessions();
        if (sessions is not null)
        {
            var supportedPlaying = sessions
                .Select(candidate => new
                {
                    Session = candidate,
                    Source = NormalizeSource(candidate.SourceAppUserModelId)
                })
                .Where(x => IsSupportedSource(x.Source) && IsSessionPlaying(x.Session))
                .OrderBy(x => GetRecognitionPriority(x.Source))
                .FirstOrDefault();
            if (supportedPlaying is not null)
            {
                return supportedPlaying.Session;
            }

            var supportedAny = sessions
                .Select(candidate => new
                {
                    Session = candidate,
                    Source = NormalizeSource(candidate.SourceAppUserModelId)
                })
                .Where(x => IsSupportedSource(x.Source))
                .OrderBy(x => GetRecognitionPriority(x.Source))
                .FirstOrDefault();
            if (supportedAny is not null)
            {
                return supportedAny.Session;
            }

            foreach (var candidate in sessions)
            {
                if (IsBlockedSource(candidate.SourceAppUserModelId))
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
                if (!IsBlockedSource(candidate.SourceAppUserModelId))
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
        if (sourceAppUserModelId.Contains("spotify", StringComparison.OrdinalIgnoreCase))
        {
            return "Spotify";
        }

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
               string.Equals(sourceApp, "QQMusic", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(sourceApp, "Spotify", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlockedSource(string sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
        {
            return false;
        }

        return sourceAppUserModelId.Contains("explorer", StringComparison.OrdinalIgnoreCase) ||
               sourceAppUserModelId.Contains("shellexperiencehost", StringComparison.OrdinalIgnoreCase) ||
               sourceAppUserModelId.Contains("searchhost", StringComparison.OrdinalIgnoreCase) ||
               sourceAppUserModelId.Contains("startmenuexperiencehost", StringComparison.OrdinalIgnoreCase) ||
               sourceAppUserModelId.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
               sourceAppUserModelId.Contains("microsoftedge", StringComparison.OrdinalIgnoreCase) ||
               sourceAppUserModelId.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
               sourceAppUserModelId.Contains("firefox", StringComparison.OrdinalIgnoreCase) ||
               sourceAppUserModelId.Contains("opera", StringComparison.OrdinalIgnoreCase) ||
               sourceAppUserModelId.Contains("brave", StringComparison.OrdinalIgnoreCase) ||
               sourceAppUserModelId.Contains("arc", StringComparison.OrdinalIgnoreCase) ||
               sourceAppUserModelId.Contains("vivaldi", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAnyProcessRunning(params string[] names)
    {
        Process[] allProcesses;
        try
        {
            allProcesses = Process.GetProcesses();
        }
        catch
        {
            return false;
        }

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

                if (allProcesses.Any(p =>
                {
                    try
                    {
                        return p.ProcessName.Contains(name, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                }))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore query failures.
            }
        }

        return false;
    }

    private string ResolveSourceWithProcessFallback(string sourceApp)
    {
        if (!string.IsNullOrWhiteSpace(sourceApp) && !IsBlockedSource(sourceApp))
        {
            return sourceApp;
        }

        var detected = DetectRunningSource();
        return detected ?? sourceApp;
    }

    private string? DetectRunningSource()
    {
        foreach (var source in _recognitionOrder)
        {
            if (source.Equals("QQMusic", StringComparison.OrdinalIgnoreCase) &&
                IsAnyProcessRunning("qqmusic", "qqmusicapp", "qqmusicplayer"))
            {
                return "QQMusic";
            }

            if (source.Equals("Netease", StringComparison.OrdinalIgnoreCase) &&
                IsAnyProcessRunning("cloudmusic", "neteasecloudmusic", "neteasemusic", "music.163", "music163"))
            {
                return "Netease";
            }

            if (source.Equals("Spotify", StringComparison.OrdinalIgnoreCase) &&
                IsAnyProcessRunning("spotify"))
            {
                return "Spotify";
            }
        }

        return null;
    }

    private int GetRecognitionPriority(string source)
    {
        for (var i = 0; i < _recognitionOrder.Length; i++)
        {
            if (string.Equals(_recognitionOrder[i], source, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static bool IsSessionPlaying(GlobalSystemMediaTransportControlsSession session)
    {
        var playback = session.GetPlaybackInfo();
        return playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
    }

    private static List<string> NormalizeRecognitionOrder(IReadOnlyList<string>? order)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (order is not null)
        {
            foreach (var item in order)
            {
                var normalized = NormalizeRecognitionSource(item);
                if (!string.IsNullOrWhiteSpace(normalized) && seen.Add(normalized))
                {
                    result.Add(normalized);
                }
            }
        }

        foreach (var source in DefaultRecognitionOrder)
        {
            if (seen.Add(source))
            {
                result.Add(source);
            }
        }

        return result;
    }

    private static string NormalizeRecognitionSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        if (source.Contains("qq", StringComparison.OrdinalIgnoreCase))
        {
            return "QQMusic";
        }

        if (source.Contains("netease", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase))
        {
            return "Netease";
        }

        if (source.Contains("spotify", StringComparison.OrdinalIgnoreCase))
        {
            return "Spotify";
        }

        return string.Empty;
    }

    private static bool TryInferTrackFromNeteaseProcess(out string title, out string artist)
    {
        title = string.Empty;
        artist = string.Empty;

        Process[] allProcesses;
        try
        {
            allProcesses = Process.GetProcesses();
        }
        catch
        {
            return false;
        }

        foreach (var process in allProcesses)
        {
            string processName;
            string windowTitle;
            try
            {
                processName = process.ProcessName;
                windowTitle = process.MainWindowTitle?.Trim() ?? string.Empty;
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(windowTitle))
            {
                continue;
            }

            if (!(processName.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) ||
                  processName.Contains("netease", StringComparison.OrdinalIgnoreCase) ||
                  processName.Contains("163", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (windowTitle.Contains("网易云音乐", StringComparison.OrdinalIgnoreCase) &&
                windowTitle.Length <= 12)
            {
                continue;
            }

            var match = TitleArtistRegex.Match(windowTitle);
            if (!match.Success)
            {
                continue;
            }

            title = match.Groups["title"].Value.Trim();
            artist = match.Groups["artist"].Value.Trim();

            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(artist))
            {
                artist = "Unknown Artist";
            }

            return true;
        }

        return false;
    }

    private static async Task<byte[]?> ReadCoverBytesAsync(
        IRandomAccessStreamReference? thumbnail,
        CancellationToken cancellationToken)
    {
        if (thumbnail is null)
        {
            return null;
        }

        try
        {
            using var stream = await thumbnail.OpenReadAsync().AsTask(cancellationToken);
            using var input = stream.GetInputStreamAt(0);
            using var dataReader = new DataReader(input);
            var size = (uint)stream.Size;
            await dataReader.LoadAsync(size).AsTask(cancellationToken);
            var bytes = new byte[size];
            dataReader.ReadBytes(bytes);
            return bytes.Length == 0 ? null : bytes;
        }
        catch
        {
            return null;
        }
    }

}
