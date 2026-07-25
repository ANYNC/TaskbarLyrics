using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricProviderBaseTests
{
    [Fact]
    public void ParseLrcAppliesOffsetAndExpandsMultipleTimestamps()
    {
        var provider = new ParserProvider();

        var lines = provider.Parse("[offset:+500]\n[00:01.20][00:02.00]Hello world");

        Assert.Collection(
            lines,
            first =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(1700), first.Timestamp);
                Assert.Equal("Hello world", first.Text);
            },
            second =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(2500), second.Timestamp);
                Assert.Equal("Hello world", second.Text);
            });
    }

    [Fact]
    public void ParseLrcClampsNegativeTimestampAfterOffset()
    {
        var provider = new ParserProvider();

        var line = Assert.Single(provider.Parse("[offset:-1500]\n[00:01.00]Opening"));

        Assert.Equal(TimeSpan.Zero, line.Timestamp);
        Assert.Equal("Opening", line.Text);
    }

    [Fact]
    public void BuildCacheKeyUsesSongIdBeforeMutableMetadata()
    {
        var provider = new ParserProvider();
        var original = CreateTrack(songId: "12345");
        var refreshedMetadata = original with
        {
            Title = "Updated title",
            Artist = "Updated artist",
            Album = "Updated album",
            Duration = TimeSpan.FromSeconds(240)
        };

        Assert.Equal(provider.GetCacheKey(original), provider.GetCacheKey(refreshedMetadata));
    }

    [Fact]
    public void BuildCacheKeyWithoutSongIdIncludesAlbum()
    {
        var provider = new ParserProvider();
        var original = CreateTrack(songId: null);
        var remastered = original with { Album = "Remastered" };

        Assert.NotEqual(provider.GetCacheKey(original), provider.GetCacheKey(remastered));
    }

    [Fact]
    public async Task GetLyricsWithDiagnosticsAsyncDiscardsInvalidCachedDocument()
    {
        var cache = new InMemoryCacheStore();
        var track = CreateTrack(songId: "12345");
        var resolved = new LyricDocument(
            new[] { new LyricLine(TimeSpan.Zero, "Valid lyric") },
            bestScore: 100);
        var provider = new ResolvingProvider(cache, resolved);
        cache.Store(provider.GetCacheKey(track), new LyricDocument(Array.Empty<LyricLine>(), bestScore: 100));

        var result = await provider.GetLyricsWithDiagnosticsAsync(track);

        Assert.Equal(1, provider.ResolveCount);
        Assert.NotNull(result.Document);
        Assert.Single(result.Document.Lines);
        Assert.Equal(LyricAcquisitionKind.Remote, result.Acquisition);
    }

    private static TrackInfo CreateTrack(string? songId)
    {
        return new TrackInfo(
            Id: "track",
            Title: "Song",
            Artist: "Artist",
            Album: "Album",
            SourceApp: "Netease",
            Duration: TimeSpan.FromSeconds(180),
            SongId: songId);
    }

    private class ParserProvider : LyricProviderBase
    {
        private static readonly HttpClient HttpClient = new();

        public ParserProvider(ILyricCacheStore<LyricDocument>? cacheStore = null)
            : base(HttpClient, cacheStore)
        {
        }

        public override string SourceApp => "Test";

        public List<LyricLine> Parse(string content) => ParseLrc(content);

        public string GetCacheKey(TrackInfo track) => BuildCacheKey(track);

        protected override Task<LyricDocument?> ResolveRemoteAsync(
            TrackInfo track,
            CancellationToken cancellationToken) => Task.FromResult<LyricDocument?>(null);
    }

    private sealed class ResolvingProvider : ParserProvider
    {
        private readonly LyricDocument _result;

        public ResolvingProvider(ILyricCacheStore<LyricDocument> cacheStore, LyricDocument result)
            : base(cacheStore)
        {
            _result = result;
        }

        public int ResolveCount { get; private set; }

        protected override Task<LyricDocument?> ResolveRemoteAsync(
            TrackInfo track,
            CancellationToken cancellationToken)
        {
            ResolveCount++;
            return Task.FromResult<LyricDocument?>(_result);
        }
    }

    private sealed class InMemoryCacheStore : ILyricCacheStore<LyricDocument>
    {
        private readonly Dictionary<string, LyricDocument> _entries = new(StringComparer.Ordinal);

        public bool TryGet(string key, out LyricDocument? payload, out LyricAcquisitionKind acquisition)
        {
            if (_entries.TryGetValue(key, out var cached))
            {
                payload = cached;
                acquisition = LyricAcquisitionKind.MemoryCache;
                return true;
            }

            payload = null;
            acquisition = LyricAcquisitionKind.Unknown;
            return false;
        }

        public void Store(string key, LyricDocument payload) => _entries[key] = payload;

        public void Remove(string key) => _entries.Remove(key);

        public void Clear() => _entries.Clear();
    }
}
