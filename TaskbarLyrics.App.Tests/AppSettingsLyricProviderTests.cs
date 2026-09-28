using System.IO;
using System.Text.Json;
using TaskbarLyrics.Core.Models;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class AppSettingsLyricProviderTests
{
    [Fact]
    public void SettingsStoreRoundTripsPlayerSpecificTrustOrder()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"lyric-provider-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            var settings = new AppSettings();
            var providers = PlayerSourceSettings.CreateDefaultLyricProviders();
            providers.Reverse();
            providers[0] = providers[0] with { Enabled = false };
            Assert.True(settings.SetPlayerLyricProviders("Spotify", providers));
            Assert.True(store.Save(settings));

            var loaded = store.Load();
            Assert.Equal(providers, loaded.GetPlayerLyricProviders("Spotify"));
            Assert.Equal(PlayerSourceSettings.CreateDefaultLyricProviders(),
                loaded.GetPlayerLyricProviders("QQMusic"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OldSettingsKeepDefaultTrustOrderAndIndependentPlayerPreferences()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            {"PlayerSources":{"QQMusic":{"LyricOffsetMilliseconds":120}}}
            """)!;
        settings.NormalizePlayerSources();

        Assert.Equal(KnownLyricProviders.OnlineTrustOrder,
            settings.GetLyricSourceSelection("QQMusic").Order);
        Assert.Null(settings.GetLyricSourceSelection("QQMusic").CacheContext);
        Assert.Equal(120, settings.GetPlayerLyricOffsetMilliseconds("QQMusic"));

        var qqProviders = settings.GetPlayerLyricProviders("QQMusic").ToList();
        qqProviders.Reverse();
        qqProviders[1] = qqProviders[1] with { Enabled = false };
        Assert.True(settings.SetPlayerLyricProviders("QQMusic", qqProviders));
        Assert.Equal(3, settings.GetLyricSourceSelection("QQMusic").Order.Count);
        Assert.Equal(KnownLyricProviders.LrcLib, settings.GetLyricSourceSelection("QQMusic").Order[0]);
        Assert.NotNull(settings.GetLyricSourceSelection("QQMusic").CacheContext);
        Assert.Equal(KnownLyricProviders.OnlineTrustOrder,
            settings.GetLyricSourceSelection("Spotify").Order);

        var cloned = settings.Clone();
        cloned.PlayerSources["QQMusic"].LyricProviders[0] = new("QQMusic", false);
        Assert.Equal("LRCLIB", settings.PlayerSources["QQMusic"].LyricProviders[0].ProviderId);
    }

    [Fact]
    public void InvalidProviderListsAreRejectedAndAllDisabledIsAllowed()
    {
        var settings = new AppSettings();
        var invalid = PlayerSourceSettings.CreateDefaultLyricProviders();
        invalid[0] = invalid[1];
        Assert.False(settings.SetPlayerLyricProviders("QQMusic", invalid));
        Assert.Null(settings.GetLyricSourceSelection("QQMusic").CacheContext);

        var disabled = PlayerSourceSettings.CreateDefaultLyricProviders()
            .Select(item => item with { Enabled = false }).ToList();
        Assert.True(settings.SetPlayerLyricProviders("QQMusic", disabled));
        Assert.Empty(settings.GetLyricSourceSelection("QQMusic").Order);
    }

    [Fact]
    public void TrustChangeRebuildsTheLiveLyricService()
    {
        var current = new AppSettings();
        var next = current.Clone();
        var providers = next.GetPlayerLyricProviders("Netease").ToList();
        providers.Reverse();
        Assert.True(next.SetPlayerLyricProviders("Netease", providers));

        Assert.True(AppSettingsChangeSet.Create(current, next).LyricSyncServiceChanged);
    }

    [Fact]
    public void WebMessageRejectsMalformedAndDuplicateProviderLists()
    {
        using var valid = JsonDocument.Parse("""
            [{"providerId":"QQMusic","enabled":true},{"providerId":"Kugou","enabled":false},
             {"providerId":"Netease","enabled":true},{"providerId":"LRCLIB","enabled":true}]
            """);
        Assert.True(SettingsWebMessageRouter.TryParseLyricProviderPreferences(
            valid.RootElement, out var parsed));
        Assert.False(parsed[1].Enabled);

        using var duplicate = JsonDocument.Parse("""
            [{"providerId":"QQMusic","enabled":true},{"providerId":"QQMusic","enabled":true},
             {"providerId":"Netease","enabled":true},{"providerId":"LRCLIB","enabled":true}]
            """);
        Assert.False(SettingsWebMessageRouter.TryParseLyricProviderPreferences(
            duplicate.RootElement, out _));
        using var wrongType = JsonDocument.Parse("""
            [{"providerId":"QQMusic","enabled":"yes"}]
            """);
        Assert.False(SettingsWebMessageRouter.TryParseLyricProviderPreferences(
            wrongType.RootElement, out _));
    }
}
