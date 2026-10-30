using System.Text.Json;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.App;

public sealed record LyricsFallbackLine
{
    public string Kind { get; set; } = "Text";
    public bool ShowTitle { get; set; } = true;
    public bool ShowArtist { get; set; }
    // Null follows the localized state message; an explicit empty string stays empty.
    public string? CustomText { get; set; }

    internal LyricsFallbackLine Normalized() => this with
    {
        Kind = Kind is "Metadata" or "Text" or "None" ? Kind : "Text",
        ShowTitle = ShowTitle || !ShowArtist,
        CustomText = NormalizeText(CustomText)
    };

    private static string? NormalizeText(string? value)
    {
        if (value is null) return null;
        var text = value.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
        return text[..Math.Min(text.Length, 120)];
    }
}

public sealed record LyricsFallbackState
{
    public bool UseSpectrum { get; set; }
    public LyricsFallbackLine FirstLine { get; set; } = new();
    public LyricsFallbackLine SecondLine { get; set; } = new() { Kind = "None" };

    internal LyricsFallbackState Normalized() => this with
    {
        FirstLine = (FirstLine ?? new LyricsFallbackLine()).Normalized(),
        SecondLine = (SecondLine ?? new LyricsFallbackLine { Kind = "None" }).Normalized()
    };
}

public sealed record LyricsFallbackSettings
{
    public int Version { get; set; } = 1;
    public LyricsFallbackState Searching { get; set; } = new()
    {
        FirstLine = new() { Kind = "Metadata", ShowArtist = true },
        SecondLine = new()
    };
    public LyricsFallbackState Instrumental { get; set; } = new();
    public LyricsFallbackState NotFound { get; set; } = new();
    public bool SpectrumWithLyrics { get; set; }

    internal bool UsesSpectrum => SpectrumWithLyrics || Instrumental.UseSpectrum || NotFound.UseSpectrum;

    internal LyricsFallbackSettings Normalized() => this with
    {
        Version = 1,
        Searching = (Searching ?? new LyricsFallbackSettings().Searching).Normalized() with { UseSpectrum = false },
        Instrumental = (Instrumental ?? new LyricsFallbackState()).Normalized(),
        NotFound = (NotFound ?? new LyricsFallbackState()).Normalized()
    };

    internal static LyricsFallbackSettings FromLegacy(AppSettings settings) => new()
    {
        Searching = new()
        {
            FirstLine = new() { Kind = "Metadata", ShowArtist = true },
            SecondLine = new() { Kind = settings.ShowLyricSearchStatus ? "Text" : "None" }
        },
        Instrumental = new() { UseSpectrum = settings.SpectrumDisplayMode is SpectrumDisplayMode.PureMusicOnly or SpectrumDisplayMode.PureMusicOrNoLyrics or SpectrumDisplayMode.Always },
        NotFound = new() { UseSpectrum = settings.SpectrumDisplayMode is SpectrumDisplayMode.Always or SpectrumDisplayMode.PureMusicOrNoLyrics },
        SpectrumWithLyrics = settings.SpectrumDisplayMode == SpectrumDisplayMode.Always
    };

    internal static bool TryRead(JsonElement value, out LyricsFallbackSettings settings)
    {
        settings = null!;
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var number) || number != 1 ||
            !HasBoolean(value, "spectrumWithLyrics")) return false;
        foreach (var state in new[] { "searching", "instrumental", "notFound" })
        {
            if (!value.TryGetProperty(state, out var rule) || rule.ValueKind != JsonValueKind.Object ||
                !HasBoolean(rule, "useSpectrum") || (state == "searching" && rule.GetProperty("useSpectrum").GetBoolean())) return false;
            foreach (var line in new[] { "firstLine", "secondLine" })
            {
                if (!rule.TryGetProperty(line, out var content) || content.ValueKind != JsonValueKind.Object ||
                    !content.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String ||
                    kind.GetString() is not ("Metadata" or "Text" or "None") ||
                    !HasBoolean(content, "showTitle") || !HasBoolean(content, "showArtist") ||
                    !content.TryGetProperty("customText", out var text) ||
                    text.ValueKind is not (JsonValueKind.Null or JsonValueKind.String) ||
                    (text.ValueKind == JsonValueKind.String && text.GetString()!.Length > 120)) return false;
            }
        }
        settings = JsonSerializer.Deserialize<LyricsFallbackSettings>(value.GetRawText(), SettingsWebJson.Options)!.Normalized();
        return true;
    }

    private static bool HasBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False;
}

internal readonly record struct LyricsFallbackPresentation(string Current, string Next);

internal static class LyricsFallbackPresentationPolicy
{
    internal static bool ShouldShowSpectrum(AppSettings settings, LyricDisplayFrame frame, bool searching)
    {
        if (searching || !settings.SpectrumAudioAccessGranted) return false;
        var rules = settings.GetLyricsFallback();
        var missing = frame.CurrentLineIndex < 0 && frame.CurrentLine == TaskbarLyrics.Core.Services.LyricSyncService.NoLyricsText;
        return frame.IsPureMusic ? rules.Instrumental.UseSpectrum
            : missing ? rules.NotFound.UseSpectrum : frame.CurrentLineIndex >= 0 && rules.SpectrumWithLyrics;
    }

    internal static LyricsFallbackPresentation Create(LyricsFallbackState rule, TrackInfo? track, string message)
    {
        string Line(LyricsFallbackLine line) => line.Kind switch
        {
            "None" => " ",
            "Metadata" => Join(line.ShowTitle ? track?.Title : null, line.ShowArtist ? track?.Artist : null),
            _ => line.CustomText ?? message
        };
        var current = Line(rule.FirstLine);
        var next = Line(rule.SecondLine);
        return new(current.Length == 0 ? " " : current, next.Length == 0 ? " " : next);
    }

    private static string Join(string? title, string? artist)
    {
        var values = new[] { title?.Trim(), artist?.Trim() }.Where(value => !string.IsNullOrEmpty(value));
        var result = string.Join(" - ", values);
        return result.Length == 0 ? " " : result;
    }
}
