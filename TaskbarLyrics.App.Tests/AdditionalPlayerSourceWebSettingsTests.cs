using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

[Collection("FloatingPanelInput")]
public sealed class AdditionalPlayerSourceWebSettingsTests
{
    private static readonly string[] ToggleValues = ["false", "{}", "true"];

    [Theory]
    [InlineData("enableAppleMusic", nameof(AppSettings.EnableAppleMusic))]
    [InlineData("enableSodaMusic", nameof(AppSettings.EnableSodaMusic))]
    [InlineData("enableFoobar2000", nameof(AppSettings.EnableFoobar2000))]
    public void SourceToggleMessagesUpdateTheirOwnSettingsAndRejectInvalidValues(string key, string field)
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
                    var apply = typeof(SettingsWindow).GetMethod("ApplyWebSettingUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var property = typeof(AppSettings).GetProperty(field)!;
                    foreach (var payload in ToggleValues)
                    {
                        using var document = JsonDocument.Parse(payload);
                        var json = JsonSerializer.Serialize(new { version = 1, type = "update", payload = new { key, value = document.RootElement } });
                        var message = SettingsWebMessageRouter.Parse(json);
                        Assert.NotNull(message);
                        apply.Invoke(window, [message.Key, message.Value]);
                        Assert.Equal(payload == "true", property.GetValue(settings));
                    }
                }
                finally { window.Close(); }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Playback source toggle did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
