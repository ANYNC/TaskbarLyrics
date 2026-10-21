using System.Reflection;
using System.Text.Json;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    private static readonly bool[] SearchStatusValues = [false, true];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SearchStatusSettingUpdatesPayloadAndCopiesInBothWindowModes(bool floating)
    {
        RunSta(() =>
        {
            var settings = new AppSettings { UseFloatingWindow = floating };
            using var offsets = new TrackLyricOffsetStore();
            var window = new SettingsWindow(settings, offsets,
                () => Task.FromResult<CurrentTrackLyricsContext?>(null), () => throw new NotSupportedException(),
                (_, _, _) => throw new NotSupportedException(), (_, _, _) => throw new NotSupportedException(), () => { });
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                foreach (var enabled in SearchStatusValues)
                {
                    var message = SettingsWebMessageRouter.Parse(JsonSerializer.Serialize(new
                    {
                        version = 1,
                        type = "update",
                        payload = new { key = "showLyricSearchStatus", value = enabled }
                    }));
                    Assert.NotNull(message);
                    typeof(SettingsWindow).GetMethod("ApplyWebSettingUpdate", flags)!.Invoke(window, [message.Key, message.Value]);
                    Assert.Equal(enabled, settings.ShowLyricSearchStatus);
                    var dto = typeof(SettingsWindow).GetMethod("CreateSettingsPayload", flags)!.Invoke(window, null)!;
                    Assert.Equal(enabled, JsonSerializer.SerializeToElement(dto, SettingsWebJson.Options)
                        .GetProperty("showLyricSearchStatus").GetBoolean());
                    var target = new AppSettings { ShowLyricSearchStatus = !enabled };
                    typeof(SettingsWindow).GetMethod("CopySettings", BindingFlags.Static | BindingFlags.NonPublic)!
                        .Invoke(null, [settings, target]);
                    Assert.Equal(enabled, target.ShowLyricSearchStatus);
                }
                var invalid = JsonSerializer.SerializeToElement(123);
                typeof(SettingsWindow).GetMethod("ApplyWebSettingUpdate", flags)!.Invoke(window, ["showLyricSearchStatus", invalid]);
                Assert.True(settings.ShowLyricSearchStatus);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SearchStatusSettingReappliesPresentationWithoutRestartingRetrieval(bool enabled)
    {
        var current = new AppSettings { ShowLyricSearchStatus = !enabled };
        var next = current.Clone();
        next.ShowLyricSearchStatus = enabled;
        var changes = AppSettingsChangeSet.Create(current, next);
        Assert.True(changes.SearchStatusDisplayChanged);
        Assert.True(changes.RequiresLyricsWindowApply);
        Assert.False(changes.LyricSyncServiceChanged);
        Assert.False(changes.PlayerRecognitionChanged);
        Assert.False(changes.VisualStyleChanged);
        Assert.False(changes.LyricsLayoutChanged);
        Assert.False(changes.WindowLayoutChanged);
    }
}
