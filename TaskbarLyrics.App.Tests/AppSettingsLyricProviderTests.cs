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
    public void CustomPlayerRetainsItsTrustOrderAndIdentityAcrossSettingsRoundTrip()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"custom-player-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            const string sourceId = "Example.Player_123!Music";
            var settings = new AppSettings();
            const string iconDataUrl = "data:image/png;base64,iVBORw0KGgo=";
            Assert.True(settings.AddCustomPlayerSource(sourceId, "示例播放器", iconDataUrl));
            Assert.False(settings.AddCustomPlayerSource(sourceId.ToUpperInvariant(), "重复"));
            var providers = settings.GetPlayerLyricProviders(sourceId).Reverse().ToList();
            providers[0] = providers[0] with { Enabled = false };
            Assert.True(settings.SetPlayerLyricProviders(sourceId, providers));
            settings.SetPlayerLyricOffsetMilliseconds(sourceId, 230);
            Assert.True(settings.SetCustomPlayerSourceEnabled(sourceId, false));
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            Assert.True(store.Save(settings));

            var loaded = store.Load();
            Assert.Single(loaded.CustomPlayerSources);
            Assert.Equal("示例播放器", loaded.CustomPlayerSources[0].DisplayName);
            Assert.False(loaded.CustomPlayerSources[0].Enabled);
            Assert.Equal(iconDataUrl, loaded.CustomPlayerSources[0].IconDataUrl);
            Assert.Equal(providers, loaded.GetPlayerLyricProviders(sourceId));
            Assert.Equal(230, loaded.GetPlayerLyricOffsetMilliseconds(sourceId));
            Assert.Equal(sourceId, loaded.SourceRecognitionOrder[^1]);
            Assert.NotNull(loaded.GetLyricSourceSelection(sourceId).CacheContext);
            Assert.Null(loaded.GetLyricSourceSelection("Other.Player").CacheContext);

            var clone = loaded.Clone();
            Assert.True(clone.RemoveCustomPlayerSource(sourceId));
            Assert.Empty(clone.CustomPlayerSources);
            Assert.DoesNotContain(sourceId, clone.PlayerSources.Keys);
            Assert.DoesNotContain(sourceId, clone.SourceRecognitionOrder);
            Assert.Single(loaded.CustomPlayerSources);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PresetPlayerIconPersistsAndInvalidStoredPresetFallsBackSafely()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"preset-player-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var settings = new AppSettings();
            Assert.True(settings.AddCustomPlayerSource("Example.Player!Music", "示例", presetIconId: "disc", presetIconColor: "#12abEF"));
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            Assert.True(store.Save(settings));

            var loaded = store.Load();
            Assert.Equal("disc", loaded.CustomPlayerSources[0].PresetIconId);
            Assert.Equal("#12ABEF", loaded.CustomPlayerSources[0].PresetIconColor);
            Assert.Empty(loaded.CustomPlayerSources[0].IconDataUrl);

            loaded.CustomPlayerSources[0] = loaded.CustomPlayerSources[0] with { PresetIconId = "unknown" };
            loaded.NormalizePlayerSources();
            Assert.Empty(loaded.CustomPlayerSources[0].PresetIconId);
            Assert.Empty(loaded.CustomPlayerSources[0].PresetIconColor);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CustomPlayerCardCanBeEditedWithoutChangingItsPlaybackPreferences()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"custom-player-edit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            const string sourceId = "Example.Player!Music";
            var settings = new AppSettings();
            Assert.True(settings.AddCustomPlayerSource(sourceId, "原名称", presetIconId: "note", presetIconColor: "#EAB308"));
            Assert.True(settings.SetCustomPlayerSourceEnabled(sourceId, false));
            settings.SetPlayerLyricOffsetMilliseconds(sourceId, 230);
            Assert.False(settings.UpdateCustomPlayerSource("QQMusic", "非法更新", "", "disc", "#123ABC"));
            Assert.False(settings.UpdateCustomPlayerSource(sourceId, "", "", "disc", "#123ABC"));
            Assert.False(settings.UpdateCustomPlayerSource(sourceId, "新名称", "data:text/html;base64,AAAA", "", ""));
            Assert.True(settings.UpdateCustomPlayerSource(sourceId.ToUpperInvariant(), "新名称", "", "disc", "#123abc"));

            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            Assert.True(store.Save(settings));
            var loaded = store.Load();
            var player = Assert.Single(loaded.CustomPlayerSources);
            Assert.Equal(sourceId, player.SourceAppUserModelId);
            Assert.Equal("新名称", player.DisplayName);
            Assert.Equal("disc", player.PresetIconId);
            Assert.Equal("#123ABC", player.PresetIconColor);
            Assert.False(player.Enabled);
            Assert.Equal(230, loaded.GetPlayerLyricOffsetMilliseconds(sourceId));
            Assert.Equal(sourceId, loaded.SourceRecognitionOrder[^1]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CustomPlayerAddPersistsRecognitionAndTrustChoices()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"custom-player-draft-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            const string sourceId = "Example.Player!Music";
            var providers = PlayerSourceSettings.CreateDefaultLyricProviders();
            (providers[0], providers[1]) = (providers[1], providers[0]);
            providers[2] = providers[2] with { Enabled = false };
            var settings = new AppSettings();
            Assert.True(settings.AddCustomPlayerSource(sourceId, "示例", presetIconId: "note",
                presetIconColor: "#EAB308", enabled: false, lyricProviders: providers));

            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            Assert.True(store.Save(settings));
            var loaded = store.Load();
            Assert.False(Assert.Single(loaded.CustomPlayerSources).Enabled);
            Assert.Equal(providers, loaded.GetPlayerLyricProviders(sourceId));
            Assert.False(settings.AddCustomPlayerSource("Other.Player", "其他", lyricProviders: providers.Take(2).ToList()));
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

        Assert.False(settings.EnableBrowser);
        Assert.Contains("Browser", settings.PlayerSources.Keys);
        Assert.Contains("Browser", settings.SourceRecognitionOrder);

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

    [Fact]
    public void CustomPlayerAddMessageRequiresAValidMediaSessionIdentity()
    {
        using var valid = JsonDocument.Parse("""
            {"sourceAppUserModelId":"Example.Player!Music","displayName":"示例播放器"}
            """);
        Assert.True(SettingsWebMessageRouter.TryParseCustomPlayerSourceAddRequest(
            valid.RootElement, out var request));
        Assert.Equal("Example.Player!Music", request.SourceAppUserModelId);
        Assert.Equal(string.Empty, request.IconDataUrl);
        Assert.True(request.Enabled);
        Assert.Equal(4, request.LyricProviders.Count);

        using var configured = JsonDocument.Parse("""
            {"sourceAppUserModelId":"Example.Player!Music","displayName":"示例播放器","enabled":false,
             "lyricProviders":[{"providerId":"Kugou","enabled":true},{"providerId":"QQMusic","enabled":true},
                               {"providerId":"Netease","enabled":false},{"providerId":"LRCLIB","enabled":true}]}
            """);
        Assert.True(SettingsWebMessageRouter.TryParseCustomPlayerSourceAddRequest(configured.RootElement, out var configuredRequest));
        Assert.False(configuredRequest.Enabled);
        Assert.Equal("Kugou", configuredRequest.LyricProviders[0].ProviderId);
        Assert.False(configuredRequest.LyricProviders[2].Enabled);
        using var invalidEnabled = JsonDocument.Parse("""
            {"sourceAppUserModelId":"Example.Player!Music","displayName":"示例播放器","enabled":"false"}
            """);
        Assert.False(SettingsWebMessageRouter.TryParseCustomPlayerSourceAddRequest(invalidEnabled.RootElement, out _));
        using var invalidProviders = JsonDocument.Parse("""
            {"sourceAppUserModelId":"Example.Player!Music","displayName":"示例播放器","lyricProviders":[]}
            """);
        Assert.False(SettingsWebMessageRouter.TryParseCustomPlayerSourceAddRequest(invalidProviders.RootElement, out _));

        using var icon = JsonDocument.Parse("""
            {"sourceAppUserModelId":"Example.Player!Music","displayName":"示例播放器","iconDataUrl":"data:image/png;base64,iVBORw0KGgo="}
            """);
        Assert.True(SettingsWebMessageRouter.TryParseCustomPlayerSourceAddRequest(icon.RootElement, out var iconRequest));
        Assert.Equal("data:image/png;base64,iVBORw0KGgo=", iconRequest.IconDataUrl);
        using var preset = JsonDocument.Parse("""
            {"sourceAppUserModelId":"Example.Player!Music","displayName":"示例播放器","presetIconId":"disc","presetIconColor":"#123abc"}
            """);
        Assert.True(SettingsWebMessageRouter.TryParseCustomPlayerSourceAddRequest(preset.RootElement, out var presetRequest));
        Assert.Equal("disc", presetRequest.PresetIconId);
        Assert.Equal("#123ABC", presetRequest.PresetIconColor);
        using var unknownPreset = JsonDocument.Parse("""
            {"sourceAppUserModelId":"Example.Player!Music","displayName":"示例播放器","presetIconId":"unknown","presetIconColor":"#123ABC"}
            """);
        Assert.False(SettingsWebMessageRouter.TryParseCustomPlayerSourceAddRequest(unknownPreset.RootElement, out _));
        using var conflictingIcons = JsonDocument.Parse("""
            {"sourceAppUserModelId":"Example.Player!Music","displayName":"示例播放器","iconDataUrl":"data:image/png;base64,iVBORw0KGgo=","presetIconId":"note","presetIconColor":"#123ABC"}
            """);
        Assert.False(SettingsWebMessageRouter.TryParseCustomPlayerSourceAddRequest(conflictingIcons.RootElement, out _));
        using var unsafeIcon = JsonDocument.Parse("""
            {"sourceAppUserModelId":"Example.Player!Music","displayName":"示例播放器","iconDataUrl":"data:image/svg+xml;base64,PHN2Zz4="}
            """);
        Assert.False(SettingsWebMessageRouter.TryParseCustomPlayerSourceAddRequest(unsafeIcon.RootElement, out _));

        using var invalid = JsonDocument.Parse("""
            {"sourceAppUserModelId":"\n","displayName":"示例播放器"}
            """);
        Assert.False(SettingsWebMessageRouter.TryParseCustomPlayerSourceAddRequest(
            invalid.RootElement, out _));
    }
}
