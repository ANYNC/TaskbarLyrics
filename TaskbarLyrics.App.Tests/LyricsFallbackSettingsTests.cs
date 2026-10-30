using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class LyricsFallbackSettingsTests
{
    [Theory]
    [InlineData(SpectrumDisplayMode.Disabled, false, false, false)]
    [InlineData(SpectrumDisplayMode.PureMusicOnly, true, false, false)]
    [InlineData(SpectrumDisplayMode.PureMusicOrNoLyrics, true, true, false)]
    [InlineData(SpectrumDisplayMode.Always, true, true, true)]
    public void MissingNewSettingsUseLegacyPreferences(SpectrumDisplayMode mode, bool pure, bool missing, bool lyrics)
    {
        var settings = new AppSettings { SpectrumDisplayMode = mode, ShowLyricSearchStatus = false };
        var rules = settings.GetLyricsFallback();
        Assert.Null(settings.LyricsFallback);
        Assert.Equal("Metadata", rules.Searching.FirstLine.Kind);
        Assert.True(rules.Searching.FirstLine.ShowArtist);
        Assert.Equal("None", rules.Searching.SecondLine.Kind);
        Assert.False(rules.Searching.UseSpectrum);
        Assert.Equal(pure, rules.Instrumental.UseSpectrum);
        Assert.Equal(missing, rules.NotFound.UseSpectrum);
        Assert.Equal(lyrics, rules.SpectrumWithLyrics);
    }

    [Fact]
    public void CloneAndSettingsChangeTrackingPreserveIndependentRulesWithoutRestartingRetrieval()
    {
        var settings = new AppSettings { LyricsFallback = new() };
        var clone = settings.Clone();
        Assert.False(AppSettingsChangeSet.Create(settings, clone).RequiresLyricsWindowApply);
        clone.LyricsFallback!.Searching.FirstLine.Kind = "Text";
        clone.LyricsFallback.Searching.FirstLine.CustomText = "My text";
        Assert.Equal("Metadata", settings.LyricsFallback.Searching.FirstLine.Kind);
        var changes = AppSettingsChangeSet.Create(settings, clone);
        Assert.True(changes.SearchStatusDisplayChanged);
        Assert.True(changes.SpectrumDisplayChanged);
        Assert.True(changes.RequiresLyricsWindowApply);
        Assert.False(changes.LyricSyncServiceChanged);
        Assert.False(changes.PlayerRecognitionChanged);
    }

    [Fact]
    public void PersistenceKeepsCustomLinesNullDefaultsAndUnrepresentableSpectrumCombination()
    {
        using var directory = new TestDirectory();
        var store = new SettingsStore(Path.Combine(directory.Path, "settings.json"));
        var settings = new AppSettings { SpectrumAudioAccessGranted = true, WindowWidth = 780, LyricsFallback = new() };
        settings.LyricsFallback.NotFound.UseSpectrum = true;
        settings.LyricsFallback.Searching.FirstLine.Kind = "Text";
        settings.LyricsFallback.Searching.FirstLine.CustomText = "<b>plain text</b>";
        Assert.True(store.Save(settings));
        var loaded = store.Load();
        Assert.Equal(780, loaded.WindowWidth);
        Assert.Equal(SpectrumDisplayMode.Custom, loaded.SpectrumDisplayMode);
        Assert.Equal("<b>plain text</b>", loaded.LyricsFallback!.Searching.FirstLine.CustomText);
        Assert.Null(loaded.LyricsFallback.Searching.SecondLine.CustomText);
        Assert.True(loaded.LyricsFallback.NotFound.UseSpectrum);
        Assert.False(loaded.LyricsFallback.Instrumental.UseSpectrum);
        Assert.True(store.Save(loaded));
        Assert.Equal(loaded.LyricsFallback, store.Load().LyricsFallback);
    }

    [Fact]
    public void ConsentRevocationAndLegacyTrayCommandsKeepLinePreferences()
    {
        var settings = new AppSettings { LyricsFallback = new(), SpectrumAudioAccessGranted = true };
        settings.LyricsFallback.Instrumental.FirstLine.CustomText = "Keep me";
        settings.SetLegacySpectrumMode(SpectrumDisplayMode.Always);
        Assert.True(settings.GetLyricsFallback().UsesSpectrum);
        settings.SetLegacySpectrumMode(SpectrumDisplayMode.Disabled);
        Assert.False(settings.GetLyricsFallback().UsesSpectrum);
        Assert.Equal("Keep me", settings.LyricsFallback.Instrumental.FirstLine.CustomText);
        settings.SetLegacySpectrumMode(SpectrumDisplayMode.PureMusicOnly);
        Assert.True(settings.GetLyricsFallback().Instrumental.UseSpectrum);
        Assert.False(settings.GetLyricsFallback().NotFound.UseSpectrum);
    }

    [Fact]
    public void UnrecognizedStoredRuleVersionDoesNotDiscardOtherSettingsOrGrantAudio()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, "{\"WindowWidth\":650,\"LyricsFallback\":{\"Version\":2},\"SpectrumAudioAccessGranted\":false}");
        var settings = new SettingsStore(path).Load();
        Assert.Equal(650, settings.WindowWidth);
        Assert.False(settings.SpectrumAudioAccessGranted);
        Assert.False(settings.GetLyricsFallback().UsesSpectrum);
    }

    [Theory]
    [InlineData("version", "\"bad\"")]
    [InlineData("version", "2")]
    [InlineData("searching.useSpectrum", "true")]
    [InlineData("searching.firstLine.kind", "\"Unknown\"")]
    [InlineData("notFound.secondLine.showTitle", "12")]
    [InlineData("instrumental.firstLine.customText", "[]")]
    public void WebBoundaryRejectsMalformedRules(string path, string json)
    {
        var value = JsonSerializer.SerializeToNode(new LyricsFallbackSettings(), SettingsWebJson.Options)!;
        var parts = path.Split('.');
        var target = value;
        foreach (var part in parts[..^1]) target = target![part];
        target![parts[^1]] = JsonNode.Parse(json);
        Assert.False(LyricsFallbackSettings.TryRead(JsonSerializer.SerializeToElement(value), out _));
    }

    [Fact]
    public void WebBoundaryRejectsOversizedTextAndNormalizesEmptyMetadata()
    {
        var rules = new LyricsFallbackSettings();
        rules.Searching.FirstLine.CustomText = new string('x', 121);
        Assert.False(LyricsFallbackSettings.TryRead(JsonSerializer.SerializeToElement(rules, SettingsWebJson.Options), out _));
        rules.Searching.FirstLine.CustomText = "";
        rules.Searching.FirstLine.ShowTitle = false;
        rules.Searching.FirstLine.ShowArtist = false;
        Assert.True(LyricsFallbackSettings.TryRead(JsonSerializer.SerializeToElement(rules, SettingsWebJson.Options), out var parsed));
        Assert.True(parsed.Searching.FirstLine.ShowTitle);
        Assert.Equal("", parsed.Searching.FirstLine.CustomText);
    }

    [Fact]
    public void PresentationUsesMetadataOrLiteralTextAndKeepsExplicitEmptyLinesBlank()
    {
        var track = new TrackInfo("a", "  Title  ", " Artist ", "", "QQMusic", TimeSpan.FromMinutes(3));
        var rules = new LyricsFallbackState
        {
            FirstLine = new() { Kind = "Metadata", ShowArtist = true },
            SecondLine = new() { CustomText = "<script>text</script>" }
        };
        var result = LyricsFallbackPresentationPolicy.Create(rules, track, "Localized default");
        Assert.Equal("Title - Artist", result.Current);
        Assert.Equal("<script>text</script>", result.Next);
        rules.FirstLine.Kind = "None";
        rules.SecondLine.CustomText = "";
        result = LyricsFallbackPresentationPolicy.Create(rules, null, "Localized default");
        Assert.Equal(" ", result.Current);
        Assert.Equal(" ", result.Next);
        rules.SecondLine.CustomText = null;
        Assert.Equal("Localized default", LyricsFallbackPresentationPolicy.Create(rules, null, "Localized default").Next);
    }

    [Fact]
    public void SpectrumPolicySeparatesSearchingLyricsInstrumentalAndMissingStates()
    {
        var settings = new AppSettings { SpectrumAudioAccessGranted = true, LyricsFallback = new() };
        settings.LyricsFallback.NotFound.UseSpectrum = true;
        var lyric = new LyricDisplayFrame("lyric", "next", "title", 0, 0);
        var pure = lyric with { IsPureMusic = true };
        var missing = new LyricDisplayFrame(LyricSyncService.NoLyricsText, "", "title", 0, -1);
        Assert.True(LyricsFallbackPresentationPolicy.ShouldShowSpectrum(settings, missing, false));
        Assert.False(LyricsFallbackPresentationPolicy.ShouldShowSpectrum(settings, pure, false));
        Assert.False(LyricsFallbackPresentationPolicy.ShouldShowSpectrum(settings, lyric, false));
        settings.LyricsFallback.SpectrumWithLyrics = true;
        Assert.True(LyricsFallbackPresentationPolicy.ShouldShowSpectrum(settings, lyric, false));
        Assert.False(LyricsFallbackPresentationPolicy.ShouldShowSpectrum(settings, lyric, true));
        settings.SpectrumAudioAccessGranted = false;
        Assert.False(LyricsFallbackPresentationPolicy.ShouldShowSpectrum(settings, missing, false));
    }
}

