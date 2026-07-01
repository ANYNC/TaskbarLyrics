using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Light.App;

/// <summary>
/// 按需实例化歌词源，首次检索时才加载对应 Provider 及其依赖。
/// </summary>
internal sealed class LazyLyricProvider : ILyricProvider
{
    private readonly Func<ILyricProvider> _factory;
    private readonly object _gate = new();
    private ILyricProvider? _inner;

    public LazyLyricProvider(string sourceApp, Func<ILyricProvider> factory)
    {
        SourceApp = sourceApp;
        _factory = factory;
    }

    public string SourceApp { get; }

    public Task<LyricDocument?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default) =>
        EnsureInner().GetLyricsAsync(track, cancellationToken);

    private ILyricProvider EnsureInner()
    {
        if (_inner is not null)
        {
            return _inner;
        }

        lock (_gate)
        {
            return _inner ??= _factory();
        }
    }
}
