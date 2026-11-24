using System.Windows;
using Xunit;
using Drawing = System.Drawing;

namespace TaskbarLyrics.App.Tests;

public sealed class AdaptiveLyricForegroundTests
{
    [Fact]
    public void BackgroundModePreservesTheStoredColorOnStartup()
    {
        var settings = new AppSettings
        {
            ForegroundColorMode = ForegroundColorMode.Background,
            ForegroundColor = "#FF336699"
        };

        Assert.False(ForegroundColorPolicy.ApplyStartup(settings, systemUsesLightTheme: true));
        Assert.Equal(ForegroundColorMode.Background, settings.ForegroundColorMode);
        Assert.Equal("#FF336699", settings.ForegroundColor);
    }

    [Fact]
    public void WaitsForAStableChangeAndFallsBackAfterLostSamples()
    {
        var policy = new AdaptiveLyricForeground();
        var bright = AdaptiveLyricForeground.Measure(Drawing.Color.White);
        var dark = AdaptiveLyricForeground.Measure(Drawing.Color.Black);
        Assert.True(policy.Observe(bright, 0, false));
        Assert.False(policy.IsConfirmingChange);
        Assert.True(policy.Observe(dark, 180, false));
        Assert.True(policy.IsConfirmingChange);
        Assert.True(policy.Observe(dark, 360, false));
        Assert.False(policy.Observe(dark, 540, false));
        Assert.False(policy.IsConfirmingChange);
        Assert.False(policy.Observe(null, 700, true));
        Assert.True(policy.Observe(null, 2340, true));
    }

    [Fact]
    public void ALongGapRestartsTheColorConfirmation()
    {
        var policy = new AdaptiveLyricForeground();
        var bright = AdaptiveLyricForeground.Measure(Drawing.Color.White);
        var dark = AdaptiveLyricForeground.Measure(Drawing.Color.Black);
        Assert.True(policy.Observe(bright, 0, false));
        Assert.True(policy.Observe(dark, 900, false));
        Assert.True(policy.IsConfirmingChange);
        Assert.True(policy.Observe(dark, 1800, false));
        Assert.True(policy.IsConfirmingChange);
        Assert.False(policy.Observe(dark, 2100, false));
        Assert.False(policy.IsConfirmingChange);
    }

    [Fact]
    public async Task DisabledSamplerDoesNotRequestScreenGeometryOrPublishColors()
    {
        var sampler = new LyricBackgroundSampler(_ => throw new InvalidOperationException());
        var request = new LyricBackgroundRequest(Rect.Empty, [], [], false);
        await sampler.SampleAsync(null!, request, () => throw new InvalidOperationException());

        sampler.Configure(true);
        sampler.Configure(false);
        await sampler.SampleAsync(null!, request, () => throw new InvalidOperationException());
    }

    [Fact]
    public void TextOverWhiteUsesDarkColorDespiteDarkUnusedSpace()
    {
        using var image = MakeSplitBackground(64);
        var text = new Rect(-188, 9, 38, 15);
        var contrast = Sample(image, text);
        Assert.True(new AdaptiveLyricForeground().Observe(contrast, 0, false));
    }

    [Fact]
    public void TextAcrossTheSplitIncludesLowContrastForBothColors()
    {
        using var image = MakeSplitBackground(100);
        var contrast = Sample(image, new Rect(-185, 9, 167, 15));
        Assert.InRange(contrast.Dark, 1, 2);
        Assert.InRange(contrast.Light, 1, 2);
    }

    [Fact]
    public void InkDoesNotChangeTheSelectedBackgroundColor()
    {
        using var image = MakeSplitBackground(0);
        using (var graphics = Drawing.Graphics.FromImage(image))
            graphics.FillRectangle(Drawing.Brushes.White, 24, 9, 120, 15);
        var contrast = Sample(image, new Rect(-176, 9, 120, 15));
        Assert.False(new AdaptiveLyricForeground().Observe(contrast, 0, true));
    }

    [Fact]
    public void SpectrumChoosesDarkTextFromBrightOnePixelGapsBetweenDarkBars()
    {
        using var image = new Drawing.Bitmap(128, 24);
        using var graphics = Drawing.Graphics.FromImage(image);
        graphics.Clear(Drawing.Color.White);
        var bars = Enumerable.Range(0, 32).Select(index => new Rect(index * 4, 0, 3, image.Height)).ToArray();
        foreach (var bar in bars)
            graphics.FillRectangle(Drawing.Brushes.Black, (int)bar.X, 0, (int)bar.Width, image.Height);

        var contrast = LyricBackgroundSampler.ReadBands(new Drawing.Rectangle(0, 0, image.Width, image.Height),
            image, [new LyricBackgroundText(new Rect(0, 0, image.Width, image.Height), 0.65)],
            bars, scale: 1, isSpectrum: true);

        Assert.True(contrast.HasValue);
        Assert.True(new AdaptiveLyricForeground().Observe(contrast, 0, false));
    }

    private static LyricBackgroundContrast Sample(Drawing.Bitmap image, Rect text) =>
        Assert.IsType<LyricBackgroundContrast>(LyricBackgroundSampler.ReadBands(
            new Drawing.Rectangle(-200, 0, image.Width, image.Height), image,
            [new LyricBackgroundText(text, 0.9)], [text], scale: 1));

    private static Drawing.Bitmap MakeSplitBackground(int lightWidth)
    {
        var image = new Drawing.Bitmap(200, 36);
        using var graphics = Drawing.Graphics.FromImage(image);
        graphics.Clear(Drawing.Color.Black);
        if (lightWidth > 0)
            graphics.FillRectangle(Drawing.Brushes.White, 0, 0, lightWidth, image.Height);
        return image;
    }
}
