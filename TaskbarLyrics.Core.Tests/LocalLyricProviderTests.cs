using System.Text;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LocalLyricProviderTests
{
    [Fact]
    public async Task GetLyricsAsyncParsesEmbeddedEnhancedLrcWordsUsingAbsoluteBoundaries()
    {
        var directory = CreateEmbeddedLyricsFlacDirectory(
            "Artist - Enhanced Song.flac",
            "[00:09.739]こ<00:09.955>の<00:10.172>夢<00:10.725>");

        try
        {
            using var provider = new LocalLyricProvider([directory]);
            var document = await GetLyricsAsync(provider, "Enhanced Song");

            var line = Assert.Single(document.Lines);
            Assert.Equal(TimeSpan.FromMilliseconds(9739), line.Timestamp);
            Assert.Collection(line.Syllables!,
                syllable => AssertSyllable(syllable, 0, 216, "こ"),
                syllable => AssertSyllable(syllable, 216, 217, "の"),
                syllable => AssertSyllable(syllable, 433, 553, "夢"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetLyricsAsyncUsesEmptyEnhancedBoundariesToAdvanceTheWordCursor()
    {
        var directory = CreateLyricsDirectory(
            "Artist - Cursor Song.lrc",
            "[00:00.000]<00:00.815><00:00.816>Tayori<00:01.000>");

        try
        {
            using var provider = new LocalLyricProvider([directory]);
            var document = await GetLyricsAsync(provider, "Cursor Song");

            var line = Assert.Single(document.Lines);
            var syllable = Assert.Single(line.Syllables!);
            AssertSyllable(syllable, 816, 184, "Tayori");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetLyricsAsyncFallsBackWhenInvalidTagIsMixedWithEnhancedBoundary()
    {
        var directory = CreateLyricsDirectory(
            "Artist - Mixed Invalid Song.lrc",
            "[00:01.000]a<00:bad>b<00:02.000>\n[00:05.000]next");

        try
        {
            using var provider = new LocalLyricProvider([directory]);
            var document = await GetLyricsAsync(provider, "Mixed Invalid Song");

            var line = document.Lines[0];
            Assert.Equal("ab", line.Text);
            Assert.Collection(line.Syllables!,
                syllable => AssertSyllable(syllable, 0, 2000, "a"),
                syllable => AssertSyllable(syllable, 2000, 2000, "b"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetLyricsAsyncPreservesOrdinaryLrcOffsetAndSameTimestampTranslation()
    {
        var directory = CreateLyricsDirectory(
            "Artist - Ordinary Song.lrc",
            "[offset:+250]\n[00:01.00]Original line\n[00:01.00]翻译行\n[00:03.00]Later");

        try
        {
            using var provider = new LocalLyricProvider([directory]);
            var document = await GetLyricsAsync(provider, "Ordinary Song");

            Assert.Collection(document.Lines,
                line =>
                {
                    Assert.Equal(TimeSpan.FromMilliseconds(1250), line.Timestamp);
                    Assert.Equal("Original line", line.Text);
                    Assert.Equal("翻译行", line.Translation);
                    Assert.NotEmpty(line.Syllables!);
                },
                line => Assert.Equal("Later", line.Text));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetLyricsAsyncFallsBackSafelyForInvalidEnhancedBoundaries()
    {
        var directory = CreateLyricsDirectory(
            "Artist - Invalid Song.lrc",
            "[00:01.000]a<00:01.200>b<00:01.100>c\n" +
            "[00:03.000]repeat<00:03.200>d<00:03.200>e<00:03.500>\n" +
            "[00:05.000]broken<00:05.200>tail\n" +
            "[00:15.000]next");

        try
        {
            using var provider = new LocalLyricProvider([directory]);
            var document = await GetLyricsAsync(provider, "Invalid Song");

            var invalidLines = document.Lines.Take(3).ToArray();
            Assert.Equal(["abc", "repeatde", "brokentail"], invalidLines.Select(line => line.Text));
            Assert.All(invalidLines, line =>
            {
                Assert.NotEmpty(line.Syllables!);
                Assert.All(line.Syllables!, syllable =>
                {
                    Assert.True(syllable.RelativeOffset >= TimeSpan.Zero);
                    Assert.True(syllable.Duration > TimeSpan.Zero);
                });
            });
            Assert.True(invalidLines[0].Syllables![0].Duration > TimeSpan.FromMilliseconds(500));
            Assert.True(invalidLines[2].Syllables![0].Duration > TimeSpan.FromMilliseconds(500));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateLyricsDirectory(string fileName, string content)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"taskbar-lyrics-local-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), content);
        return directory;
    }

    private static string CreateEmbeddedLyricsFlacDirectory(string fileName, string lyrics)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"taskbar-lyrics-local-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var comment = Encoding.UTF8.GetBytes($"LYRICS={lyrics}");
        var bytes = new byte[4 + sizeof(int) + comment.Length];
        Encoding.ASCII.GetBytes("fLaC").CopyTo(bytes, 0);
        BitConverter.GetBytes(comment.Length).CopyTo(bytes, 4);
        comment.CopyTo(bytes, 4 + sizeof(int));
        File.WriteAllBytes(Path.Combine(directory, fileName), bytes);
        return directory;
    }

    private static async Task<LyricDocument> GetLyricsAsync(LocalLyricProvider provider, string title)
    {
        var track = new TrackInfo("local-test", title, "Artist", string.Empty, "Test", TimeSpan.Zero);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            var document = await provider.GetLyricsAsync(track);
            if (document is not null)
            {
                return document;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("The local lyric index did not discover the test lyric file.");
    }

    private static void AssertSyllable(LyricSyllable syllable, int relativeOffsetMs, int durationMs, string text)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(relativeOffsetMs), syllable.RelativeOffset);
        Assert.Equal(TimeSpan.FromMilliseconds(durationMs), syllable.Duration);
        Assert.Equal(text, syllable.Text);
    }
}
