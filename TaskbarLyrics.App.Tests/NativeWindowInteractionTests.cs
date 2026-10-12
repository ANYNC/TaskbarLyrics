using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class NativeWindowInteractionTests
{
    private const long Transparent = 0x00000020;
    private const long NoActivate = 0x08000000;
    private const long Layered = 0x00080000;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplyClickThroughStyleAddsTheMaskWithoutDroppingExistingStyles(bool includeNoActivate)
    {
        var next = NativeWindowInteraction.ApplyClickThroughStyle(Layered, includeNoActivate);

        Assert.Equal(Layered | Transparent | (includeNoActivate ? NoActivate : 0), next);
    }

    [Fact]
    public void ShouldRestoreClickThroughStyleAcceptsOnlyStylesThisControllerProduced()
    {
        const long original = 0x00080080;

        Assert.True(NativeWindowInteraction.ShouldRestoreClickThroughStyle(
            original | Transparent | NoActivate,
            original,
            includeNoActivate: true));
        Assert.True(NativeWindowInteraction.ShouldRestoreClickThroughStyle(
            original | Transparent,
            original,
            includeNoActivate: false));
    }

    [Fact]
    public void ShouldRestoreClickThroughStyleLeavesStylesChangedByAnotherComponentAlone()
    {
        // The taskbar embedding anchor also owns WS_EX_NOACTIVATE, so a window whose
        // style moved on after this controller recorded it must not be reverted.
        const long original = 0x00080080;

        Assert.False(NativeWindowInteraction.ShouldRestoreClickThroughStyle(
            original | Transparent | NoActivate,
            original,
            includeNoActivate: false));
        Assert.False(NativeWindowInteraction.ShouldRestoreClickThroughStyle(
            original | Transparent,
            original,
            includeNoActivate: true));
        Assert.False(NativeWindowInteraction.ShouldRestoreClickThroughStyle(
            original | Transparent | NoActivate | 0x01000000,
            original,
            includeNoActivate: true));
    }

    [Fact]
    public void ClickThroughMaskOnlyIncludesActivationSuppressionForTheRootWindow()
    {
        Assert.Equal(Transparent, NativeWindowInteraction.ClickThroughMask(includeNoActivate: false));
        Assert.Equal(Transparent | NoActivate, NativeWindowInteraction.ClickThroughMask(includeNoActivate: true));
    }
}
