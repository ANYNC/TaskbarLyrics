using System.IO;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class TaskbarScaleTransitionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void ResizingTaskbarScalesSingleLineProportionallyAndRepeatedNotificationsDoNotCompound(double dpi)
    {
        var settings = new AppSettings { SingleLineLyrics = true, LyricsLayoutScalePercent = 70 };
        var compact = Constraints(32, dpi);
        var normal = Constraints(48, dpi);
        settings.LyricsLayoutScalePercent = TaskbarEmbeddingLayoutPolicy.ScaleForTaskbarHeightChange(settings, compact, normal);
        Assert.Equal(105, settings.LyricsLayoutScalePercent, 6);
        Assert.Equal(105, TaskbarEmbeddingLayoutPolicy.ScaleForTaskbarHeightChange(settings, normal, normal), 6);
        Assert.Equal(70, TaskbarEmbeddingLayoutPolicy.ScaleForTaskbarHeightChange(settings, normal, compact), 6);
        Assert.Equal(14, settings.FontSize);
        Assert.Equal(34, settings.CoverSize);
        Assert.Equal(100, settings.FloatingLyricsLayoutScalePercent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GrowthRespectsNewTaskbarLimitWithoutChangingTheOriginalSettings(bool floating)
    {
        var settings = new AppSettings { UseFloatingWindow = floating, SingleLineLyrics = true, LyricsLayoutScalePercent = 100 };
        var normal = Constraints(48);
        var scale = TaskbarEmbeddingLayoutPolicy.ScaleForTaskbarHeightChange(settings, Constraints(32), normal);
        Assert.InRange(scale, 100, 149);
        var candidate = settings.Clone();
        candidate.LyricsLayoutScalePercent = scale;
        Assert.True(TaskbarEmbeddingLayoutPolicy.CanFit(candidate, normal));
        candidate.LyricsLayoutScalePercent++;
        Assert.False(TaskbarEmbeddingLayoutPolicy.CanFit(candidate, normal));
        Assert.Equal(100, settings.LyricsLayoutScalePercent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedDisplaysUseTheSmallestHorizontalTaskbarAndIgnoreSidebarHeight(bool floating)
    {
        var settings = new AppSettings { UseFloatingWindow = floating, SingleLineLyrics = true, LyricsLayoutScalePercent = 70 };
        var compact = Constraints(32) with { Displays = [new(1920, 32, 1), new(2560, 48, 1.5), new(48, 1080, 1) { IsVertical = true }] };
        var normal = compact with { Displays = [new(1920, 48, 1), new(2560, 48, 1.5), new(48, 1080, 1) { IsVertical = true }] };
        Assert.Equal(105, TaskbarEmbeddingLayoutPolicy.ScaleForTaskbarHeightChange(settings, compact, normal), 6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingGeometryAndOrientationChangesKeepExistingScale(bool floating)
    {
        var settings = new AppSettings { UseFloatingWindow = floating, SingleLineLyrics = true, LyricsLayoutScalePercent = 70 };
        var compact = Constraints(32);
        var normal = Constraints(48);
        var sidebar = new TaskbarEmbeddingConstraints(48, 1080) { Displays = [new(48, 1080, 1) { IsVertical = true }] };
        Assert.Equal(70, TaskbarEmbeddingLayoutPolicy.ScaleForTaskbarHeightChange(settings, null, normal));
        Assert.Equal(70, TaskbarEmbeddingLayoutPolicy.ScaleForTaskbarHeightChange(settings, default(TaskbarEmbeddingConstraints), normal));
        Assert.Equal(70, TaskbarEmbeddingLayoutPolicy.ScaleForTaskbarHeightChange(settings, compact, default));
        Assert.Equal(70, TaskbarEmbeddingLayoutPolicy.ScaleForTaskbarHeightChange(settings, sidebar, normal));
        Assert.Equal(70, TaskbarEmbeddingLayoutPolicy.ScaleForTaskbarHeightChange(settings, compact, sidebar));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 1.25)]
    [InlineData(false, 1.5)]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    [InlineData(true, 1.25)]
    [InlineData(true, 1.5)]
    [InlineData(true, 2)]
    public void EmbeddedScaleRecoversWhenTaskbarReturnsToNormalWhileFloating(bool singleLine, double dpi)
    {
        var normal = Constraints(48, dpi);
        var compact = Constraints(32, dpi);
        var settings = new AppSettings
        {
            SingleLineLyrics = singleLine,
            LyricsLayoutScalePercent = 105,
            FloatingLyricsLayoutScalePercent = 145
        };
        settings.LyricsLayoutScalePercent = TaskbarEmbeddingLayoutPolicy.ScaleForTaskbarHeightChange(settings, normal, compact);
        Assert.Equal(70, settings.LyricsLayoutScalePercent, 6);
        var floating = App.CreateDisplayModeSettings(settings, true, compact, out _);
        var adjusted = floating.Clone();
        adjusted.LyricsLayoutScalePercent = TaskbarEmbeddingLayoutPolicy.ScaleForTaskbarHeightChange(floating, compact, normal);

        Assert.Equal(105, adjusted.LyricsLayoutScalePercent, 6);
        Assert.Equal(70, floating.LyricsLayoutScalePercent, 6);
        Assert.Equal(145, adjusted.GetLyricsLayoutScalePercent());
        Assert.Equal(105, TaskbarEmbeddingLayoutPolicy.ScaleForTaskbarHeightChange(adjusted, normal, normal), 6);
        Assert.False(AppSettingsChangeSet.Create(floating, adjusted).RequiresLyricsWindowApply);

        var embedded = App.CreateDisplayModeSettings(adjusted, false, normal, out var message);
        Assert.False(embedded.UseFloatingWindow);
        Assert.Equal(105, embedded.GetLyricsLayoutScalePercent(), 6);
        Assert.Equal(145, embedded.FloatingLyricsLayoutScalePercent);
        Assert.Equal(string.Empty, message);
    }

    [Fact]
    public void TaskbarShrinkingWhileFloatingAlsoAdaptsOnlyTheEmbeddedScale()
    {
        var normal = Constraints(48);
        var compact = Constraints(32);
        var settings = new AppSettings
        {
            UseFloatingWindow = true,
            LyricsLayoutScalePercent = 105,
            FloatingLyricsLayoutScalePercent = 145
        };

        settings.LyricsLayoutScalePercent = TaskbarEmbeddingLayoutPolicy.ScaleForTaskbarHeightChange(settings, normal, compact);
        Assert.Equal(70, settings.LyricsLayoutScalePercent, 6);
        Assert.Equal(145, settings.GetLyricsLayoutScalePercent());
        var embedded = App.CreateDisplayModeSettings(settings, false, compact, out _);
        Assert.False(embedded.UseFloatingWindow);
        Assert.Equal(70, embedded.GetLyricsLayoutScalePercent(), 6);
        Assert.True(TaskbarEmbeddingLayoutPolicy.CanFit(embedded, compact));
    }

    [Fact]
    public void UpdatedScalePersistsUsingTheExistingFieldAndRequiresLayoutAndStyleRefresh()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"taskbar-scale-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var previous = new AppSettings { SingleLineLyrics = true, LyricsLayoutScalePercent = 70 };
            var settings = previous.Clone();
            settings.LyricsLayoutScalePercent = TaskbarEmbeddingLayoutPolicy.ScaleForTaskbarHeightChange(settings, Constraints(32), Constraints(48));
            var changes = AppSettingsChangeSet.Create(previous, settings);
            Assert.True(changes.LyricsLayoutChanged);
            Assert.True(changes.VisualStyleChanged);
            var store = new SettingsStore(path);
            Assert.True(store.Save(settings));
            var loaded = store.Load();
            Assert.Equal(105, loaded.LyricsLayoutScalePercent);
            Assert.True(loaded.SingleLineLyrics);
            Assert.Equal(previous.FontSize, loaded.FontSize);
            Assert.Equal(previous.CoverSize, loaded.CoverSize);
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    private static TaskbarEmbeddingConstraints Constraints(double height, double dpi = 1) =>
        TaskbarEmbeddingLayoutPolicy.FromDisplay(new DisplayMonitor("test", "Test", true,
            new NativeRect(0, 0, (int)(1920 * dpi), (int)(1080 * dpi)),
            new NativeRect(0, 0, (int)(1920 * dpi), (int)((1080 - height) * dpi)), dpi));
}
