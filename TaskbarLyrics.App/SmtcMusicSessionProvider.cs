using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace TaskbarLyrics.App;

public sealed class SmtcMusicSessionProvider : IMusicSessionProvider
{
    private static readonly string[] DefaultRecognitionOrder = { "QQMusic", "Netease", "Kugou", "Spotify" };
    private static readonly TimeSpan MissingCoverRetryInterval = TimeSpan.FromSeconds(5);
    private static readonly Regex TitleArtistRegex = new(
        @"^(?<title>.+?)\s*[-|—]\s*(?<artist>.+)$",
        RegexOptions.Compiled);

    private readonly SemaphoreSlim _managerLock = new(1, 1);
    private readonly TimelinePositionStrategyRegistry _timelineStrategyRegistry = TimelinePositionStrategyRegistry.CreateDefault();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private SmtcTimelineDiagnostics? _lastTimelineDiagnostics;
    private string? _currentLyricSourceApp;
    private int _currentPlayerLyricOffsetMilliseconds;
    private int _currentTrackLyricOffsetMilliseconds;
    private string[] _recognitionOrder = DefaultRecognitionOrder;
    private HashSet<string> _enabledSources = new(DefaultRecognitionOrder, StringComparer.OrdinalIgnoreCase);
    private string _lastCoverMetadataKey = string.Empty;
    private string _lastQqMetadataDiagnosticsKey = string.Empty;
    private byte[]? _lastCoverImageBytes;
    private DateTimeOffset _nextMissingCoverRetryUtc;
    private readonly object _coverLock = new();
    private Task? _coverReadTask;
    private string _coverReadMetadataKey = string.Empty;
    private int _coverReadSequence;
    private string _lastCoverThumbnailDiagnosticsKey = string.Empty;
    private string _lastMediaPropertiesErrorKey = string.Empty;
    private DateTimeOffset _nextMediaPropertiesErrorLogUtc;

    public void SetRecognitionOrder(
        IReadOnlyList<string>? order,
        IReadOnlyCollection<string>? enabledSources = null)
    {
        _enabledSources = enabledSources is null
            ? new HashSet<string>(DefaultRecognitionOrder, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(enabledSources, StringComparer.OrdinalIgnoreCase);
        var normalized = NormalizeRecognitionOrder(order, _enabledSources);
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

    public void SetCurrentLyricOffsets(int playerOffsetMilliseconds, int trackOffsetMilliseconds)
    {
        Volatile.Write(ref _currentPlayerLyricOffsetMilliseconds, playerOffsetMilliseconds);
        Volatile.Write(ref _currentTrackLyricOffsetMilliseconds, trackOffsetMilliseconds);
    }

    public LyricOffsetDiagnostics GetCurrentLyricOffsets()
    {
        var playerOffset = Volatile.Read(ref _currentPlayerLyricOffsetMilliseconds);
        var trackOffset = Volatile.Read(ref _currentTrackLyricOffsetMilliseconds);
        return new LyricOffsetDiagnostics(playerOffset, trackOffset, playerOffset + trackOffset);
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

        var isPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var position = timeline.Position;
        var extrapolatedPosition = ComputeExtrapolatedPosition(timeline, isPlaying, nowUtc, out var lastUpdateAge);

        string title = string.Empty;
        string artist = string.Empty;
        string album = string.Empty;
        byte[]? coverImageBytes = null;
        bool isCoverLoading = false;
        string? songId = null;

        try
        {
            var media = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken);
            title = media?.Title?.Trim() ?? string.Empty;
            artist = media?.Artist?.Trim() ?? string.Empty;
            album = media?.AlbumTitle?.Trim() ?? string.Empty;

            (coverImageBytes, isCoverLoading) = GetCoverBytes(
                sourceApp,
                title,
                artist,
                media?.Thumbnail,
                nowUtc);

            // 从 Genres 流派元数据列表中解析播放器写入的 SongId (例如网易云 NCM-, QQ音乐 QQ-)
            if (media?.Genres != null)
            {
                if (sourceApp.Equals("Netease", StringComparison.OrdinalIgnoreCase))
                {
                    songId = media.Genres
                        .FirstOrDefault(x => x.StartsWith("NCM-", StringComparison.OrdinalIgnoreCase))?
                        .Replace("NCM-", "");
                }
                else if (sourceApp.Equals("QQMusic", StringComparison.OrdinalIgnoreCase))
                {
                    songId = media.Genres
                        .FirstOrDefault(x => x.StartsWith("QQ-", StringComparison.OrdinalIgnoreCase))?
                        .Replace("QQ-", "");
                }

                if (sourceApp.Equals("QQMusic", StringComparison.OrdinalIgnoreCase))
                {
                    var genresText = string.Join(" | ", media.Genres);
                    var diagnosticsKey = $"{title}|{artist}|{album}|{songId}|{genresText}";
                    if (!string.Equals(_lastQqMetadataDiagnosticsKey, diagnosticsKey, StringComparison.Ordinal))
                    {
                        _lastQqMetadataDiagnosticsKey = diagnosticsKey;
                        Log.Debug($"SMTC QQMusic metadata: Album='{album}', SongId='{songId ?? string.Empty}', Genres='{genresText}'");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Keep fallback chain alive.
            var errorKey = $"{sourceApp}|{ex.GetType().FullName}|{ex.HResult:X8}|{ex.Message}";
            if (!string.Equals(errorKey, _lastMediaPropertiesErrorKey, StringComparison.Ordinal) ||
                nowUtc >= _nextMediaPropertiesErrorLogUtc)
            {
                _lastMediaPropertiesErrorKey = errorKey;
                _nextMediaPropertiesErrorLogUtc = nowUtc.AddSeconds(30);
                Log.Diagnostic(
                    "COVER-SMTC",
                    $"MediaPropertiesFailed Source='{ToLogValue(sourceApp)}' Exception='{ex.GetType().Name}' " +
                    $"HResult=0x{ex.HResult:X8} Message='{ToLogValue(ex.Message)}'");
            }
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
        var track = new TrackInfo(trackId, title, artist, album, sourceApp, timeline.EndTime, songId);
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
            ExtrapolatedPosition: extrapolatedPosition,
            IsCoverLoading: isCoverLoading);
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
                        Album: string.Empty,
                        SourceApp: "Netease",
                        Duration: TimeSpan.Zero),
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
                    Album: string.Empty,
                    SourceApp: "Netease",
                    Duration: TimeSpan.Zero),
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
                    Album: string.Empty,
                    SourceApp: "QQMusic",
                    Duration: TimeSpan.Zero),
                CoverImageBytes: null);
        }

