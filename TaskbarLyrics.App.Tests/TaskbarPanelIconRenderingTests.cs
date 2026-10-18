using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TaskbarLyrics.Core.Models;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void TaskbarSourceIconAndPlayerNameShareRenderedVerticalCenter(double scale)
    {
        RunSta(() =>
        {
            var panel = new LyricsControlPanelWindow(_ => { }, _ => { });
            try
            {
                panel.UpdateTrack(new TrackInfo("alignment", "Run Off", "Liyon", "", "Netease", TimeSpan.FromMinutes(3)),
                    false, null, null, false, PlayerSourceBadgeResolver.Resolve("Netease", new AppSettings()));
                ((TextBlock)panel.FindName("SourceText")).Foreground = new SolidColorBrush(Color.FromRgb(169, 173, 181));
                ((UIElement)panel.FindName("VisualRoot")).Opacity = 1;
                panel.Show();
                panel.UpdateLayout();
                var row = (Grid)panel.FindName("SourceRow");
                var mark = (Border)panel.FindName("SourceMark");
                var name = (TextBlock)panel.FindName("SourceText");
                var rowPosition = row.TranslatePoint(new System.Windows.Point(), panel);
                var markPosition = mark.TranslatePoint(new System.Windows.Point(), panel);
                var namePosition = name.TranslatePoint(new System.Windows.Point(), panel);
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth * scale), (int)Math.Ceiling(panel.ActualHeight * scale),
                    96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render((Visual)panel.Content);
                var pixels = ReadIconPixels(bitmap);
                var top = (int)Math.Floor(rowPosition.Y * scale);
                var bottom = (int)Math.Ceiling((rowPosition.Y + row.ActualHeight) * scale);
                var iconCenter = SourceInkVerticalCenter(bitmap, pixels, (int)(markPosition.X * scale),
                    (int)((markPosition.X + mark.ActualWidth) * scale), top, bottom);
                var nameCenter = SourceInkVerticalCenter(bitmap, pixels, (int)(namePosition.X * scale),
                    (int)((namePosition.X + name.ActualWidth) * scale), top, bottom);
                Assert.True(Math.Abs(iconCenter - nameCenter) <= 0.5,
                    $"Icon center {iconCenter} and text center {nameCenter} differ at {scale * 100}% DPI.");
                Assert.Equal(12, mark.ActualWidth);
                Assert.Equal(12, mark.ActualHeight);
                Assert.True(name.DesiredSize.Height <= name.ActualHeight);
            }
            finally { panel.Close(); }
        });
    }

    private static double SourceInkVerticalCenter(BitmapSource bitmap, byte[] pixels, int left, int right, int top, int bottom)
    {
        var first = bitmap.PixelHeight;
        var last = -1;
        for (var y = top; y < Math.Min(bottom, bitmap.PixelHeight); y++)
        {
            for (var x = left; x < Math.Min(right, bitmap.PixelWidth); x++)
            {
                var index = (y * bitmap.PixelWidth + x) * 4;
                if (pixels[index] <= 110 && pixels[index + 1] <= 110 && pixels[index + 2] <= 110) continue;
                first = Math.Min(first, y);
                last = Math.Max(last, y);
            }
        }
        Assert.True(last >= first, "Expected visible icon or player-name pixels.");
        return (first + last) / 2.0;
    }

    [Theory]
    [InlineData(12)]
    [InlineData(15)]
    [InlineData(18)]
    [InlineData(24)]
    public void TaskbarPanelSmallIconKeepsFilteredDetailsAtDifferentDpi(int physicalPixels)
    {
        RunSta(() =>
        {
            var panel = new LyricsControlPanelWindow(_ => { }, _ => { });
            try
            {
                var badge = PlayerSourceBadgeResolver.Resolve("Netease", new AppSettings());
                panel.UpdateTrack(new TrackInfo("icon", "Song", "Artist", "", "Netease", TimeSpan.FromMinutes(3)),
                    false, null, null, false, badge);
                var sourceIcon = (Image)panel.FindName("SourceIcon");
                var source = Assert.IsAssignableFrom<BitmapSource>(sourceIcon.Source);

                // Compare actual output against area-filtered full-resolution artwork. The old
                // linear downscale skips narrow logo features and produces sharply aliased edges.
                var reference = RenderPanelIcon(source, BitmapScalingMode.HighQuality, physicalPixels);
                var aliased = RenderPanelIcon(source, BitmapScalingMode.LowQuality, physicalPixels);
                var actual = RenderPanelIcon(sourceIcon, physicalPixels);
                Assert.True(IconPixelDifference(actual, reference) < IconPixelDifference(aliased, reference) / 2,
                    $"Small icon lost filtered logo details at {physicalPixels}px.");
                Assert.Equal(12, sourceIcon.ActualWidth);
                Assert.Equal(12, sourceIcon.ActualHeight);
                Assert.True(sourceIcon.SnapsToDevicePixels);
                Assert.Contains(ReadIconPixels(actual).Where((_, index) => index % 4 == 3), alpha => alpha is > 0 and < 255);
            }
            finally { panel.Close(); }
        });
    }

    private static RenderTargetBitmap RenderPanelIcon(ImageSource source, BitmapScalingMode mode, int physicalPixels)
    {
        var icon = new Image { Source = source, Width = 12, Height = 12, Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(icon, mode);
        return RenderPanelIcon(icon, physicalPixels);
    }

    private static RenderTargetBitmap RenderPanelIcon(Image icon, int physicalPixels)
    {
        icon.Measure(new Size(12, 12));
        icon.Arrange(new Rect(0, 0, 12, 12));
        var result = new RenderTargetBitmap(physicalPixels, physicalPixels,
            96 * physicalPixels / 12.0, 96 * physicalPixels / 12.0, PixelFormats.Pbgra32);
        result.Render(icon);
        return result;
    }

    private static byte[] ReadIconPixels(BitmapSource image)
    {
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return pixels;
    }

    private static double IconPixelDifference(BitmapSource actual, BitmapSource reference)
    {
        var pixels = ReadIconPixels(actual);
        var expected = ReadIconPixels(reference);
        long difference = 0;
        for (var index = 0; index < pixels.Length; index++) difference += Math.Abs(pixels[index] - expected[index]);
        return (double)difference / pixels.Length;
    }
}
