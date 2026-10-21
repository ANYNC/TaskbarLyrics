using System.Reflection;
using System.Text.Json;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MediaHotkeySettingsAcceptIndividualSwitchesEmptyBindingsAndReset(bool floating)
    {
        RunSta(() =>
        {
            var settings = new AppSettings { UseFloatingWindow = floating };
            using var offsets = new TrackLyricOffsetStore();
            var window = new SettingsWindow(settings, offsets,
                () => Task.FromResult<CurrentTrackLyricsContext?>(null), () => throw new NotSupportedException(),
                (_, _, _) => throw new NotSupportedException(), (_, _, _) => throw new NotSupportedException(), () => { });
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            void Update(string key, object value)
            {
                var message = SettingsWebMessageRouter.Parse(JsonSerializer.Serialize(new
                {
                    version = 1,
                    type = "update",
                    payload = new { key, value }
                }))!;
                typeof(SettingsWindow).GetMethod("ApplyWebSettingUpdate", flags)!.Invoke(window, [message.Key, message.Value]);
            }
            try
            {
                foreach (var definition in MediaHotkeyCatalog.Definitions)
                {
                    Update(definition.EnabledSettingKey, false);
                    Assert.False(settings.GlobalMediaHotkeys.IsActionEnabled(definition.Action));
                    Update(definition.EnabledSettingKey, 123);
                    Assert.False(settings.GlobalMediaHotkeys.IsActionEnabled(definition.Action));
                    Update(definition.SettingKey, "");
                    Assert.Empty(settings.GlobalMediaHotkeys.GetBinding(definition.Action));
                    Update(definition.SettingKey, "Ctrl+F12");
                    Update(definition.EnabledSettingKey, true);
                    Assert.True(settings.GlobalMediaHotkeys.IsActionEnabled(definition.Action));
                    Assert.Equal("Ctrl+F12", settings.GlobalMediaHotkeys.GetBinding(definition.Action));
                    var dto = typeof(SettingsWindow).GetMethod("CreateSettingsPayload", flags)!.Invoke(window, null)!;
                    var payload = JsonSerializer.SerializeToElement(dto, SettingsWebJson.Options);
                    Assert.Equal("Ctrl+F12", payload.GetProperty(definition.SettingKey).GetString());
                    var entry = payload.GetProperty("mediaHotkeys").EnumerateArray()
                        .Single(item => item.GetProperty("action").GetString() == definition.Action.ToString());
                    Assert.True(entry.GetProperty("isEnabled").GetBoolean());
                    Assert.Equal(definition.EnabledSettingKey, entry.GetProperty("enabledSettingKey").GetString());
                    typeof(SettingsWindow).GetMethod("ResetMediaHotkey", flags)!
                        .Invoke(window, [JsonSerializer.SerializeToElement(definition.Action.ToString())]);
                    Assert.Equal(definition.DefaultBinding, settings.GlobalMediaHotkeys.GetBinding(definition.Action));
                }
                settings.GlobalMediaHotkeys.SetActionEnabled(MediaHotkeyAction.ToggleDisplayMode, false);
                var target = new AppSettings();
                typeof(SettingsWindow).GetMethod("CopySettings", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [settings, target]);
                Assert.False(target.GlobalMediaHotkeys.IsActionEnabled(MediaHotkeyAction.ToggleDisplayMode));
                target.GlobalMediaHotkeys.SetActionEnabled(MediaHotkeyAction.ToggleDisplayMode, true);
                Assert.False(settings.GlobalMediaHotkeys.IsActionEnabled(MediaHotkeyAction.ToggleDisplayMode));
            }
            finally { window.Close(); }
        });
    }
}
