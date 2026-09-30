using Microsoft.Data.Sqlite;
using TaskbarLyrics.Core.Database;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class TrackLyricOffsetStoreTests
{
    [Fact]
    public async Task FailedWritesKeepTheLastPersistedOffsetInMemory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"taskbar-lyrics-offsets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "offsets.db");
        var currentPath = databasePath;
        var track = new TrackInfo("track", "Song", "Artist", string.Empty, "Player", TimeSpan.FromMinutes(3));

        try
        {
            using var store = new TrackLyricOffsetStore(() => new UserDataDbContext(currentPath));
            Assert.True((await store.SetOffsetAsync(track, "QQMusic", 100)).IsSaved);
            Assert.True(TrackLyricOffsetStore.TryCreateIdentity(track, "QQMusic", out var identity));
            var key = new TrackLyricOffsetRecordKey(
                identity.NormalizedTitle,
                identity.NormalizedArtist,
                identity.NormalizedLyricSource,
                identity.DurationBucketSeconds);

            currentPath = directory;
            Assert.False((await store.SetOffsetAsync(track, "QQMusic", 200)).IsSaved);
            Assert.Equal(100, store.GetOffsetMilliseconds(track, "QQMusic"));
            Assert.False((await store.DeleteAsync(key)).IsSaved);
            Assert.Equal(100, store.GetOffsetMilliseconds(track, "QQMusic"));
            Assert.False((await store.ClearAsync()).IsSaved);
            Assert.Equal(100, store.GetOffsetMilliseconds(track, "QQMusic"));

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                store.SetOffsetAsync(track, "QQMusic", 300, canceled.Token));
            Assert.Equal(100, store.GetOffsetMilliseconds(track, "QQMusic"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
