using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;

namespace TaskbarLyrics.Light.App;

/// <summary>
/// 在首次需要歌词同步时才创建 <see cref="LyricSyncService"/> 并初始化基础设施。
/// </summary>
internal sealed class DeferredLyricSyncService : IDisposable
{
    private readonly object _gate = new();
    private LyricSyncService? _inner;
    private AppSettings _settings = new();
    private bool _isDisposed;

    public string? CurrentLyricSourceApp => _inner?.CurrentLyricSourceApp;

    public void Reset()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _inner?.Dispose();
            _inner = null;
        }
    }

    public void UpdateSettings(AppSettings settings)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var nextSettings = settings.Clone();
            var providersChanged = HasProviderSettingsChanged(_settings, nextSettings);
            _settings = nextSettings;

            if (providersChanged)
            {
                _inner?.Dispose();
                _inner = null;
            }
        }
    }

    public Task<LyricDisplayFrame> GetDisplayFrameAsync(PlaybackSnapshot snapshot)
    {
        if (snapshot.Track is null)
        {
            return Task.FromResult(new LyricDisplayFrame("", "", "", 0, -1));
        }

        return EnsureInner().GetDisplayFrameAsync(snapshot);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _inner?.Dispose();
            _inner = null;
        }
    }

    private LyricSyncService EnsureInner()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_inner is not null)
            {
                return _inner;
            }

            LyricsInfrastructure.EnsureInitialized();
            _inner = LyricProviderComposer.CreateSyncService(
                _settings,
                PublishResolveDiagnostics,
                _ => _settings.ShowLyricTranslation);
            return _inner;
        }
    }

    private void PublishResolveDiagnostics(
        TrackInfo track,
        IReadOnlyList<LyricResolveResult> results,
        TimeSpan elapsed)
    {
        var best = results
            .Where(result => result.Document is { Lines.Count: > 0 })
            .OrderByDescending(result => result.Document!.BestScore)
            .ThenBy(result => result.SourceApp == "QQMusic" || result.SourceApp == "Netease" ? 0 : 1)
            .FirstOrDefault();

        var candidates = results.Count == 0
            ? "None"
            : string.Join(", ", results.Select(result =>
                result.Document is null
                    ? $"{result.SourceApp}:none"
                    : $"{result.SourceApp}:score={result.Document.BestScore},lines={result.Document.Lines.Count}"));

        LyricResolveDiagnosticsState.Update(new LyricResolveDiagnosticsSnapshot(
            DateTimeOffset.UtcNow,
            track.Title,
            track.Artist,
            track.SourceApp,
            best?.SourceApp ?? string.Empty,
            best?.Document?.BestScore ?? 0,
            best?.Document?.Lines.Count ?? 0,
            best?.Document?.IsPureMusic ?? false,
            $"{candidates}; elapsed={elapsed.TotalMilliseconds:F0}ms",
            LyricResolveDiagnosticsState.Current.AppliedOffsetMs,
            LyricResolveDiagnosticsState.Current.PlaybackPosition,
            LyricResolveDiagnosticsState.Current.CurrentLineIndex,
            LyricResolveDiagnosticsState.Current.LineProgress));
    }

    private static bool HasProviderSettingsChanged(AppSettings previous, AppSettings current) =>
        previous.EnableNetease != current.EnableNetease ||
        previous.EnableQQMusic != current.EnableQQMusic ||
        previous.EnableKugou != current.EnableKugou ||
        previous.EnableSpotify != current.EnableSpotify ||
        previous.EnableLocalLyrics != current.EnableLocalLyrics ||
        previous.LocalLyricsSearchMode != current.LocalLyricsSearchMode ||
        !previous.LocalMusicFolders.SequenceEqual(current.LocalMusicFolders) ||
        !previous.SourceRecognitionOrder.SequenceEqual(current.SourceRecognitionOrder);

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(DeferredLyricSyncService));
        }
    }
}
