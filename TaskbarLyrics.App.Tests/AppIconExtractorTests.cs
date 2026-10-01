using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TaskbarLyrics.App;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class AppIconExtractorTests
{
    [Fact]
    public void EncodesSmallIconsAsStorablePng()
    {
        using var bitmap = new Bitmap(16, 16);

        var png = AppIconExtractor.TryEncodePng(bitmap);

        Assert.NotNull(png);
        Assert.True(CustomPlayerIcon.AcceptsPng(png));
    }

    [Fact]
    public void SkipsEncodedIconsThatExceedTheStorageLimit()
    {
        using var bitmap = new Bitmap(512, 512, PixelFormat.Format32bppArgb);
        FillWithNoise(bitmap);

        Assert.Null(AppIconExtractor.TryEncodePng(bitmap));
    }

    private static void FillWithNoise(Bitmap bitmap)
    {
        var random = new Random(12345);
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * bitmap.Height];
            random.NextBytes(bytes);
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
