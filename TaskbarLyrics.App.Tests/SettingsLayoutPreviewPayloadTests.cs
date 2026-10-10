using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class SettingsLayoutPreviewPayloadTests
{
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
