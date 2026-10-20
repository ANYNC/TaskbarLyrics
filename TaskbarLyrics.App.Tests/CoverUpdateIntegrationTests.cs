using System.Reflection;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MainWindowReplacesCachedArtworkAfterARecheckOrReusedSongId(bool reusedSongId)
    {
        RunSta(() =>
        {
            using var composition = new AppCompositionRoot();
            using var offsets = new TrackLyricOffsetStore();
            var main = new MainWindow(offsets, composition, () => { }, () => { });
            var now = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
            using var cache = new CoverRefreshCache(() => now);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var update = typeof(MainWindow).GetMethod("UpdateCover", flags)!.CreateDelegate<Action<PlaybackSnapshot>>(main);
            var uriField = typeof(MainWindow).GetField("_currentCoverDataUri", flags)!;
            var track = new TrackInfo("id", "Song", "Artist", "Album", "Test", TimeSpan.FromMinutes(3), "same-id");
            byte[] oldCover = [0xFF, 0xD8, 0xFF, 1];
            byte[] newCover = [0xFF, 0xD8, 0xFF, 2];
            try
            {
                cache.GetCover(CoverIdentity.FromTrack(track), track.Title, _ => Task.FromResult<byte[]?>(oldCover));
                var original = cache.GetCover(CoverIdentity.FromTrack(track), track.Title, null);
                update(new PlaybackSnapshot(true, TimeSpan.Zero, track, original.Bytes));
                Assert.Equal($"data:image/jpeg;base64,{Convert.ToBase64String(oldCover)}", uriField.GetValue(main));

                now = now.AddMilliseconds(500);
                if (reusedSongId) track = track with { Title = "Next song" };
                cache.GetCover(CoverIdentity.FromTrack(track), track.Title, _ => Task.FromResult<byte[]?>(newCover));
                var corrected = cache.GetCover(CoverIdentity.FromTrack(track), track.Title, null);
                update(new PlaybackSnapshot(true, TimeSpan.Zero, track, corrected.Bytes));
                var correctedUri = uriField.GetValue(main);
                Assert.Equal($"data:image/jpeg;base64,{Convert.ToBase64String(newCover)}", correctedUri);

                update(new PlaybackSnapshot(true, TimeSpan.Zero, track, newCover.ToArray()));
                Assert.Same(correctedUri, uriField.GetValue(main));
            }
            finally { main.Close(); }
        });
    }
}
