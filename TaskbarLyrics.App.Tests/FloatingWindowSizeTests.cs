using System.IO;
using System.Windows;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class FloatingWindowSizeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void ScreenDisplacementIsConvertedToDipsAndAlwaysUsesTheInitialGesture(double dpi)
    {
        var start = new Point(-600, -120);
        var gesture = new FloatingWindowSizeGesture(new(420, 100), start, dpi, 420, 1920);
        var point = new Point(start.X + 80 * dpi, start.Y - 25 * dpi);
        var result = gesture.Calculate(point);
        Assert.Equal(125, result.ScalePercent);
        Assert.Equal(500, EffectiveWidth(result));
        Assert.Equal(gesture.Calculate(point), gesture.Calculate(point));
        Assert.Equal(new FloatingWindowSize(420, 100), gesture.Calculate(start));
    }

    [Theory]
    [InlineData(60, 0, 480, 150)]
    [InlineData(0, -20, 420, 170)]
    [InlineData(-60, 20, 360, 130)]
    public void HorizontalChangesWidthAndVerticalChangesScale(double dx, double dy, double width, double scale)
    {
        var gesture = new FloatingWindowSizeGesture(new(420, 150), new Point(), 1, 420, 1920);
        var result = gesture.Calculate(new Point(dx, dy));
        Assert.Equal(width, EffectiveWidth(result), 6);
        Assert.Equal(scale, result.ScalePercent);
    }

    [Theory]
    [InlineData(-5000, 5000, 100, 25)]
    [InlineData(5000, 0, 800, 100)]
    [InlineData(0, -5000, 420, 300)]
    public void WidthAndScaleStayWithinSettingsAndWorkAreaLimits(double dx, double dy, double width, double scale)
    {
        var gesture = new FloatingWindowSizeGesture(new(420, 100), new Point(), 1, 420, 800);
        var result = gesture.Calculate(new Point(dx, dy));
        Assert.Equal(width, result.Width, 6);
        Assert.Equal(scale, result.ScalePercent);
        Assert.InRange(EffectiveWidth(result), 0, 800);
    }

    [Fact]
    public void InvalidCursorCoordinatesDoNotChangeTheSize()
    {
        var original = new FloatingWindowSize(420, 100);
        var gesture = new FloatingWindowSizeGesture(original, new Point(), 1, 420, 1920);
        Assert.Equal(original, gesture.Calculate(new Point(double.NaN, 0)));
        Assert.Equal(original, gesture.Calculate(new Point(0, double.PositiveInfinity)));
    }

    [Theory]
    [InlineData(420, 100, -5000, 300)]
    [InlineData(420, 100, 5000, 25)]
    [InlineData(1400, 100, 5000, 25)]
    [InlineData(100, 100, -5000, 300)]
    public void VerticalZoomAlwaysKeepsTheVisibleWidthIncludingSettingsLimits(double width, double initialScale, double dy, double expectedScale)
    {
        var gesture = new FloatingWindowSizeGesture(new(width, initialScale), new Point(), 1, width, 1920);
        var result = gesture.Calculate(new Point(0, dy));
        Assert.Equal(width, EffectiveWidth(result), 6);
        Assert.Equal(expectedScale, result.ScalePercent);
        Assert.InRange(result.Width, AppSettings.MinimumWindowWidth, AppSettings.MaximumWindowWidth);
        var loaded = result.ApplyTo(new AppSettings());
        loaded.NormalizeLyricsLayout();
        loaded.NormalizeWindowLayout();
        Assert.Equal(width, EffectiveWidth(FloatingWindowSize.FromSettings(loaded)), 6);
    }

    [Fact]
    public void ConsecutiveZoomGesturesAndCombinedDraggingDoNotAccumulateWidthDrift()
    {
        var initial = new FloatingWindowSize(420, 100);
        var enlarged = new FloatingWindowSizeGesture(initial, new Point(), 1.25, 420, 1920).Calculate(new Point(0, -31.25));
        var combined = new FloatingWindowSizeGesture(enlarged, new Point(), 1.25, EffectiveWidth(enlarged), 1920)
            .Calculate(new Point(100, 31.25));
        Assert.Equal(500, EffectiveWidth(combined), 6);
        Assert.Equal(100, combined.ScalePercent);
    }

    private static double EffectiveWidth(FloatingWindowSize size) =>
        AppSettings.CalculateEffectiveWindowWidth(size.Width);

    [Fact]
    public void VerticalZoomKeepsTheRequestedWidthEvenWhenTheMonitorClampsItsVisibleWidth()
    {
        var gesture = new FloatingWindowSizeGesture(new(1400, 100), new Point(), 1, 800, 800);
        Assert.Equal(new FloatingWindowSize(1400, 125), gesture.Calculate(new Point(0, -25)));
    }

    [Fact]
    public void CommittingSizeMergesOnlyItsFieldsAndRoundTripsExistingSettings()
    {
        var original = new AppSettings { WindowWidth = 700, FloatingWindowWidth = 420, LyricsLayoutScalePercent = 100, XOffset = 57, UseFloatingWindow = true };
        var changed = new FloatingWindowSize(520, 135).ApplyTo(original);
        Assert.Equal(700, original.WindowWidth);
        Assert.Equal(420, original.FloatingWindowWidth);
        Assert.Equal(700, changed.WindowWidth);
        Assert.Equal(520, changed.FloatingWindowWidth);
        Assert.Equal(100, original.LyricsLayoutScalePercent);
        Assert.Equal(100, changed.LyricsLayoutScalePercent);
        Assert.Equal(135, changed.FloatingLyricsLayoutScalePercent);
        Assert.Equal(57, changed.XOffset);
        Assert.True(changed.UseFloatingWindow);
        var directory = Path.Combine(AppContext.BaseDirectory, $"floating-size-{Guid.NewGuid():N}");
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        try
        {
            Assert.True(store.Save(changed));
            var loaded = store.Load();
            Assert.Equal(new FloatingWindowSize(520, 135), FloatingWindowSize.FromSettings(loaded));
            Assert.Equal(700, loaded.WindowWidth);
            Assert.Equal(57, loaded.XOffset);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
