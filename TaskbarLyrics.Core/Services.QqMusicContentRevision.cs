using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Services;

internal static class QqMusicContentRevision
{
    internal const string Current = "3";
    internal const string DiagnosticKey = "qqContentRevision";

    internal static bool NeedsRefresh(LyricProviderId provider, LyricPayloadFormat format,
        IReadOnlyDictionary<string, string> diagnostics) =>
        provider == KnownLyricProviders.QQMusic && format is LyricPayloadFormat.Lrc or LyricPayloadFormat.Qrc &&
        (!diagnostics.TryGetValue(DiagnosticKey, out var revision) || revision != Current);
}
