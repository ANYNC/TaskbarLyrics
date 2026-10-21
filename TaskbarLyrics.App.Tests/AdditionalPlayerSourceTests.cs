using System.IO;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class AdditionalPlayerSourceTests
{
    [Theory]
    [InlineData("AppleMusic", "AppleMusic")]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", "AppleMusic")]
    [InlineData("SodaMusic", "SodaMusic")]
    [InlineData("汽水音乐", "SodaMusic")]
    [InlineData("com.soda.music", "SodaMusic")]
    [InlineData("foobar2000", "Foobar2000")]
    [InlineData("foobar2000.exe", "Foobar2000")]
    public void KnownSessionIdentitiesUseTheirOwnRecognitionToggle(string session, string canonical)
    {
        Assert.Equal(canonical, SmtcMusicSessionProvider.NormalizeSource(session));
        using var provider = new SmtcMusicSessionProvider();
        Assert.True(provider.CanUseSource(session));
        provider.SetRecognitionOrder(PlayerSourceIdentity.BuiltInOrder, [canonical]);
        Assert.True(provider.CanUseSource(session));
        provider.SetRecognitionOrder(PlayerSourceIdentity.BuiltInOrder, []);
        Assert.False(provider.CanUseSource(session));
    }

    [Theory]
    [InlineData("Unknown.AppleMusic.Player")]
    [InlineData("https://example.com/foobar2000.exe")]
    [InlineData("OtherSodaMusic")]
    public void SimilarNamesAreNotClaimedByNewBuiltInRecognizers(string session)
    {
        Assert.Null(PlayerSourceIdentity.NormalizeAdditional(session));
        Assert.Equal(session, SmtcMusicSessionProvider.NormalizeSource(session));
    }

    [Fact]
    public void MissingPlayersAreInsertedBeforeBrowserWithoutReorderingExistingPlayers()
    {
        var order = PlayerSourceIdentity.NormalizeOrder(["Spotify", "QQMusic", "Netease", "Kugou", "Browser"], []);
        Assert.Equal(["Spotify", "QQMusic", "Netease", "Kugou", "AppleMusic", "SodaMusic", "Foobar2000", "Browser"], order);
        Assert.Equal(order, PlayerSourceIdentity.NormalizeOrder(order, []));
        var movedBrowser = new[] { "Browser", "QQMusic", "Netease", "Kugou", "Spotify", "AppleMusic", "SodaMusic", "Foobar2000" };
        Assert.Equal(movedBrowser, PlayerSourceIdentity.NormalizeOrder(movedBrowser, []));
    }

    [Theory]
    [InlineData(nameof(AppSettings.EnableAppleMusic))]
    [InlineData(nameof(AppSettings.EnableSodaMusic))]
    [InlineData(nameof(AppSettings.EnableFoobar2000))]
    public void RecognitionChoicesSurviveCloneCopyAndSerialization(string field)
    {
        var current = new AppSettings();
        var property = typeof(AppSettings).GetProperty(field)!;
        Assert.Equal(true, property.GetValue(current));
        var next = current.Clone();
        property.SetValue(next, false);
        Assert.True(AppSettingsChangeSet.Create(current, next).PlayerRecognitionChanged);
        Assert.False(AppSettingsChangeSet.Create(current, next).LyricSyncServiceChanged);
        var target = new AppSettings();
        typeof(SettingsWindow).GetMethod("CopySettings", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [next, target]);
        Assert.Equal(false, property.GetValue(target));
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(target))!;
        Assert.Equal(false, property.GetValue(loaded));
    }

    [Theory]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App")]
    [InlineData("applemusic")]
    [InlineData("汽水音乐")]
    [InlineData("com.soda.music")]
    [InlineData("foobar2000")]
    [InlineData("foobar2000.exe")]
    public void LegacyCustomPlayersKeepIdentityDisabledStateOffsetAndTrustOrder(string session)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"TaskbarLyrics-additional-sources-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var providers = PlayerSourceSettings.CreateDefaultLyricProviders();
            providers.Reverse();
            providers[0] = providers[0] with { Enabled = false };
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                CustomPlayerSources = new[] { new CustomPlayerSource(session, "我的播放器", false) },
                SourceRecognitionOrder = new[] { "QQMusic", session, "Browser" },
                PlayerSources = new Dictionary<string, PlayerSourceSettings>
                {
                    [session] = new() { LyricOffsetMilliseconds = 246, LyricProviders = providers }
                }
            }));
            var store = new SettingsStore(path);
            var settings = store.Load();
            Assert.True(store.Save(settings));
            var loaded = store.Load();
            loaded.NormalizePlayerSources();
            var custom = Assert.Single(loaded.CustomPlayerSources);
            Assert.Equal(session, custom.SourceAppUserModelId);
            Assert.Equal("我的播放器", custom.DisplayName);
            Assert.False(custom.Enabled);
            Assert.Contains(session, loaded.PlayerSources.Keys);
            Assert.Equal(246, loaded.GetPlayerLyricOffsetMilliseconds(session));
            Assert.Equal(providers, loaded.GetPlayerLyricProviders(session));
            Assert.Equal(settings.SourceRecognitionOrder, loaded.SourceRecognitionOrder);
            using var provider = new SmtcMusicSessionProvider();
            provider.SetRecognitionOrder(loaded.SourceRecognitionOrder, [], [session]);
            Assert.False(provider.CanUseSource(session));
            provider.SetRecognitionOrder(loaded.SourceRecognitionOrder, [session], [session]);
            Assert.True(provider.CanUseSource(session));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