        if (string.Equals(preferredSource, "Kugou", StringComparison.OrdinalIgnoreCase))
        {
            PublishFallbackDiagnostics(
                sourceAppUserModelId: string.Empty,
                resolvedSource: "Kugou",
                title: "Unknown Title",
                artist: "Unknown Artist");
            return new PlaybackSnapshot(
                IsPlaying: false,
                Position: TimeSpan.Zero,
                Track: new TrackInfo(
                    Id: "Kugou|ProcessFallback",
                    Title: "Unknown Title",
                    Artist: "Unknown Artist",
                    Album: string.Empty,
                    SourceApp: "Kugou",
                    Duration: TimeSpan.Zero),
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
                .Where(x =>
                    !IsBlockedSource(x.Session.SourceAppUserModelId) &&
                    IsSupportedSource(x.Source) &&
                    IsSessionPlaying(x.Session))
                .OrderBy(x => GetRecognitionPriority(x.Source))
                .FirstOrDefault();
            if (supportedPlaying is not null)
            {
                return supportedPlaying.Session;
            }

            foreach (var candidate in sessions)
            {
                if (!CanUseGenericSession(candidate.SourceAppUserModelId))
                {
                    continue;
                }

                var playback = candidate.GetPlaybackInfo();
                if (playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    return candidate;
                }
            }

            var supportedAny = sessions
                .Select(candidate => new
                {
                    Session = candidate,
                    Source = NormalizeSource(candidate.SourceAppUserModelId)
                })
                .Where(x =>
                    !IsBlockedSource(x.Session.SourceAppUserModelId) &&
                    IsSupportedSource(x.Source))
                .OrderBy(x => GetRecognitionPriority(x.Source))
                .FirstOrDefault();
            if (supportedAny is not null)
            {
                return supportedAny.Session;
            }

            foreach (var candidate in sessions)
            {
                if (CanUseGenericSession(candidate.SourceAppUserModelId))
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

        if (sourceAppUserModelId.Contains("qqmusic", StringComparison.OrdinalIgnoreCase))
        {
            return "QQMusic";
        }

        if (sourceAppUserModelId.Contains("kugou", StringComparison.OrdinalIgnoreCase))
        {
            return "Kugou";
        }

        return sourceAppUserModelId;
    }

    private bool IsSupportedSource(string sourceApp)
    {
        return _enabledSources.Contains(sourceApp);
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
        if (!string.IsNullOrWhiteSpace(sourceApp) &&
            !IsBlockedSource(sourceApp) &&
            !IsDisabledKnownSource(sourceApp))
        {
            return sourceApp;
        }

        var detected = DetectRunningSource();
        return detected ?? sourceApp;
    }

    private bool CanUseGenericSession(string sourceAppUserModelId)
    {
        return !IsBlockedSource(sourceAppUserModelId) &&
               !IsDisabledKnownSource(sourceAppUserModelId);
    }

    private bool IsDisabledKnownSource(string sourceAppUserModelId)
    {
        var normalized = NormalizeSource(sourceAppUserModelId);
        return IsKnownSource(normalized) && !_enabledSources.Contains(normalized);
    }

    private static bool IsKnownSource(string sourceApp)
    {
        return DefaultRecognitionOrder.Contains(sourceApp, StringComparer.OrdinalIgnoreCase);
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

            if (source.Equals("Kugou", StringComparison.OrdinalIgnoreCase) &&
                IsAnyProcessRunning("kugou", "kugoumusic", "kgmusic"))
            {
                return "Kugou";
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

    private static List<string> NormalizeRecognitionOrder(
        IReadOnlyList<string>? order,
        IReadOnlySet<string> enabledSources)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (order is not null)
        {
            foreach (var item in order)
            {
                var normalized = NormalizeRecognitionSource(item);
                if (!string.IsNullOrWhiteSpace(normalized) &&
                    enabledSources.Contains(normalized) &&
                    seen.Add(normalized))
                {
                    result.Add(normalized);
                }
            }
        }

        foreach (var source in DefaultRecognitionOrder)
        {
            if (enabledSources.Contains(source) && seen.Add(source))
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

        if (source.Contains("qqmusic", StringComparison.OrdinalIgnoreCase))
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

        if (source.Contains("kugou", StringComparison.OrdinalIgnoreCase))
        {
            return "Kugou";
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

    private static async Task<CoverReadResult> ReadCoverBytesAsync(
        IRandomAccessStreamReference? thumbnail,
        CancellationToken cancellationToken)
    {
        if (thumbnail is null)
        {
            return new CoverReadResult(null, string.Empty, "Thumbnail reference is null", null, 0, 0);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var stream = await thumbnail.OpenReadAsync().AsTask(cancellationToken);
            var contentType = stream.ContentType ?? string.Empty;
            if (stream.Size == 0)
            {
                return new CoverReadResult(null, contentType, "Thumbnail stream is empty", null, 0, stopwatch.ElapsedMilliseconds);
            }

            if (stream.Size > uint.MaxValue)
            {
                return new CoverReadResult(null, contentType, $"Thumbnail stream is too large ({stream.Size} bytes)", null, 0, stopwatch.ElapsedMilliseconds);
            }

            using var input = stream.GetInputStreamAt(0);
            using var dataReader = new DataReader(input);
            var size = (uint)stream.Size;
            var loadedSize = await dataReader.LoadAsync(size).AsTask(cancellationToken);
            if (loadedSize != size)
            {
                return new CoverReadResult(
                    null,
                    contentType,
                    $"Thumbnail stream was only partially loaded ({loadedSize}/{size} bytes)",
                    null,
                    loadedSize,
                    stopwatch.ElapsedMilliseconds);
            }

            var bytes = new byte[size];
            dataReader.ReadBytes(bytes);
            return new CoverReadResult(bytes, contentType, string.Empty, null, loadedSize, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            return new CoverReadResult(null, string.Empty, ex.Message, ex, 0, stopwatch.ElapsedMilliseconds);
        }
    }

    private (byte[]? Bytes, bool IsLoading) GetCoverBytes(
        string sourceApp,
        string title,
        string artist,
        IRandomAccessStreamReference? thumbnail,
        DateTimeOffset nowUtc)
    {
        var metadataKey = $"{sourceApp}|{title}|{artist}";
        byte[]? cachedCover;
        var isLoading = false;
        var thumbnailStateChanged = false;
        var thumbnailState = thumbnail is null ? "Missing" : "Provided";

        lock (_coverLock)
        {
            var trackChanged = !string.Equals(metadataKey, _lastCoverMetadataKey, StringComparison.Ordinal);
            if (trackChanged)
            {
                _lastCoverMetadataKey = metadataKey;
                _lastCoverImageBytes = null;
                _nextMissingCoverRetryUtc = nowUtc;
            }

            var thumbnailDiagnosticsKey = $"{metadataKey}|{thumbnailState}";
            if (!string.Equals(thumbnailDiagnosticsKey, _lastCoverThumbnailDiagnosticsKey, StringComparison.Ordinal))
            {
                _lastCoverThumbnailDiagnosticsKey = thumbnailDiagnosticsKey;
                thumbnailStateChanged = true;
            }

            cachedCover = _lastCoverImageBytes;
            var shouldRetryMissingCover = cachedCover is null && nowUtc >= _nextMissingCoverRetryUtc;
            var isReadingCurrentCover =
                _coverReadTask is { IsCompleted: false } &&
                string.Equals(_coverReadMetadataKey, metadataKey, StringComparison.Ordinal);

            if (thumbnail is not null && shouldRetryMissingCover && !isReadingCurrentCover)
            {
                var readSequence = Interlocked.Increment(ref _coverReadSequence);
                _coverReadMetadataKey = metadataKey;
                _coverReadTask = ReadCoverBytesInBackgroundAsync(metadataKey, thumbnail, readSequence);
                _nextMissingCoverRetryUtc = DateTimeOffset.MaxValue;
                isLoading = true;
            }
            else
            {
                isLoading = cachedCover is null && isReadingCurrentCover;
            }
        }

        if (thumbnailStateChanged)
        {
            Log.Diagnostic(
                "COVER-SMTC",
                $"Thumbnail{thumbnailState} Track='{ToLogValue(metadataKey)}'");
        }

        return (cachedCover, isLoading);
    }

    private async Task ReadCoverBytesInBackgroundAsync(
        string metadataKey,
        IRandomAccessStreamReference thumbnail,
        int readSequence)
    {
        Log.Diagnostic(
            "COVER-SMTC",
            $"ReadStarted Sequence={readSequence} Track='{ToLogValue(metadataKey)}'");

        var result = await ReadCoverBytesAsync(thumbnail, CancellationToken.None);
        var nowUtc = DateTimeOffset.UtcNow;
        var isStale = false;

        lock (_coverLock)
        {
            if (!string.Equals(metadataKey, _lastCoverMetadataKey, StringComparison.Ordinal))
            {
                isStale = true;
            }
            else
            {
                _lastCoverImageBytes = result.Bytes;
                _nextMissingCoverRetryUtc = result.Bytes is null
                    ? nowUtc + MissingCoverRetryInterval
                    : DateTimeOffset.MaxValue;
            }
        }

        if (isStale)
        {
            Log.Diagnostic(
                "COVER-SMTC",
                $"ReadDiscarded Sequence={readSequence} Reason='Track changed' Track='{ToLogValue(metadataKey)}' " +
                $"ElapsedMs={result.ElapsedMilliseconds}");
            return;
        }

        if (result.Bytes is { Length: > 0 } bytes)
        {
            Log.Diagnostic(
                "COVER-SMTC",
                $"ReadSucceeded Sequence={readSequence} Track='{ToLogValue(metadataKey)}' Bytes={bytes.Length} " +
                $"ContentType='{ToLogValue(result.ContentType)}' Signature='{GetByteSignature(bytes)}' " +
                $"ElapsedMs={result.ElapsedMilliseconds}");
            return;
        }

        var exceptionText = result.Exception is null
            ? string.Empty
            : $" Exception='{result.Exception.GetType().Name}' HResult=0x{result.Exception.HResult:X8}";
        Log.Diagnostic(
            "COVER-SMTC",
            $"ReadFailed Sequence={readSequence} Track='{ToLogValue(metadataKey)}' " +
            $"ContentType='{ToLogValue(result.ContentType)}' LoadedBytes={result.LoadedBytes} " +
            $"Reason='{ToLogValue(result.FailureReason)}'{exceptionText} ElapsedMs={result.ElapsedMilliseconds} " +
            $"RetryAfterMs={(int)MissingCoverRetryInterval.TotalMilliseconds}");
    }

    private static string GetByteSignature(byte[] bytes)
    {
        return Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 12)));
    }

    private static string ToLogValue(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return normalized.Length <= 240 ? normalized : normalized[..240];
    }

    private sealed record CoverReadResult(
        byte[]? Bytes,
        string ContentType,
        string FailureReason,
        Exception? Exception,
        uint LoadedBytes,
        long ElapsedMilliseconds);

}
