using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace TaskbarLyrics.App;

internal static class AppIconExtractor
{
    internal static readonly int[] CandidateSizes = [512, 256, 192, 128, 96, 72, 64, 48, 32, 24, 16];

    private static readonly int[] ShellItemImageFlags =
    [
        NativeIconInterop.ShellItemImageFlagIconOnly,
        NativeIconInterop.ShellItemImageFlagThumbnailOnly
    ];

    public static byte[]? ExtractExecutablePng(string executablePath, int iconIndex = 0)
    {
        foreach (var size in CandidateSizes)
        {
            if (ExtractExecutablePngAtSize(executablePath, size, iconIndex) is { } png)
            {
                return png;
            }
        }

        return null;
    }

    public static byte[]? ExtractShellItemPng(string shellItemPath)
    {
        var interfaceId = typeof(NativeIconInterop.IShellItemImageFactory).GUID;
        if (NativeIconInterop.SHCreateItemFromParsingName(shellItemPath, IntPtr.Zero, interfaceId, out var factory) == 0 &&
            factory is not null)
        {
            try
            {
                foreach (var size in CandidateSizes)
                {
                    foreach (var flags in ShellItemImageFlags)
                    {
                        if (factory.GetImage(new NativeIconInterop.NativeSize(size), flags, out var bitmapHandle) != 0 ||
                            bitmapHandle == IntPtr.Zero)
                        {
                            continue;
                        }

                        try
                        {
                            using var bitmap = ToBitmap(bitmapHandle);
                            if (bitmap is not null && TryEncodePng(bitmap) is { } png)
                            {
                                return png;
                            }
                        }
                        finally
                        {
                            NativeIconInterop.DeleteObject(bitmapHandle);
                        }
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }

        return ExtractShellItemIconFromPidl(shellItemPath);
    }

    private static byte[]? ExtractExecutablePngAtSize(string executablePath, int size, int iconIndex)
    {
        var icons = new IntPtr[1];
        var extracted = NativeIconInterop.PrivateExtractIcons(executablePath, iconIndex, size, size, icons, null, 1, 0);
        if (extracted is 0 or NativeIconInterop.ExtractIconFailure || icons[0] == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            using var icon = Icon.FromHandle(icons[0]);
            using var bitmap = icon.ToBitmap();
            return TryEncodePng(bitmap);
        }
        finally
        {
            NativeIconInterop.DestroyIcon(icons[0]);
        }
    }

    private static byte[]? ExtractShellItemIconFromPidl(string shellItemPath)
    {
        var pidl = NativeIconInterop.ILCreateFromPath(shellItemPath);
        if (pidl == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var fileInfo = new NativeIconInterop.ShellFileInfo();
            var fileInfoSize = (uint)Marshal.SizeOf<NativeIconInterop.ShellFileInfo>();
            if (NativeIconInterop.SHGetFileInfo(pidl, 0, ref fileInfo, fileInfoSize,
                    NativeIconInterop.ShellFileInfoIcon |
                    NativeIconInterop.ShellFileInfoLargeIcon |
                    NativeIconInterop.ShellFileInfoPidl) == IntPtr.Zero ||
                fileInfo.Icon == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                using var icon = Icon.FromHandle(fileInfo.Icon);
                using var bitmap = icon.ToBitmap();
                return TryEncodePng(bitmap);
            }
            finally
            {
                NativeIconInterop.DestroyIcon(fileInfo.Icon);
            }
        }
        finally
        {
            NativeIconInterop.ILFree(pidl);
        }
    }

    private static Bitmap? ToBitmap(IntPtr bitmapHandle)
    {
        if (NativeIconInterop.GetObject(bitmapHandle, Marshal.SizeOf<NativeIconInterop.NativeBitmapInfo>(), out var info) == 0)
        {
            return null;
        }

        if (info.Bits == IntPtr.Zero || info.BitsPerPixel != 32)
        {
            return Bitmap.FromHbitmap(bitmapHandle);
        }

        var width = info.Width;
        var height = Math.Abs(info.Height);
        var stride = width * 4;
        var raw = new byte[stride * height];
        Marshal.Copy(info.Bits, raw, 0, raw.Length);

        var flipped = new byte[raw.Length];
        for (var row = 0; row < height; row++)
        {
            Array.Copy(raw, row * stride, flipped, (height - 1 - row) * stride, stride);
        }

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(flipped, 0, data.Scan0, flipped.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    // 取图结果必须先满足自定义播放器图标的落库约束；超限时降档到更小尺寸，而不是把图标整张丢弃。
    internal static byte[]? TryEncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        var png = stream.ToArray();
        return CustomPlayerIcon.AcceptsPng(png) ? png : null;
    }
}
