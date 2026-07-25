using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class MediaHotkeyCatalogTests
{
    [Fact]
    public void Definitions_ProvideUniqueActionsStatusKeysAndRegistrationIds()
    {
        var definitions = MediaHotkeyCatalog.Definitions;

        Assert.Equal(Enum.GetValues<MediaHotkeyAction>().Length, definitions.Count);
        Assert.Equal(definitions.Count, definitions.Select(definition => definition.Action).Distinct().Count());
        Assert.Equal(definitions.Count, definitions.Select(definition => definition.StatusKey).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(definitions.Count, definitions.Select(definition => definition.RegistrationId).Distinct().Count());
    }

    [Fact]
    public void ResetBinding_UsesTheDefaultFromTheCatalog()
    {
        var settings = new GlobalMediaHotkeySettings { SeekForward = "Ctrl+F12" };

        settings.ResetBinding(MediaHotkeyAction.SeekForward);

        Assert.Equal(
            MediaHotkeyCatalog.Get(MediaHotkeyAction.SeekForward).DefaultBinding,
            settings.SeekForward);
    }
}
