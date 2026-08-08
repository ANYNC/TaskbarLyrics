using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricCacheStoreTests
{
    [Fact]
    public void JsonStoreRoundTripsAcrossInstancesAndClearsPersistedState()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"taskbar-lyrics-cache-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "cache.json");
        var payload = new CachePayload { Value = "lyrics" };

        try
        {
            var first = new JsonLyricCacheStore<CachePayload>(filePath);
            first.Store("track", payload);

            Assert.True(first.TryGet("track", out var fromMemory, out var memoryAcquisition));
            Assert.Equal(LyricAcquisitionKind.MemoryCache, memoryAcquisition);
            Assert.Equal("lyrics", fromMemory!.Value);

            var second = new JsonLyricCacheStore<CachePayload>(filePath);
            Assert.True(second.TryGet("track", out var fromDisk, out var diskAcquisition));
            Assert.Equal(LyricAcquisitionKind.DiskCache, diskAcquisition);
            Assert.Equal("lyrics", fromDisk!.Value);

            second.Remove("track");
            var third = new JsonLyricCacheStore<CachePayload>(filePath);
            Assert.False(third.TryGet("track", out _, out _));

            second.Clear();
            Assert.False(File.Exists(filePath));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonStoreRoundTripsLyricDocumentAcrossInstances()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"taskbar-lyrics-document-cache-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "cache.json");
        var document = new LyricDocument(
            [
                new LyricLine(TimeSpan.FromSeconds(2), "Second line"),
                new LyricLine(TimeSpan.FromSeconds(1), "First line", "Translation")
            ],
            bestScore: 103,
            isPureMusic: false);

        try
        {
            var first = new JsonLyricCacheStore<LyricDocument>(filePath);
            first.Store("track", document);

            var second = new JsonLyricCacheStore<LyricDocument>(filePath);

            Assert.True(second.TryGet("track", out var restored, out var acquisition));
            Assert.Equal(LyricAcquisitionKind.DiskCache, acquisition);
            Assert.False(restored!.IsPureMusic);
            Assert.Collection(
                restored.Lines,
                line =>
                {
                    Assert.Equal(TimeSpan.FromSeconds(1), line.Timestamp);
                    Assert.Equal("First line", line.Text);
                    Assert.Equal("Translation", line.Translation);
                },
                line =>
                {
                    Assert.Equal(TimeSpan.FromSeconds(2), line.Timestamp);
                    Assert.Equal("Second line", line.Text);
                });
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonStoreDiscardsCacheWhenPayloadContractCannotBeDeserialized()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"taskbar-lyrics-invalid-cache-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "cache.json");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(filePath, "{\"track\":{\"Value\":\"lyrics\"}}");
            var store = new JsonLyricCacheStore<UnbindableCachePayload>(filePath);

            Assert.False(store.TryGet("track", out _, out _));
            Assert.False(File.Exists(filePath));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    public sealed class CachePayload
    {
        public string Value { get; set; } = string.Empty;
    }

    public sealed class UnbindableCachePayload
    {
        public UnbindableCachePayload(int incompatibleValue)
        {
            Value = string.Empty;
        }

        public string Value { get; }
    }
}
