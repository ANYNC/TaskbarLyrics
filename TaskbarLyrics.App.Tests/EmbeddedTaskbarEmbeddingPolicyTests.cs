using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class EmbeddedTaskbarEmbeddingPolicyTests
{
    private static DisplayMonitor CreateDisplay(string id) =>
        new(
            id,
            "显示器 1",
            false,
            new NativeRect(0, 0, 1920, 1080),
            new NativeRect(0, 0, 1920, 1040),
            1);

    [Fact]
    public void AttachedPrimaryTaskbarRetargetingSecondaryDisplayRequiresWindowReplacement()
    {
        var attached = EmbeddedTaskbarDisplayTarget.Create(null);
        var secondary = CreateDisplay("display-b");

        Assert.True(EmbeddedTaskbarEmbeddingPolicy.RequiresWindowReplacement(attached, secondary));
    }

    [Fact]
    public void AttachedToTargetDisplaySameDisplayKeepsWindow()
    {
        var display = CreateDisplay("display-b");
        var attached = EmbeddedTaskbarDisplayTarget.Create(display);

        Assert.False(EmbeddedTaskbarEmbeddingPolicy.RequiresWindowReplacement(attached, display));
    }

    [Fact]
    public void AttachedSecondaryDisplayRetargetingPrimaryDisplayRequiresWindowReplacement()
    {
        var secondary = CreateDisplay("display-b");
        var attached = EmbeddedTaskbarDisplayTarget.Create(secondary);
        var primary = CreateDisplay("display-a");

        Assert.True(EmbeddedTaskbarEmbeddingPolicy.RequiresWindowReplacement(attached, primary));
    }

    [Fact]
    public void AttachedPrimaryTaskbarPrimarySemanticsUnchangedKeepsWindow()
    {
        var attached = EmbeddedTaskbarDisplayTarget.Create(null);

        Assert.False(EmbeddedTaskbarEmbeddingPolicy.RequiresWindowReplacement(attached, null));
    }

    [Fact]
    public void AttachedSecondaryDisplayTargetFallsBackToPrimaryTaskbarRequiresWindowReplacement()
    {
        var secondary = CreateDisplay("display-b");
        var attached = EmbeddedTaskbarDisplayTarget.Create(secondary);

        Assert.True(EmbeddedTaskbarEmbeddingPolicy.RequiresWindowReplacement(attached, null));
    }

    [Fact]
    public void NoEstablishedAttachmentNeverRequiresWindowReplacement()
    {
        var display = CreateDisplay("display-b");

        Assert.False(EmbeddedTaskbarEmbeddingPolicy.RequiresWindowReplacement(null, display));
        Assert.False(EmbeddedTaskbarEmbeddingPolicy.RequiresWindowReplacement(null, null));
    }
}
