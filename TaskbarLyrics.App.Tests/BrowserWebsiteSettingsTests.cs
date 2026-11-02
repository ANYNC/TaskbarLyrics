using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

[Collection("FloatingPanelInput")]
public sealed class BrowserWebsiteSettingsTests
{
    [Fact]
    public void ExistingSettingsRemainUnconfiguredAndNewRulesRoundTripIndependently()
    {
        var path = Path.Combine(Path.GetTempPath(), $"TaskbarLyrics-browser-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"EnableBrowser\":true,\"WindowWidth\":480}");
            var store = new SettingsStore(path);
            var legacy = store.Load();
            Assert.True(legacy.EnableBrowser);
            Assert.False(legacy.BrowserWebsiteRecognitionEnabled);
            Assert.Empty(legacy.BrowserSites);
            legacy.BrowserWebsiteRecognitionEnabled = true;
            legacy.BrowserSites.Add(new("site", "Music", "http://localhost:8096/path", false));
            Assert.True(store.Save(legacy));
            var restored = store.Load();
            Assert.True(restored.BrowserWebsiteRecognitionEnabled);
            Assert.Equal("http://localhost:8096", Assert.Single(restored.BrowserSites).Origin);
            Assert.False(restored.BrowserSites[0].Enabled);
            Assert.Equal(480, restored.WindowWidth);
            var clone = restored.Clone();
            clone.BrowserSites.Clear();
            Assert.Single(restored.BrowserSites);
            Assert.False(new AppSettings().EnableBrowser);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WebsiteActionsValidateAtomicReordersAndKeepEmptyWhitelistConfigured()
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
                    bool Apply(string type, object payload)
                    {
                        var envelope = JsonSerializer.Serialize(new { version = 1, type, payload });
                        var message = SettingsWebMessageRouter.Parse(envelope)!;
                        return window.ApplyBrowserSiteAction(message.Type!, message.Value);
                    }
                    Assert.True(Apply("browserSiteAdd", new { address = "https://music.test/path", name = "Music" }));
                    Assert.True(settings.BrowserWebsiteRecognitionEnabled);
                    Assert.False(settings.EnableBrowser);
                    Assert.False(Apply("browserSiteAdd", new { address = "https://music.test/duplicate" }));
                    Assert.True(Apply("browserSiteAdd", new { address = "https://video.test" }));
                    var original = settings.BrowserSites.Select(site => site.Id).ToArray();
                    Assert.False(Apply("browserSitesReorder", new { ids = new[] { original[0], original[0] } }));
                    Assert.Equal(original, settings.BrowserSites.Select(site => site.Id));
                    Assert.True(Apply("browserSitesReorder", new { ids = original.Reverse().ToArray() }));
                    Assert.Equal(original.Reverse(), settings.BrowserSites.Select(site => site.Id));
                    Assert.False(Apply("browserSiteToggle", new { id = original[0], enabled = "false" }));
                    Assert.True(Apply("browserSiteToggle", new { id = original[0], enabled = false }));
                    Assert.False(settings.BrowserSites.Single(site => site.Id == original[0]).Enabled);
                    Assert.True(Apply("browserSiteRemove", new { id = original[0] }));
                    Assert.True(Apply("browserSiteRemove", new { id = original[1] }));
                    Assert.Empty(settings.BrowserSites);
                    Assert.True(settings.BrowserWebsiteRecognitionEnabled);
                    typeof(SettingsWindow).GetMethod("ApplyWebSettingUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(window, ["browserWebsiteRecognitionEnabled", JsonSerializer.SerializeToElement(false)]);
                    Assert.False(settings.BrowserWebsiteRecognitionEnabled);
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var payload = typeof(SettingsWindow).GetMethod("CreateSettingsPayload", flags)!.Invoke(window, null);
                    var json = JsonSerializer.SerializeToElement(payload, SettingsWebJson.Options);
                    Assert.False(json.GetProperty("browserWebsiteRecognitionEnabled").GetBoolean());
                    Assert.Equal(JsonValueKind.Array, json.GetProperty("browserSites").ValueKind);
                }
                finally { window.Close(); }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Website settings interaction timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
