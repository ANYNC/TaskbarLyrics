using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class CompactTaskbarLayoutTests
{
    [Theory]
    [InlineData(false, 1, 70)]
    [InlineData(false, 1.5, 105)]
    [InlineData(true, 1.25, 100)]
    public void SingleLineEnlargesOnlyTheDisplayedFontAndKeepsBaseSettings(bool floating, double dpi, double scale)
    {
        var settings = new AppSettings
        {
            UseFloatingWindow = floating,
            LyricsLayoutScalePercent = scale,
            FloatingLyricsLayoutScalePercent = scale
        };
        var doubleLine = LyricsLayoutMetrics.Create(settings, dpi);
        settings.SingleLineLyrics = true;
        var singleLine = LyricsLayoutMetrics.Create(settings, dpi);
        Assert.Equal(doubleLine.FontSize * 1.4, singleLine.FontSize, 6);
        Assert.Equal(doubleLine.CoverSize, singleLine.CoverSize);
        Assert.Equal(doubleLine.ScalePercent, singleLine.ScalePercent);
        Assert.Equal(14, settings.FontSize);
        Assert.Equal(34, settings.CoverSize);
        var payload = SettingsWindow.CreateLyricsLayoutPreviewPayload(settings, singleLine,
            new TaskbarEmbeddingConstraints(1920, 48), 1920, true);
        Assert.Equal(singleLine.FontSize, payload["effectiveFontSize"]);
    }

    [Theory]
    [InlineData(1, 32, 1920)]
    [InlineData(1.5, 32, 1920)]
    [InlineData(2, 1080, 48)]
    public void LargerSingleLineFontStillFitsCompactTaskbarsAndSidebars(double dpi, double height, double width)
    {
        var settings = new AppSettings { SingleLineLyrics = true, LyricsLayoutScalePercent = 300 };
        var metrics = LyricsLayoutMetrics.Create(settings, dpi, new TaskbarBounds(0, 0, width, height));
        if (metrics.IsVertical)
        {
            Assert.True(metrics.FontSize * 1.12 + metrics.LineTextBottomPadding + metrics.LyricsPaneLeftPadding +
                metrics.LyricsPaneRightPadding + 2 * metrics.HostHorizontalPadding <= width + 0.01);
        }
        else
        {
            Assert.True(metrics.FontSize * 1.12 + metrics.PrimaryOffsetY + metrics.LineTextBottomPadding +
                metrics.LyricsPaneTopPadding + 2 * metrics.HostVerticalPadding <= height + 0.01);
        }
    }

    [Theory]
    [InlineData(1, false, true)]
    [InlineData(1.25, true, true)]
    [InlineData(1.5, false, false)]
    [InlineData(2, true, false)]
    public void ShortTaskbarLimitsZoomBeforeTheRendererFitsItsOutput(double dpi, bool singleLine, bool cover)
    {
        var settings = new AppSettings { SingleLineLyrics = singleLine, ShowCover = cover };
        var constraints = new TaskbarEmbeddingConstraints(1920, 32)
        {
            Displays = [new TaskbarEmbeddingDisplayConstraint(1920, 32, dpi)]
        };
        var bounds = TaskbarEmbeddingLayoutPolicy.GetInputBounds(settings, constraints, 1920);
        Assert.InRange(bounds.MaxScalePercent, AppSettings.MinimumLyricsLayoutScalePercent, 99);
        Assert.True(bounds.MaxFontSize >= settings.FontSize);
        settings.LyricsLayoutScalePercent = bounds.MaxScalePercent;
        Assert.True(TaskbarEmbeddingLayoutPolicy.CanFit(settings, constraints));
        settings.LyricsLayoutScalePercent++;
        Assert.False(TaskbarEmbeddingLayoutPolicy.CanFit(settings, constraints));
        settings.LyricsLayoutScalePercent = 300;
        TaskbarEmbeddingLayoutPolicy.ClampToConstraints(settings, constraints);
        Assert.Equal(bounds.MaxScalePercent, settings.LyricsLayoutScalePercent);
        Assert.Equal(AppSettings.DefaultFontSize, settings.FontSize);
        Assert.Equal(AppSettings.DefaultCoverSize, settings.CoverSize);
        Assert.True(TaskbarEmbeddingLayoutPolicy.CanFit(settings, constraints));
        var payload = SettingsWindow.CreateLyricsLayoutPreviewPayload(settings,
            LyricsLayoutMetrics.Create(settings, dpi, new TaskbarBounds(0, 0, 1920, 32)), constraints, 1920, true);
        Assert.Equal(bounds.MaxScalePercent, payload["taskbarMaxScalePercent"]);
    }

    [Fact]
    public void SingleLineWithoutArtworkAllowsMoreZoomThanTwoLines()
    {
        var settings = new AppSettings { ShowCover = false };
        var constraints = new TaskbarEmbeddingConstraints(1920, 32);
        var doubleLine = TaskbarEmbeddingLayoutPolicy.GetInputBounds(settings, constraints, 1920);
        settings.SingleLineLyrics = true;
        var singleLine = TaskbarEmbeddingLayoutPolicy.GetInputBounds(settings, constraints, 1920);
        Assert.True(singleLine.MaxScalePercent > doubleLine.MaxScalePercent);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1.25, true)]
    [InlineData(1.5, false)]
    [InlineData(2, true)]
    public void ShortTaskbarFitsArtworkAndChosenRowsWithoutChangingSavedSizes(double dpi, bool singleLine)
    {
        var settings = new AppSettings { SingleLineLyrics = singleLine, LyricsLayoutScalePercent = 150 };
        var saved = settings.Clone();
        var metrics = LyricsLayoutMetrics.Create(settings, dpi, new TaskbarBounds(-1920, 1048, 1920, 32));
        Assert.Equal(singleLine, metrics.IsSingleLine);
        Assert.False(metrics.IsVertical);
        Assert.Equal(32, metrics.DesiredWindowHeight);
        Assert.Equal(0, metrics.MinimumContentHeight);
        Assert.InRange(metrics.CoverSize + 2 * metrics.HostVerticalPadding, 0, 32);
        var rows = singleLine ? 1 : 2;
        Assert.True((metrics.FontSize * 1.12 + metrics.PrimaryOffsetY + metrics.LineTextBottomPadding) * rows +
            metrics.LyricsPaneTopPadding + 2 * metrics.HostVerticalPadding <= 32.01);
        Assert.Equal(saved.FontSize, settings.FontSize);
        Assert.Equal(saved.CoverSize, settings.CoverSize);
        Assert.Equal(saved.LyricsLayoutScalePercent, settings.LyricsLayoutScalePercent);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(1.25, false)]
    [InlineData(1.5, true)]
    [InlineData(2, false)]
    public void SidebarStacksArtworkAndTextWithinItsActualWidth(double dpi, bool cover)
    {
        var settings = new AppSettings { CoverSize = 100, ShowCover = cover };
        var metrics = LyricsLayoutMetrics.Create(settings, dpi, new TaskbarBounds(-48, 0, 48, 1080));
        Assert.True(metrics.IsVertical);
        Assert.InRange(metrics.CoverSize + 2 * metrics.HostHorizontalPadding, 0, 48);
        Assert.InRange(metrics.DesiredWindowHeight, 1, 1080);
        Assert.Equal(settings.WindowWidth, metrics.DesiredWindowHeight);
        Assert.Equal(100, settings.CoverSize);
        Assert.Equal(cover, settings.ShowCover);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FloatingWindowKeepsItsLayoutAndChosenLineCount(bool singleLine)
    {
        var settings = new AppSettings { UseFloatingWindow = true, SingleLineLyrics = singleLine };
        Assert.Equal(LyricsLayoutMetrics.Create(settings, 1.5),
            LyricsLayoutMetrics.Create(settings, 1.5, new TaskbarBounds(0, 0, 48, 1080)));
        Assert.Equal(singleLine, LyricsLayoutMetrics.Create(settings).IsSingleLine);
    }

    [Fact]
    public void EmbeddedSettingsReadoutUsesTheFittedTaskbarSizesWhileFloatingIsActive()
    {
        var settings = new AppSettings { UseFloatingWindow = true, SingleLineLyrics = true, LyricsLayoutScalePercent = 150 };
        var metrics = SettingsWindow.CreateEmbeddedLayoutPreviewMetrics(settings, 1.5, new TaskbarBounds(0, 0, 1920, 32));
        Assert.InRange(metrics.CoverSize, 1, 26);
        Assert.True(metrics.FontSize < settings.FontSize * 1.5);
        Assert.Equal(32, metrics.DesiredWindowHeight);
        Assert.True(settings.UseFloatingWindow);
    }

    [Fact]
    public void SidebarDoesNotResetHorizontalPreferencesOrForceFloatingAtStartup()
    {
        var settings = new AppSettings { WindowWidth = 900, LyricsLayoutScalePercent = 150 };
        var display = new DisplayMonitor("side", "Side", true,
            new NativeRect(-1920, 0, 0, 1080), new NativeRect(-1872, 0, 0, 1080), 1);
        var constraints = TaskbarEmbeddingLayoutPolicy.FromDisplay(display);
        Assert.True(constraints.Displays.Single().IsVertical);
        Assert.True(TaskbarEmbeddingLayoutPolicy.CanFit(settings, constraints));
        Assert.False(App.NormalizeStartupWindowPresentation(settings, constraints, out _));
        TaskbarEmbeddingLayoutPolicy.ClampToConstraints(settings, constraints);
        Assert.False(settings.UseFloatingWindow);
        Assert.Equal(900, settings.WindowWidth);
        Assert.Equal(150, settings.LyricsLayoutScalePercent);
    }

    [Fact]
    public void SidebarWindowLengthSetsItsHeightAndAvailableVerticalOffset()
    {
        var settings = new AppSettings { WindowWidth = 300 };
        var display = new DisplayMonitor("side", "Side", true,
            new NativeRect(0, 0, 1920, 900), new NativeRect(48, 0, 1920, 900), 1);
        var constraints = TaskbarEmbeddingLayoutPolicy.FromDisplay(display);
        var bounds = TaskbarEmbeddingLayoutPolicy.GetInputBounds(settings, constraints, 1920);
        var metrics = LyricsLayoutMetrics.Create(settings, 1, new TaskbarBounds(0, 0, 48, 900));
        Assert.Equal(300, metrics.DesiredWindowHeight);
        Assert.Equal(900, bounds.MaxWindowWidth);
        Assert.Equal(0, bounds.MinYOffset);
        Assert.Equal(600, bounds.MaxYOffset);
        settings.WindowWidth = 600;
        bounds = TaskbarEmbeddingLayoutPolicy.GetInputBounds(settings, constraints, 1920);
        Assert.Equal(0, bounds.MinYOffset);
        Assert.Equal(300, bounds.MaxYOffset);
    }

    [Fact]
    public void LineCountUpdatesBothHostLayoutAndWebStyleWithoutRebuildingLyricServices()
    {
        var settings = new AppSettings();
        var next = settings.Clone();
        next.SingleLineLyrics = true;
        var changes = AppSettingsChangeSet.Create(settings, next);
        Assert.True(changes.LyricsLayoutChanged);
        Assert.True(changes.VisualStyleChanged);
        Assert.False(changes.LyricSyncServiceChanged);
        var script = LyricsStyleScriptFactory.Create(next, 1.5, new TaskbarBounds(0, 0, 48, 1080));
        Assert.Contains("\"singleLine\":true", script);
        Assert.Contains("\"verticalTaskbar\":true", script);
    }
}
