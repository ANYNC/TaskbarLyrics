using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricCacheStoreTests
{
    [Fact]
    public void JsonStore_RoundTripsAcrossInstancesAndClearsPersistedState()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"taskbar-lyrics-cache-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "cache.json");
        var payload = new CachePayload { Value = "lyrics" };

        try
        {
            var first = new JsonLyricCacheStore<CachePayload>(filePath);
            first.Set("track", payload);

            Assert.True(first.TryGet("track", out var fromMemory, out var memoryAcquisition));
            Assert.Equal(LyricAcquisitionKind.MemoryCache, memoryAcquisition);
            Assert.Equal("lyrics", fromMemory!.Value);

            var second = new JsonLyricCacheStore<CachePayload>(filePath);
            Assert.True(second.TryGet("track", out var fromDisk, out var diskAcquisition));
            Assert.Equal(LyricAcquisitionKind.DiskCache, diskAcquisition);
            Assert.Equal("lyrics", fromDisk!.Value);

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

    public sealed class CachePayload
    {
        public string Value { get; set; } = string.Empty;
    }
}
