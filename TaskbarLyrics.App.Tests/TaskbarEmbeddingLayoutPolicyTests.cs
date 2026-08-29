using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class TaskbarEmbeddingLayoutPolicyTests
{
    [Theory]
    [InlineData(LyricsHorizontalAnchor.Left, 0, 80)]
    [InlineData(LyricsHorizontalAnchor.Center, -40, 40)]
    [InlineData(LyricsHorizontalAnchor.Right, -80, 0)]
    public void InputBoundsCalculateHorizontalOffsetsFromAnchor(
        LyricsHorizontalAnchor anchor,
        double expectedMinimum,
        double expectedMaximum)
    {
        var settings = new AppSettings
        {
            HorizontalAnchor = anchor,
            WindowWidth = 420
        };

        var bounds = TaskbarEmbeddingLayoutPolicy.GetInputBounds(
            settings,
            new TaskbarEmbeddingConstraints(500, 48),
            fallbackWidth: 420);

        Assert.True(bounds.IsSupported);
        Assert.Equal(expectedMinimum, bounds.MinXOffset);
        Assert.Equal(expectedMaximum, bounds.MaxXOffset);
        Assert.Equal(-2, bounds.MinYOffset);
        Assert.Equal(2, bounds.MaxYOffset);
    }

    [Fact]
    public void NormalizeForEmbeddingResetsAnOverflowingLayoutToSafeSharedValues()
    {
        var settings = new AppSettings
        {
            FontSize = AppSettings.ExtendedFontSizeMax,
            CoverSize = AppSettings.ExtendedCoverSizeMax,
            CoverGap = AppSettings.CoverGapMax,
            CoverCornerRadius = AppSettings.ExtendedCoverSizeMax / 2,
            LyricsLayoutScalePercent = AppSettings.MaximumLyricsLayoutScalePercent,
            WindowWidth = AppSettings.MaximumWindowWidth,
            XOffset = AppSettings.MaximumWindowOffset,
            YOffset = AppSettings.MaximumWindowOffset
        };

        var result = TaskbarEmbeddingLayoutPolicy.NormalizeForEmbedding(
            settings,
            new TaskbarEmbeddingConstraints(500, 48));

        Assert.True(result.CanEmbed);
        Assert.True(result.Changed);
        Assert.NotEmpty(result.Message);
        Assert.Equal(AppSettings.DefaultFontSize, settings.FontSize);
        Assert.Equal(AppSettings.DefaultCoverSize, settings.CoverSize);
        Assert.Equal(AppSettings.DefaultCoverGap, settings.CoverGap);
        Assert.Equal(AppSettings.DefaultCoverCornerRadius, settings.CoverCornerRadius);
        Assert.Equal(0, settings.XOffset);
        Assert.Equal(0, settings.YOffset);
        Assert.InRange(
            settings.LyricsLayoutScalePercent,
            AppSettings.MinimumLyricsLayoutScalePercent,
            AppSettings.DefaultLyricsLayoutScalePercent + 20);
        Assert.True(TaskbarEmbeddingLayoutPolicy.CanFit(
            settings,
            new TaskbarEmbeddingConstraints(500, 48)));
    }

    [Fact]
    public void NormalizeForEmbeddingRejectsTaskbarThatCannotFitMinimumWindow()
    {
        var settings = new AppSettings
        {
            WindowWidth = 900,
            XOffset = 120,
            YOffset = -80
        };

        var result = TaskbarEmbeddingLayoutPolicy.NormalizeForEmbedding(
            settings,
            new TaskbarEmbeddingConstraints(300, 48));

        Assert.False(result.CanEmbed);
        Assert.False(result.Changed);
        Assert.Equal(900, settings.WindowWidth);
        Assert.Equal(120, settings.XOffset);
        Assert.Equal(-80, settings.YOffset);
    }

    [Fact]
    public void StartupFallsBackToFloatingWhenTaskbarCannotFitMinimumLayout()
    {
        var settings = new AppSettings();

        var changed = App.NormalizeStartupWindowPresentation(
            settings,
            new TaskbarEmbeddingConstraints(300, 48),
            out var message);

        Assert.True(changed);
        Assert.True(settings.UseFloatingWindow);
        Assert.NotEmpty(message);
    }

    [Fact]
    public void StartupLeavesFloatingPresentationUnchanged()
    {
        var settings = new AppSettings
        {
            UseFloatingWindow = true,
            WindowWidth = 900
        };

        var changed = App.NormalizeStartupWindowPresentation(
            settings,
            new TaskbarEmbeddingConstraints(300, 48),
            out var message);

        Assert.False(changed);
        Assert.True(settings.UseFloatingWindow);
        Assert.Equal(900, settings.WindowWidth);
        Assert.Empty(message);
    }

    [Fact]
    public void CanFitRejectsOffsetsThatPlaceTheWindowOutsideTheTaskbar()
    {
        var settings = new AppSettings
        {
            WindowWidth = 420,
            XOffset = 1000,
            YOffset = 1000
        };

        Assert.False(TaskbarEmbeddingLayoutPolicy.CanFit(
            settings,
            new TaskbarEmbeddingConstraints(500, 48)));
    }

    [Fact]
    public void ClampToConstraintsPreservesAnAlreadyValidSmallerLayout()
    {
        var settings = new AppSettings
        {
            LyricsLayoutScalePercent = 50,
            WindowWidth = 420,
            XOffset = 10,
            YOffset = 0
        };

        TaskbarEmbeddingLayoutPolicy.ClampToConstraints(
            settings,
            new TaskbarEmbeddingConstraints(500, 48));

        Assert.Equal(50, settings.LyricsLayoutScalePercent);
        Assert.Equal(420, settings.WindowWidth);
        Assert.Equal(10, settings.XOffset);
        Assert.Equal(0, settings.YOffset);
    }

    [Fact]
    public void FromDisplaysUsesTheStrictestLogicalTaskbarBoundsAcrossDpiScales()
    {
        var displays = new[]
        {
            new DisplayMonitor(
                "display-1",
                "Display 1",
                true,
                new NativeRect(0, 0, 1920, 1080),
                new NativeRect(0, 0, 1920, 1032),
                1),
            new DisplayMonitor(
                "display-2",
                "Display 2",
                false,
                new NativeRect(1920, 0, 4480, 1440),
                new NativeRect(1920, 0, 4224, 1440),
                2)
        };

        var constraints = TaskbarEmbeddingLayoutPolicy.FromDisplays(displays);

        Assert.True(constraints.IsSupported);
        Assert.Equal(128, constraints.MaxWidth);
        Assert.Equal(48, constraints.MaxHeight);
        Assert.Equal(2, constraints.Displays.Count);
        Assert.False(TaskbarEmbeddingLayoutPolicy.CanFit(new AppSettings(), constraints));
    }
}
