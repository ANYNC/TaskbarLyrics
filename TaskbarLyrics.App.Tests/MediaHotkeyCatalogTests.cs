using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class MediaHotkeyCatalogTests
{
    [Theory]
    [InlineData(MediaHotkeyAction.TogglePlayPause, "Ctrl+Alt+P")]
    [InlineData(MediaHotkeyAction.PreviousTrack, "Ctrl+Alt+Left")]
    [InlineData(MediaHotkeyAction.NextTrack, "Ctrl+Alt+Right")]
    [InlineData(MediaHotkeyAction.ToggleLyricsVisibility, "Ctrl+Alt+D")]
    public void DefinitionsUseExpectedDefaultBindings(MediaHotkeyAction action, string expectedBinding)
    {
        Assert.Equal(expectedBinding, MediaHotkeyCatalog.Get(action).DefaultBinding);
    }

    [Fact]
    public void DefinitionsProvideUniqueActionsStatusKeysAndRegistrationIds()
    {
        var definitions = MediaHotkeyCatalog.Definitions;

        Assert.Equal(Enum.GetValues<MediaHotkeyAction>().Length, definitions.Count);
        Assert.Equal(definitions.Count, definitions.Select(definition => definition.Action).Distinct().Count());
        Assert.Equal(definitions.Count, definitions.Select(definition => definition.StatusKey).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(definitions.Count, definitions.Select(definition => definition.RegistrationId).Distinct().Count());
    }

    [Fact]
    public void ResetBindingUsesTheDefaultFromTheCatalog()
    {
        var settings = new GlobalMediaHotkeySettings { SeekForward = "Ctrl+F12" };

        settings.ResetBinding(MediaHotkeyAction.SeekForward);

        Assert.Equal(
            MediaHotkeyCatalog.Get(MediaHotkeyAction.SeekForward).DefaultBinding,
            settings.SeekForward);
    }
}
