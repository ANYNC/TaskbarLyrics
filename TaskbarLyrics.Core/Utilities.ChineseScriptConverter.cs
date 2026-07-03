using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarLyrics.Core.Utilities;

public static class ChineseScriptConverter
{
    private const uint LCMAP_SIMPLIFIED_CHINESE = 0x02000000;
    private static readonly Lazy<Func<string, string>?> OpenCcToSimplified = new(CreateOpenCcToSimplified);

    private static readonly IReadOnlyDictionary<char, char> FallbackSimplifiedMap = new Dictionary<char, char>
    {
        ['後'] = '后',
        ['愛'] = '爱',
        ['錯'] = '错',
        ['過'] = '过',
        ['靜'] = '静',
        ['倫'] = '伦',
        ['聽'] = '听',
        ['國'] = '国',
        ['體'] = '体',
        ['語'] = '语',
        ['夢'] = '梦',
        ['時'] = '时',
        ['間'] = '间',
        ['歡'] = '欢',
        ['與'] = '与',
        ['為'] = '为',
        ['會'] = '会',
        ['來'] = '来',
        ['見'] = '见',
        ['這'] = '这',
        ['樣'] = '样',
        ['點'] = '点',
        ['聲'] = '声',
        ['樂'] = '乐',
        ['別'] = '别',
        ['裡'] = '里',
        ['臺'] = '台',
        ['妳'] = '你'
    };

    public static string ToSimplified(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var converted = TryOpenCcToSimplified(text);
        converted = TryWindowsToSimplified(converted);
        return ApplyFallbackSimplifiedMap(converted);
    }

    private static string TryOpenCcToSimplified(string text)
    {
        try
        {
            var converter = OpenCcToSimplified.Value;
            return converter is null ? text : converter(text);
        }
        catch
        {
            return text;
        }
    }

    private static Func<string, string>? CreateOpenCcToSimplified()
    {
        try
        {
            return global::OpenCC.Presets.T2cn.Converter("tw", "cn");
        }
        catch
        {
            return null;
        }
    }

    private static string TryWindowsToSimplified(string text)
    {
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

            var buffer = new char[required];
            var written = LCMapStringEx(
                "zh-CN",
                LCMAP_SIMPLIFIED_CHINESE,
                text,
                text.Length,
                buffer,
                buffer.Length,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            return written > 0
                ? new string(buffer, 0, Math.Min(written, buffer.Length)).TrimEnd('\0')
                : text;
        }
        catch
        {
            return text;
        }
    }

    private static string ApplyFallbackSimplifiedMap(string text)
    {
        StringBuilder? builder = null;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (!FallbackSimplifiedMap.TryGetValue(ch, out var mapped))
            {
                if (builder is not null)
                {
                    builder.Append(ch);
                }

                continue;
            }

            builder ??= new StringBuilder(text.Length).Append(text, 0, i);
            builder.Append(mapped);
        }

        return builder?.ToString() ?? text;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringEx(
        string lpLocaleName,
        uint dwMapFlags,
        string lpSrcStr,
        int cchSrc,
        char[]? lpDestStr,
        int cchDest,
        IntPtr lpVersionInformation,
        IntPtr lpReserved,
        IntPtr sortHandle);
}
