using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LocalMediaIndexTests
{
    [Fact]
    public async Task AcquireForTheSameFoldersSharesOneCancellableFileIndex()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"taskbar-lyrics-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "Artist - Song.mp3"), []);
        File.WriteAllText(Path.Combine(directory, "Artist - Song.lrc"), "[00:00.00]Song");
        File.WriteAllText(Path.Combine(directory, "ignored.txt"), "ignored");

        try
        {
            using var first = LocalMediaIndexRegistry.Acquire([directory]);
            using var second = LocalMediaIndexRegistry.Acquire([directory]);

            var snapshot = await WaitForFilesAsync(first, expectedCount: 2);

            Assert.Equal(2, snapshot.Files.Count);
            Assert.Contains(snapshot.Files, entry => entry.Kind == LocalMediaFileKind.Audio);
            Assert.Contains(snapshot.Files, entry => entry.Kind == LocalMediaFileKind.Lyric);
            Assert.Equal(snapshot.Version, second.GetSnapshot().Version);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<LocalMediaIndexSnapshot> WaitForFilesAsync(
        ILocalMediaIndex index,
        int expectedCount)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        while (!timeout.IsCancellationRequested)
        {
            var snapshot = index.GetSnapshot();
            if (snapshot.Files.Count == expectedCount)
            {
                return snapshot;
            }

            await Task.Delay(10, timeout.Token);
        }

        throw new TimeoutException("Local media index did not finish in time.");
    }
}
