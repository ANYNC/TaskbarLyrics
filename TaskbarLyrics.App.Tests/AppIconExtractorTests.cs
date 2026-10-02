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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConvertsShellDibToUprightBitmapForBothRowOrders(bool topDown)
    {
        const int height = 16;
        var hBitmap = CreateDibSection(height, topDown, Color.Red, Color.Blue);
        try
        {
            using var bitmap = AppIconExtractor.ToBitmap(hBitmap);

            Assert.NotNull(bitmap);
            AssertColor(bitmap!.GetPixel(8, 0), Color.Red);
            AssertColor(bitmap.GetPixel(8, height - 1), Color.Blue);
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    private static void AssertColor(Color actual, Color expected)
    {
        Assert.True(
            Math.Abs(actual.R - expected.R) < 8 && Math.Abs(actual.G - expected.G) < 8 && Math.Abs(actual.B - expected.B) < 8,
            $"expected {expected} but was {actual}");
    }

    private static IntPtr CreateDibSection(int height, bool topDown, Color top, Color bottom)
    {
        const int width = 16;
        var header = new TestBitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<TestBitmapInfoHeader>(),
            Width = width,
            Height = topDown ? -height : height,
            Planes = 1,
            BitCount = 32,
            Compression = 0
        };

        var hBitmap = CreateDIBSection(IntPtr.Zero, ref header, 0, out var bits, IntPtr.Zero, 0);
        Assert.NotEqual(IntPtr.Zero, hBitmap);
        Assert.NotEqual(IntPtr.Zero, bits);

        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
        {
            var imageRow = topDown ? row : height - 1 - row;
            var color = imageRow < height / 2 ? top : bottom;
            for (var x = 0; x < width; x++)
            {
                var offset = ((row * width) + x) * 4;
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = color.A;
            }
        }

        Marshal.Copy(pixels, 0, bits, pixels.Length);
        return hBitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TestBitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr deviceContext, ref TestBitmapInfoHeader bitmapInfo, uint usage,
        out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr gdiObject);
}
