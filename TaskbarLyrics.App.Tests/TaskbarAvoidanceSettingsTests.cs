using System.Reflection;
using System.Text.Json;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    private static readonly bool[] TaskbarAvoidanceValues = [false, true];
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TaskbarAvoidanceSettingUpdatesPayloadCopiesAndIgnoresFloatingEdits(bool floating)
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
                foreach (var enabled in TaskbarAvoidanceValues)
                {
                    var message = SettingsWebMessageRouter.Parse(JsonSerializer.Serialize(new
                    {
                        version = 1,
                        type = "update",
                        payload = new { key = "taskbarAvoidanceEnabled", value = enabled }
                    }));
                    Assert.NotNull(message);
                    typeof(SettingsWindow).GetMethod("ApplyWebSettingUpdate", flags)!.Invoke(window, [message.Key, message.Value]);
                    var expected = floating || enabled;
                    Assert.Equal(expected, settings.TaskbarAvoidanceEnabled);
                    var dto = typeof(SettingsWindow).GetMethod("CreateSettingsPayload", flags)!.Invoke(window, null)!;
                    Assert.Equal(expected, JsonSerializer.SerializeToElement(dto, SettingsWebJson.Options)
                        .GetProperty("taskbarAvoidanceEnabled").GetBoolean());
                    var target = new AppSettings { TaskbarAvoidanceEnabled = !expected };
                    typeof(SettingsWindow).GetMethod("CopySettings", BindingFlags.Static | BindingFlags.NonPublic)!
                        .Invoke(null, [settings, target]);
                    Assert.Equal(expected, target.TaskbarAvoidanceEnabled);
                }
                var invalid = JsonSerializer.SerializeToElement(123);
                typeof(SettingsWindow).GetMethod("ApplyWebSettingUpdate", flags)!.Invoke(window, ["taskbarAvoidanceEnabled", invalid]);
                Assert.True(settings.TaskbarAvoidanceEnabled);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TaskbarAvoidanceChangeRepositionsOnlyEmbeddedWindows(bool floating)
    {
        var current = new AppSettings { UseFloatingWindow = floating };
        var next = current.Clone();
        next.TaskbarAvoidanceEnabled = false;
        var changes = AppSettingsChangeSet.Create(current, next);
        Assert.Equal(!floating, changes.WindowLayoutChanged);
        Assert.Equal(!floating, changes.RequiresLyricsWindowApply);
        Assert.False(changes.LyricSyncServiceChanged);
        Assert.False(changes.VisualStyleChanged);
        Assert.False(changes.TaskbarEmbeddingChanged);
        next.ResetWindowPresentation(floating: true);
        Assert.False(next.TaskbarAvoidanceEnabled);
        next.ResetWindowPresentation(floating: false);
        Assert.True(next.TaskbarAvoidanceEnabled);
    }
}
