using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

[Collection("FloatingPanelInput")]
public sealed class ControlPanelTriggerWebSettingsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingsUpdatesAreModeScopedAndReturnNormalizedTriggerChoices(bool floating)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings { UseFloatingWindow = floating };
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
                    Update("controlPanelTriggerMethod", "Hover");
                    Update("controlPanelTriggerLyrics", true);
                    Update("controlPanelTriggerCover", false);
                    Update("embeddedPanelBrightness", 0.8);
                    Update("embeddedPanelTintOpacity", 0.3);
                    Update("embeddedPanelBlurAmount", 48);
                    Update("floatingPanelBrightness", 0.2);
                    Update("showFloatingBackground", true);
                    Update("showFloatingBackground", "invalid");
                    Assert.Equal(floating, settings.ShowFloatingBackground);
                    Update("floatingWindowLayer", "Desktop");
                    Assert.Equal(floating ? FloatingWindowLayer.Desktop : FloatingWindowLayer.Fullscreen, settings.FloatingWindowLayer);
                    Update("floatingWindowLayer", "Unknown");
                    Update("floatingWindowLayer", "99");
                    Update("floatingWindowLayer", 1);
                    Assert.Equal(floating ? FloatingWindowLayer.Desktop : FloatingWindowLayer.Fullscreen, settings.FloatingWindowLayer);
                    Assert.Equal(floating ? 0.20 : 0.8, settings.EmbeddedPanelBrightness);
                    Assert.Equal(floating ? 0.65 : 0.3, settings.EmbeddedPanelTintOpacity);
                    Assert.Equal(floating ? 24 : 48, settings.EmbeddedPanelBlurAmount);
                    Assert.Equal(floating ? 0.2 : 0.25, settings.FloatingPanelBrightness);
                    Assert.Equal(floating ? ControlPanelTriggerMethod.Click : ControlPanelTriggerMethod.Hover, settings.ControlPanelTriggerMethod);
                    Assert.Equal(floating, settings.ControlPanelTriggerCover);
                    Assert.Equal(!floating, settings.ControlPanelTriggerLyrics);
                    Update("controlPanelTriggerMethod", "Unknown");
                    Update("controlPanelTriggerLyrics", "invalid");
                    Assert.Equal(!floating, settings.ControlPanelTriggerLyrics);
                    Update("showCover", false);
                    typeof(SettingsWindow).GetMethod("SaveSettings", flags)!.Invoke(window, null);
                    Assert.True(settings.ControlPanelTriggerLyrics);
                    var dto = typeof(SettingsWindow).GetMethod("CreateSettingsPayload", flags)!.Invoke(window, null)!;
                    var payload = JsonSerializer.SerializeToElement(dto, SettingsWebJson.Options);
                    Assert.Equal(floating ? "Desktop" : "Fullscreen", payload.GetProperty("floatingWindowLayer").GetString());
                    Assert.Equal(floating, payload.GetProperty("showFloatingBackground").GetBoolean());
                    Assert.Equal(settings.EmbeddedPanelBrightness, payload.GetProperty("embeddedPanelBrightness").GetDouble());
                    Assert.Equal(settings.EmbeddedPanelTintOpacity, payload.GetProperty("embeddedPanelTintOpacity").GetDouble());
                    Assert.Equal(settings.EmbeddedPanelBlurAmount, payload.GetProperty("embeddedPanelBlurAmount").GetDouble());
                    Assert.Equal(floating ? "Click" : "Hover", payload.GetProperty("controlPanelTriggerMethod").GetString());
                    Assert.Equal(settings.ControlPanelTriggerCover, payload.GetProperty("controlPanelTriggerCover").GetBoolean());
                    Assert.True(payload.GetProperty("controlPanelTriggerLyrics").GetBoolean());
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
}
