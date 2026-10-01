using System.Runtime.InteropServices;

namespace TaskbarLyrics.App;

internal static class NativeIconInterop
{
    internal const uint ProcessQueryLimitedInformation = 0x1000;

    internal const uint ExtractIconFailure = 0xFFFFFFFF;

    internal const int ShellItemImageFlagIconOnly = 0x04;
    internal const int ShellItemImageFlagThumbnailOnly = 0x08;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool QueryFullProcessImageName(IntPtr process, uint flags, [Out] char[] executableName, ref int size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint PrivateExtractIcons(string fileName, int iconIndex, int width, int height,
        IntPtr[] icons, uint[]? iconIds, uint iconCount, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyIcon(IntPtr icon);

    internal const uint ShellFileInfoIcon = 0x000000100;
    internal const uint ShellFileInfoLargeIcon = 0x000000000;
    internal const uint ShellFileInfoPidl = 0x000000008;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SHGetFileInfo(IntPtr pidl, uint fileAttributes, ref ShellFileInfo fileInfo,
        uint fileInfoSize, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr ILCreateFromPath(string path);

    [DllImport("shell32.dll")]
    internal static extern void ILFree(IntPtr pidl);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSize
    {
        public int Width;
        public int Height;

        public NativeSize(int size)
        {
            Width = size;
            Height = size;
        }
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage([In, MarshalAs(UnmanagedType.Struct)] NativeSize size, [In] int flags, [Out] out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    internal static extern int SHCreateItemFromParsingName(string parsingName, IntPtr bindContext,
        [MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId,
        [Out, MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeBitmapInfo
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPerPixel;
        public IntPtr Bits;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int GetObject(IntPtr gdiObject, int bufferSize, out NativeBitmapInfo bitmap);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern bool DeleteObject(IntPtr gdiObject);
}
