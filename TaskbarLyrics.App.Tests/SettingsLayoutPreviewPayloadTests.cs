using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class SettingsLayoutPreviewPayloadTests
{
    [Theory]
    [InlineData(100, 1)]
    [InlineData(100, 1.25)]
    [InlineData(125, 1.5)]
    [InlineData(125, 2)]
    public void FloatingResizesKeepEmbeddedPreviewMetricsAndBothSavedScalesIndependent(double embeddedScale, double dpi)
    {
        var settings = new AppSettings { LyricsLayoutScalePercent = embeddedScale };
        var expected = SettingsWindow.CreateEmbeddedLayoutPreviewMetrics(settings, dpi);
        settings.UseFloatingWindow = true;
        foreach (var floatingScale in new[] { 160d, 210d, 250d })
        {
            settings = new FloatingWindowSize(600, floatingScale).ApplyTo(settings);
            var metrics = SettingsWindow.CreateEmbeddedLayoutPreviewMetrics(settings, dpi);
            Assert.Equal(expected, metrics);
            Assert.Equal(floatingScale, LyricsLayoutMetrics.Create(settings, dpi).ScalePercent);
            Assert.True(settings.UseFloatingWindow);
            Assert.Equal(embeddedScale, settings.LyricsLayoutScalePercent);
            Assert.Equal(floatingScale, settings.FloatingLyricsLayoutScalePercent);
            foreach (var includeBounds in new[] { false, true })
            {
                var preview = SettingsWindow.CreateLyricsLayoutPreviewPayload(settings, metrics,
                    new TaskbarEmbeddingConstraints(1000, 80), 1920, includeBounds);
                Assert.Equal(embeddedScale, preview["scalePercent"]);
                Assert.Equal(floatingScale, preview["floatingLyricsLayoutScalePercent"]);
                Assert.Equal(expected.FontSize, preview["effectiveFontSize"]);
                Assert.Equal(expected.CoverSize, preview["effectiveCoverSize"]);
                Assert.Equal(expected.CoverGap, preview["effectiveCoverGap"]);
                Assert.Equal(expected.CoverCornerRadius, preview["effectiveCoverCornerRadius"]);
            }
        }
        settings.UseFloatingWindow = false;
        Assert.Equal(expected, SettingsWindow.CreateEmbeddedLayoutPreviewMetrics(settings, dpi));
    }

    [Theory]
    [InlineData(false, 300)]
    [InlineData(true, 600)]
    public void WidthPreviewUsesTheCurrentPresentationsSavedWidth(bool floating, double expected)
    {
        var settings = new AppSettings { UseFloatingWindow = floating, WindowWidth = 300, FloatingWindowWidth = 600 };
        var payload = SettingsWindow.CreateLyricsLayoutPreviewPayload(settings, LyricsLayoutMetrics.Create(settings),
            new TaskbarEmbeddingConstraints(500, 48), fallbackWidth: 1920, includeInputBounds: true);
        Assert.Equal(expected, Assert.IsType<double>(payload["effectiveWindowWidth"]));
        Assert.Equal(600d, payload["floatingWindowWidth"]);
        Assert.Equal(expected, settings.GetWindowWidth());
    }

    [Fact]
    public void LivePreviewKeepsMetricsWithoutRecalculatingInputBounds()
    {
        var settings = new AppSettings();
        var metrics = LyricsLayoutMetrics.Create(settings);
        var constraints = new TaskbarEmbeddingConstraints(500, 48);

        var live = SettingsWindow.CreateLyricsLayoutPreviewPayload(
            settings, metrics, constraints, fallbackWidth: 500, includeInputBounds: false);
        var full = SettingsWindow.CreateLyricsLayoutPreviewPayload(
            settings, metrics, constraints, fallbackWidth: 500, includeInputBounds: true);

        Assert.Equal(metrics.ScalePercent, live["scalePercent"]);
        Assert.Equal(metrics.FontSize, live["effectiveFontSize"]);
        Assert.Equal(full["effectiveWindowWidth"], live["effectiveWindowWidth"]);
        Assert.DoesNotContain("taskbarMaxScalePercent", live.Keys);
        Assert.Contains("taskbarMaxScalePercent", full.Keys);

        var script = WebViewMessageScriptFactory.Dispatch("settingsApp", "lyricsLayoutPreview", live);
        Assert.Contains("\"effectiveFontSize\"", script);
        Assert.DoesNotContain("\"taskbarMaxScalePercent\"", script);
    }
}
