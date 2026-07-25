using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class MediaHotkeyBindingParserTests
{
    [Theory]
    [InlineData("Ctrl+Shift+P", 0x50)]
    [InlineData("Alt+F12", 0x7B)]
    [InlineData("Ctrl + Right", 0x27)]
    [InlineData("Shift+7", 0x37)]
    public void TryParseWhenBindingIsSupportedReturnsExpectedVirtualKey(string value, int expectedVirtualKey)
    {
        var parsed = MediaHotkeyBindingParser.TryParse(value, out var binding);

        Assert.True(parsed);
        Assert.Equal(expectedVirtualKey, binding.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("P")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+P+Q")]
    [InlineData("Ctrl+Unsupported")]
    public void TryParseWhenBindingIsIncompleteOrUnsupportedReturnsFalse(string value)
    {
        var parsed = MediaHotkeyBindingParser.TryParse(value, out var binding);

        Assert.False(parsed);
        Assert.Equal(default, binding);
    }

    [Fact]
    public void TryParseWhenModifierIsRepeatedNormalizesItIntoOneModifierFlag()
    {
        var parsed = MediaHotkeyBindingParser.TryParse("Ctrl+Ctrl+P", out var binding);

        Assert.True(parsed);
        Assert.Equal(MediaHotkeyBindingParser.ModifierControl, binding.Modifiers);
        Assert.Equal(0x50, binding.VirtualKey);
    }
}
