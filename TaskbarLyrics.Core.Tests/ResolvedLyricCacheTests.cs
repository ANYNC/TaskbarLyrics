using System.Text.Json;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class ResolvedLyricCacheTests
{
    [Fact]
    public void CacheKeyIgnoresPlayerAlbumDurationAndIdentifiers()
    {
        using var fixture = CacheFile.Create();
        var storedTrack = CreateTrack(
            "  Ｃａｆé\t Song ",
            "  THE  ARTIST ",
            "Original Album",
            "Player A",
            "track-a",
            "song-a",
            TimeSpan.FromMinutes(3));
        var equivalentTrack = CreateTrack(
            "café song",
            "the artist",
            "Different Album",
            "Player B",
            "track-b",
            "song-b",
            TimeSpan.FromSeconds(2));

        using var cache = new JsonResolvedLyricCache(fixture.Path);
        Assert.True(cache.Store(storedTrack, CreateResolved("remembered")));
        Assert.True(cache.TryGet(equivalentTrack, out var resolved));
        Assert.Equal(LyricAcquisitionKind.MemoryCache, resolved!.Acquisition);
        Assert.Equal("remembered", resolved.Content.Lines[0].Text);
    }

    [Fact]
    public void DifferentTitleOrArtistMisses()
    {
        using var fixture = CacheFile.Create();
        using var cache = new JsonResolvedLyricCache(fixture.Path);
        var track = CreateTrack("Song", "Artist", "Album", "Player", "id", null, TimeSpan.FromMinutes(3));

        Assert.True(cache.Store(track, CreateResolved("lyrics")));
        Assert.False(cache.TryGet(track with { Title = "Other Song" }, out _));
        Assert.False(cache.TryGet(track with { Artist = "Other Artist" }, out _));
        Assert.False(cache.TryGet(track with { Title = " " }, out _));
    }

    [Fact]
    public void LaterStoreReplacesEarlierValueForSameNormalizedKey()
    {
        using var fixture = CacheFile.Create();
        var track = CreateTrack("Song", "Artist", "Album", "Player", "id", null, TimeSpan.FromMinutes(3));
        using var cache = new JsonResolvedLyricCache(fixture.Path);

        Assert.True(cache.Store(track, CreateResolved("first")));
        Assert.True(cache.Store(track with { Album = "Deluxe", Duration = TimeSpan.FromSeconds(1) }, CreateResolved("second")));
        Assert.True(cache.TryGet(track, out var resolved));
        Assert.Equal("second", resolved!.Content.Lines[0].Text);

        using var document = JsonDocument.Parse(File.ReadAllText(fixture.Path));
        Assert.Single(document.RootElement.GetProperty("entries").EnumerateObject());
        using var reloaded = new JsonResolvedLyricCache(fixture.Path);
        Assert.True(reloaded.TryGet(track, out var reloadedLyrics));
        Assert.Equal(LyricAcquisitionKind.DiskCache, reloadedLyrics!.Acquisition);
        Assert.Equal("second", reloadedLyrics.Content.Lines[0].Text);
    }

    [Fact]
    public void InvalidRecordIsRemovedAndUnknownVersionIsSafeMiss()
    {
        using var invalidFixture = CacheFile.Create();
        File.WriteAllText(
            invalidFixture.Path,
            "{\"version\":1,\"entries\":{\"bad\":{\"providerId\":\"QQMusic\",\"candidateId\":\"candidate\",\"diagnostics\":{},\"content\":null}}}");
        var track = CreateTrack("Song", "Artist", "Album", "Player", "id", null, TimeSpan.Zero);
        using (var cache = new JsonResolvedLyricCache(invalidFixture.Path))
        {
            Assert.False(cache.TryGet(track, out _));
        }

        using (var document = JsonDocument.Parse(File.ReadAllText(invalidFixture.Path)))
        {
            Assert.Empty(document.RootElement.GetProperty("entries").EnumerateObject());
        }

        using var unknownFixture = CacheFile.Create();
        const string unknownVersion = "{\"version\":99,\"entries\":{}}";
        File.WriteAllText(unknownFixture.Path, unknownVersion);
        using (var cache = new JsonResolvedLyricCache(unknownFixture.Path))
        {
            Assert.False(cache.TryGet(track, out _));
            Assert.False(cache.Store(track, CreateResolved("must not overwrite")));
        }

        Assert.Equal(unknownVersion, File.ReadAllText(unknownFixture.Path));
    }

    [Fact]
    public void ClearRemovesCurrentAndLegacyFiles()
    {
        using var fixture = CacheFile.Create();
        var legacyPath = System.IO.Path.Combine(fixture.DirectoryPath, JsonResolvedLyricCache.LegacyFileName);
        var track = CreateTrack("Song", "Artist", "Album", "Player", "id", null, TimeSpan.FromMinutes(3));
        using var cache = new JsonResolvedLyricCache(fixture.Path);
        Assert.True(cache.Store(track, CreateResolved("lyrics")));
        Assert.True(File.Exists(fixture.Path));
        File.WriteAllText(legacyPath, "legacy");
        cache.Clear();
        Assert.False(File.Exists(fixture.Path));
        Assert.False(File.Exists(legacyPath));
        Assert.False(cache.TryGet(track, out _));
    }

    [Fact]
    public void LegacyBindingsMigrateByTitleAndArtistWithLastRecordWinning()
    {
        using var fixture = CacheFile.Create();
        var legacyPath = System.IO.Path.Combine(fixture.DirectoryPath, JsonResolvedLyricCache.LegacyFileName);
        var track = CreateTrack("Song", "Artist", "Album", "Player", "id", null, TimeSpan.FromMinutes(3));
        var first = CreateResolved("first");
        var second = CreateResolved("last");
        var bindings = new[]
        {
            new
            {
                trackId = "first-track",
                title = track.Title,
                artist = track.Artist,
                album = "Album One",
                sourceApp = "Player A",
                duration = TimeSpan.FromMinutes(2),
                songId = "first-song",
                providerId = first.ProviderId.Value,
                candidateId = first.CandidateId,
                acquisition = first.Acquisition,
                diagnostics = first.Diagnostics,
                content = first.Content
            },
            new
            {
                trackId = "last-track",
                title = track.Title,
                artist = track.Artist,
                album = "Album Two",
                sourceApp = "Player B",
                duration = TimeSpan.FromMinutes(4),
                songId = "last-song",
                providerId = second.ProviderId.Value,
                candidateId = second.CandidateId,
                acquisition = second.Acquisition,
                diagnostics = second.Diagnostics,
                content = second.Content
            }
        };
        File.WriteAllText(
            legacyPath,
            JsonSerializer.Serialize(new { version = 1, bindings }));

        using var cache = new JsonResolvedLyricCache(fixture.Path);
        Assert.True(cache.TryGet(track with { Album = "Current Album", Duration = TimeSpan.FromSeconds(1) }, out var resolved));
        Assert.Equal("last", resolved!.Content.Lines[0].Text);
        Assert.Equal(LyricAcquisitionKind.DiskCache, resolved.Acquisition);
        Assert.True(File.Exists(fixture.Path));
        Assert.False(File.Exists(legacyPath));
    }

    private static TrackInfo CreateTrack(
        string title,
        string artist,
        string album,
        string sourceApp,
        string id,
        string? songId,
        TimeSpan duration) =>
        new(id, title, artist, album, sourceApp, duration, songId);

    private static ResolvedLyrics CreateResolved(string text) =>
        new(
            new ParsedLyrics(
                [new ParsedLyricLine(TimeSpan.Zero, null, text)],
                LyricTimingKind.LineTimed,
                LyricTimingProvenance.ProviderSupplied,
                LyricPayloadFormat.PlainText),
            KnownLyricProviders.QQMusic,
            "candidate-1",
            LyricAcquisitionKind.Remote,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["selectedBy"] = "test"
            });

    private sealed class CacheFile : IDisposable
    {
        private CacheFile(string directoryPath)
        {
            DirectoryPath = directoryPath;
            Path = System.IO.Path.Combine(directoryPath, JsonResolvedLyricCache.DefaultFileName);
        }

        public string DirectoryPath { get; }
        public string Path { get; }

        public static CacheFile Create()
        {
            var directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "TaskbarLyrics",
                "ResolvedLyricCacheTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return new CacheFile(directory);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
