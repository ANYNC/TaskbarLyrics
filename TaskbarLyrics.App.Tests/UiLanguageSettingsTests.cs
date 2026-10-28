using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

[Collection("FloatingPanelInput")]
public sealed class UiLanguageSettingsTests
{
    [Fact]
    public void CatalogDeclaresLocalizedNamesInSectionOrder()
    {
        Assert.Equal(["Zh-CN", "EN", "Zh-Hant"], Text.Languages.Select(language => language.Id));
        Assert.Equal(["简体中文", "English", "繁體中文"], Text.Languages.Select(language => language.Name));
    }

    [Fact]
    public void LanguageOptionsComeFromTheCatalog()
    {
        Assert.Equal("语言\\Language", Text.Translate("UI_InterfaceLanguage", "Zh-CN"));
        Assert.Equal("Language", Text.Translate("UI_InterfaceLanguage", "EN"));
        Assert.Equal("語言\\Language", Text.Translate("UI_InterfaceLanguage", "Zh-Hant"));
        Assert.Equal("自动检测", Text.Translate("UI_AutoDetect", "Zh-CN"));
        Assert.Equal("Auto-detect", Text.Translate("UI_AutoDetect", "EN"));
        Assert.Equal("自動偵測", Text.Translate("UI_AutoDetect", "Zh-Hant"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("zh-CN")]
    [InlineData("Zh-Hant-HK")]
    [InlineData("ja-JP")]
    public void UnknownPreferencesNormalizeToAutomatic(string? preference)
    {
        Assert.Equal("", Text.NormalizeLanguagePreference(preference));
    }

    [Theory]
    [InlineData("Zh-CN")]
    [InlineData("EN")]
    [InlineData("Zh-Hant")]
    public void CatalogIdsNormalizeToThemselves(string preference)
    {
        Assert.Equal(preference, Text.NormalizeLanguagePreference(preference));
        Assert.Equal(preference, Text.ResolveLanguage(preference));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fr-FR")]
    public void AutomaticPreferenceReusesSystemDetection(string? preference)
    {
        Assert.Equal(
            Text.DetectSystemLanguage(CultureInfo.CurrentUICulture),
            Text.ResolveLanguage(preference));
    }

    [Fact]
    public void MissingLanguagePreferenceLoadsAsAutomatic()
    {
        using var files = new TestDirectory();
        var path = Path.Combine(files.Path, "settings.json");
        File.WriteAllText(path, "{}");

        var loaded = new SettingsStore(path).Load();

        Assert.Equal("", loaded.UiLanguage);
        Assert.Equal("", loaded.Clone().UiLanguage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("EN")]
    [InlineData("Zh-Hant")]
    public void LanguagePreferenceRoundTrips(string language)
    {
        using var files = new TestDirectory();
        var path = Path.Combine(files.Path, "settings.json");
        var store = new SettingsStore(path);

        Assert.True(store.Save(new AppSettings { UiLanguage = language }));

        Assert.Equal(language, store.Load().UiLanguage);
    }

    [Fact]
    public void UnknownPersistedLanguageFallsBackToAutomatic()
    {
        using var files = new TestDirectory();
        var path = Path.Combine(files.Path, "settings.json");
        File.WriteAllText(path, "{\"UiLanguage\":\"ja-JP\"}");

        Assert.Equal("", new SettingsStore(path).Load().UiLanguage);
    }

    [Fact]
    public void SettingsWindowExposesCatalogLanguagesAndNormalizesUpdates()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings();
                using var offsets = new TrackLyricOffsetStore();
                var window = new SettingsWindow(settings, offsets,
                    () => Task.FromResult<CurrentTrackLyricsContext?>(null), () => throw new NotSupportedException(),
                    (_, _, _) => throw new NotSupportedException(), (_, _, _) => throw new NotSupportedException(), () => { });
                try
                {
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    void Update(string key, object value)
                    {
                        var message = SettingsWebMessageRouter.Parse(JsonSerializer.Serialize(new
                        {
                            version = 1,
                            type = "update",
                            payload = new { key, value }
                        }));
                        Assert.NotNull(message);
                        typeof(SettingsWindow).GetMethod("ApplyWebSettingUpdate", flags)!.Invoke(window, [message.Key, message.Value]);
                    }

                    var payload = ReadPayload(window, flags);
                    Assert.Equal("", payload.GetProperty("uiLanguage").GetString());

                    Update("uiLanguage", "Zh-Hant");
                    Assert.Equal("Zh-Hant", settings.UiLanguage);
                    Assert.Equal("Zh-Hant", ReadPayload(window, flags).GetProperty("uiLanguage").GetString());

                    Update("uiLanguage", "ja-JP");
                    Assert.Equal("", settings.UiLanguage);
                    Update("uiLanguage", 3);
                    Assert.Equal("", settings.UiLanguage);
                }
                finally { window.Close(); }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Settings interaction did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static JsonElement ReadPayload(SettingsWindow window, BindingFlags flags) =>
        JsonSerializer.SerializeToElement(
            typeof(SettingsWindow).GetMethod("CreateSettingsPayload", flags)!.Invoke(window, null)!,
            SettingsWebJson.Options);
}