public sealed partial class FloatingPanelInputTests
{
    [Fact]
    public void StructuredFallbackConsentAndRevocationPreserveCustomText()
    {
        RunSta(() =>
        {
            var settings = new AppSettings();
            using var offsets = new TrackLyricOffsetStore();
            var window = new SettingsWindow(settings, offsets,
                () => Task.FromResult<CurrentTrackLyricsContext?>(null), () => throw new NotSupportedException(),
                (_, _, _) => throw new NotSupportedException(), (_, _, _) => throw new NotSupportedException(), () => { });
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var rules = new LyricsFallbackSettings();
                rules.NotFound.UseSpectrum = true;
                rules.NotFound.FirstLine.CustomText = "Keep my text";
                var value = JsonSerializer.SerializeToElement(new { spectrumDisplayMode = "Custom", lyricsFallback = rules }, SettingsWebJson.Options);
                ((Task)typeof(SettingsWindow).GetMethod("ConfirmSpectrumAudioAccessAsync", flags)!.Invoke(window, [value])!).GetAwaiter().GetResult();
                Assert.True(settings.SpectrumAudioAccessGranted);
                Assert.True(settings.LyricsFallback!.NotFound.UseSpectrum);
                ((Task)typeof(SettingsWindow).GetMethod("RevokeSpectrumAudioAccessAsync", flags)!.Invoke(window, null)!).GetAwaiter().GetResult();
                Assert.False(settings.SpectrumAudioAccessGranted);
                Assert.False(settings.GetLyricsFallback().UsesSpectrum);
                Assert.Equal("Keep my text", settings.LyricsFallback.NotFound.FirstLine.CustomText);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FallbackWebUpdatesRespectConsentAndCopyIntoPayload(bool granted)
    {
        RunSta(() =>
        {
            var settings = new AppSettings { SpectrumAudioAccessGranted = granted };
            using var offsets = new TrackLyricOffsetStore();
            var window = new SettingsWindow(settings, offsets,
                () => Task.FromResult<CurrentTrackLyricsContext?>(null), () => throw new NotSupportedException(),
                (_, _, _) => throw new NotSupportedException(), (_, _, _) => throw new NotSupportedException(), () => { });
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var rules = new LyricsFallbackSettings();
                rules.NotFound.UseSpectrum = true;
                var update = typeof(SettingsWindow).GetMethod("ApplyWebSettingUpdate", flags)!;
                update.Invoke(window, ["lyricsFallback", JsonSerializer.SerializeToElement(rules, SettingsWebJson.Options)]);
                Assert.Equal(granted, settings.LyricsFallback is not null);
                rules.NotFound.UseSpectrum = false;
                rules.NotFound.FirstLine.Kind = "Metadata";
                update.Invoke(window, ["lyricsFallback", JsonSerializer.SerializeToElement(rules, SettingsWebJson.Options)]);
                Assert.Equal("Metadata", settings.LyricsFallback!.NotFound.FirstLine.Kind);
                var payload = JsonSerializer.SerializeToElement(typeof(SettingsWindow).GetMethod("CreateSettingsPayload", flags)!.Invoke(window, null), SettingsWebJson.Options);
                Assert.Equal("Metadata", payload.GetProperty("lyricsFallback").GetProperty("notFound").GetProperty("firstLine").GetProperty("kind").GetString());
                var copy = new AppSettings();
                typeof(SettingsWindow).GetMethod("CopySettings", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [settings, copy]);
                Assert.Equal(settings.LyricsFallback, copy.LyricsFallback);
                Assert.NotSame(settings.LyricsFallback, copy.LyricsFallback);
            }
            finally { window.Close(); }
        });
    }
}
