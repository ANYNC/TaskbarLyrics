using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarLyrics.Core.Utilities;

public static class ChineseScriptConverter
{
    private const uint LCMAP_SIMPLIFIED_CHINESE = 0x02000000;

    public static string ToSimplified(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        try
        {
            var required = LCMapStringEx(
                "zh-CN",
                LCMAP_SIMPLIFIED_CHINESE,
                text,
                text.Length,
                null,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (required <= 0)
            {
                return text;
            }

            var builder = new StringBuilder(required);
            var written = LCMapStringEx(
                "zh-CN",
                LCMAP_SIMPLIFIED_CHINESE,
                text,
                text.Length,
                builder,
                builder.Capacity,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            return written > 0 ? builder.ToString() : text;
        }
        catch
        {
            return text;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringEx(
        string lpLocaleName,
        uint dwMapFlags,
        string lpSrcStr,
        int cchSrc,
        StringBuilder? lpDestStr,
        int cchDest,
        IntPtr lpVersionInformation,
        IntPtr lpReserved,
        IntPtr sortHandle);
}
