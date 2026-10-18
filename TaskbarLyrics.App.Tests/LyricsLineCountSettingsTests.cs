using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

[Collection("FloatingPanelInput")]
public sealed class LyricsLineCountSettingsTests
{
    private static readonly bool[] Choices = [true, false];

    [Fact]
    public void LegacySettingsDefaultToDoubleLineAndBothChoicesRoundTrip()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"line-count-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            File.WriteAllText(path, "{\"WindowWidth\":500,\"FontSize\":18}");
            var store = new SettingsStore(path);
            var settings = store.Load();
            Assert.False(settings.SingleLineLyrics);
            foreach (var singleLine in Choices)
            {
                settings.SingleLineLyrics = singleLine;
                Assert.True(store.Save(settings));
                var loaded = store.Load();
                Assert.Equal(singleLine, loaded.SingleLineLyrics);
                Assert.Equal(500, loaded.WindowWidth);
                Assert.Equal(18, loaded.FontSize);
            }
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WebUpdatesAndSettingsPayloadCarryLineCountInBothWindowModes(bool floating)
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
                    foreach (var singleLine in Choices)
                    {
                        var message = SettingsWebMessageRouter.Parse(JsonSerializer.Serialize(new
                        {
                            version = 1,
                            type = "update",
                            payload = new { key = "singleLineLyrics", value = singleLine }
                        }));
                        Assert.NotNull(message);
                        typeof(SettingsWindow).GetMethod("ApplyWebSettingUpdate", flags)!.Invoke(window, [message.Key, message.Value]);
                        Assert.Equal(singleLine, settings.SingleLineLyrics);
                        var dto = typeof(SettingsWindow).GetMethod("CreateSettingsPayload", flags)!.Invoke(window, null)!;
                        Assert.Equal(singleLine, JsonSerializer.SerializeToElement(dto, SettingsWebJson.Options)
                            .GetProperty("singleLineLyrics").GetBoolean());
                    }
                    var invalid = JsonSerializer.SerializeToElement(new { value = 123 }).GetProperty("value");
                    typeof(SettingsWindow).GetMethod("ApplyWebSettingUpdate", flags)!.Invoke(window, ["singleLineLyrics", invalid]);
                    Assert.False(settings.SingleLineLyrics);
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
