using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class EmbeddedTaskbarLayoutCalculatorTests
{
    [Theory]
    [InlineData(LyricsHorizontalAnchor.Left, 0, 0)]
    [InlineData(LyricsHorizontalAnchor.Left, 40, 40)]
    [InlineData(LyricsHorizontalAnchor.Center, 0, 190)]
    [InlineData(LyricsHorizontalAnchor.Right, 0, 380)]
    public void CalculateHorizontalLeftFollowsSharedHorizontalAnchor(
        LyricsHorizontalAnchor anchor,
        double offset,
        double expected)
    {
        var result = EmbeddedTaskbarLayoutCalculator.CalculateHorizontalLeft(420, 40, anchor, offset);

        Assert.Equal(expected, result, 10);
    }

    [Fact]
    public void CalculateVerticalTopCentersWindowInTaskbar()
    {
        Assert.Equal(20, EmbeddedTaskbarLayoutCalculator.CalculateVerticalTop(80, 40, 0));
        Assert.Equal(25, EmbeddedTaskbarLayoutCalculator.CalculateVerticalTop(80, 40, 5));
        Assert.Equal(15, EmbeddedTaskbarLayoutCalculator.CalculateVerticalTop(80, 40, -5));
    }

    [Theory]
    [InlineData(-20, 0)]
    [InlineData(40, 40)]
    [InlineData(120, 80)]
    public void ClampHorizontalLeftKeepsWindowInsideTaskbar(
        double requestedLeft,
        double expectedLeft)
    {
        Assert.Equal(
            expectedLeft,
            EmbeddedTaskbarLayoutCalculator.ClampHorizontalLeft(requestedLeft, 500, 420));
    }

    [Theory]
    [InlineData(-20, 0)]
    [InlineData(2, 2)]
    [InlineData(40, 4)]
    public void ClampVerticalTopKeepsWindowInsideTaskbar(
        double requestedTop,
        double expectedTop)
    {
        Assert.Equal(
            expectedTop,
            EmbeddedTaskbarLayoutCalculator.ClampVerticalTop(requestedTop, 48, 44));
    }

    [Fact]
    public void CalculateIntersectionAreaIdentifiesTaskbarOnTargetDisplay()
    {
        var targetDisplay = new NativeRect(1920, 0, 3840, 1080);

        Assert.Equal(
            76800,
            EmbeddedTaskbarLayoutCalculator.CalculateIntersectionArea(
                new NativeRect(1920, 1040, 3840, 1080),
                targetDisplay));
        Assert.Equal(
            0,
            EmbeddedTaskbarLayoutCalculator.CalculateIntersectionArea(
                new NativeRect(0, 1040, 1920, 1080),
                targetDisplay));
    }

    [Fact]
    public void ToTaskbarClientBoundsConvertsLogicalToPhysicalPixels()
    {
        var bounds = EmbeddedTaskbarLayoutCalculator.ToTaskbarClientBounds(10, 20, 320, 40, 1.5);

        Assert.Equal(15, bounds.Left);
        Assert.Equal(30, bounds.Top);
        Assert.Equal(480, bounds.Width);
        Assert.Equal(60, bounds.Height);
    }

    [Fact]
    public void ToTaskbarClientBoundsTreatsInvalidScaleAsOne()
    {
        var bounds = EmbeddedTaskbarLayoutCalculator.ToTaskbarClientBounds(10, 20, 100, 40, 0);

        Assert.Equal(10, bounds.Left);
        Assert.Equal(20, bounds.Top);
        Assert.Equal(100, bounds.Width);
    }

    [Fact]
    public void NeedsNativeBoundsUpdateSkipsIdenticalPhysicalBounds()
    {
        var bounds = new EmbeddedTaskbarNativeBounds(10, 20, 300, 40);

        Assert.False(EmbeddedTaskbarLayoutCalculator.NeedsNativeBoundsUpdate(bounds, bounds));
    }

    [Fact]
    public void NeedsNativeBoundsUpdateRequiresInitialPositioning()
    {
        var bounds = new EmbeddedTaskbarNativeBounds(10, 20, 300, 40);

        Assert.True(EmbeddedTaskbarLayoutCalculator.NeedsNativeBoundsUpdate(null, bounds));
    }

    [Theory]
    [InlineData(1, 0, 0, 0)]
    [InlineData(0, 1, 0, 0)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(0, 0, 0, 1)]
    public void NeedsNativeBoundsUpdateDetectsAnyPhysicalBoundaryChange(
        int leftDelta,
        int topDelta,
        int widthDelta,
        int heightDelta)
    {
        var previousBounds = new EmbeddedTaskbarNativeBounds(10, 20, 300, 40);
        var targetBounds = new EmbeddedTaskbarNativeBounds(
            previousBounds.Left + leftDelta,
            previousBounds.Top + topDelta,
            previousBounds.Width + widthDelta,
            previousBounds.Height + heightDelta);

        Assert.True(EmbeddedTaskbarLayoutCalculator.NeedsNativeBoundsUpdate(previousBounds, targetBounds));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AttachResultPreservesEstablishedEmbeddingWhenPositioningIsPending(bool positioned)
    {
        var result = EmbeddedTaskbarEmbeddingPolicy.FromPositionResult(positioned);
        var expected = positioned
            ? EmbeddedTaskbarAttachResult.Attached
            : EmbeddedTaskbarAttachResult.AttachedPositionPending;

        Assert.Equal(expected, result);
        Assert.True(EmbeddedTaskbarEmbeddingPolicy.ShouldKeepEmbedded(result));
    }

    [Fact]
    public void UnavailableAttachResultFallsBackToTransparentWindow()
    {
        Assert.False(
            EmbeddedTaskbarEmbeddingPolicy.ShouldKeepEmbedded(
                EmbeddedTaskbarAttachResult.Unavailable));
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public void ExistingAttachmentIsRetainedOnlyForSameValidTarget(
        bool sameWindow,
        bool sameTargetDisplay,
        bool parentIsValid,
        bool expected)
    {
        Assert.Equal(
            expected,
            EmbeddedTaskbarEmbeddingPolicy.ShouldKeepExistingAttachment(
                sameWindow,
                sameTargetDisplay,
                parentIsValid));
    }

}
