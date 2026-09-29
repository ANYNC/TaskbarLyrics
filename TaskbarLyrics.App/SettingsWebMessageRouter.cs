using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarLyrics.App;

internal sealed class SettingsWebMessage
{
    public const int CurrentVersion = WebViewMessageRouter.CurrentVersion;

    public int Version { get; set; }

    public string? Type { get; set; }

    public JsonElement? Payload { get; set; }

    [JsonIgnore]
    public string? Key { get; init; }

    [JsonIgnore]
    public JsonElement? Value { get; init; }
}

internal enum LyricDiagnosticApplyMode
{
    Current,
    Remember
}

internal sealed record LyricDiagnosticCandidateApplyRequest(
    string ProviderId,
    string CandidateId,
    LyricDiagnosticApplyMode Mode);

internal sealed record CustomPlayerSourceAddRequest(
    string SourceAppUserModelId, string DisplayName, string IconDataUrl, string PresetIconId, string PresetIconColor,
    bool Enabled, List<LyricProviderPreference> LyricProviders);

internal static class SettingsWebJson
{
    public static JsonSerializerOptions Options => WebViewMessageRouter.JsonOptions;
}

internal static class SettingsWebMessageRouter
{
    public static bool TryParseCustomPlayerSourceAddRequest(
        JsonElement? value,
        out CustomPlayerSourceAddRequest request)
    {
        request = null!;
        if (value is not { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty("sourceAppUserModelId", out var idElement) ||
            idElement.ValueKind != JsonValueKind.String ||
            !payload.TryGetProperty("displayName", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var id = idElement.GetString();
        var name = nameElement.GetString();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || id.Any(char.IsControl) ||
            name is null || name.Length > 80 || name.Any(char.IsControl))
        {
            return false;
        }

        string? iconDataUrl = null;
        if (payload.TryGetProperty("iconDataUrl", out var iconElement))
        {
            if (iconElement.ValueKind != JsonValueKind.String) return false;
            iconDataUrl = iconElement.GetString();
        }
        if (!CustomPlayerIcon.TryNormalize(iconDataUrl, out var normalizedIcon)) return false;

        string? presetIconId = null;
        string? presetIconColor = null;
        if (payload.TryGetProperty("presetIconId", out var presetIdElement))
        {
            if (presetIdElement.ValueKind != JsonValueKind.String) return false;
            presetIconId = presetIdElement.GetString();
        }
        if (payload.TryGetProperty("presetIconColor", out var presetColorElement))
        {
            if (presetColorElement.ValueKind != JsonValueKind.String) return false;
            presetIconColor = presetColorElement.GetString();
        }
        if (!PresetPlayerIcon.TryNormalize(presetIconId, presetIconColor, out var normalizedPresetId,
                out var normalizedPresetColor) ||
            (normalizedIcon.Length > 0 && normalizedPresetId.Length > 0)) return false;

        var enabled = true;
        if (payload.TryGetProperty("enabled", out var enabledElement))
        {
            if (enabledElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            enabled = enabledElement.GetBoolean();
        }

        var lyricProviders = PlayerSourceSettings.CreateDefaultLyricProviders();
        if (payload.TryGetProperty("lyricProviders", out var providersElement) &&
            !TryParseLyricProviderPreferences(providersElement, out lyricProviders)) return false;

        request = new CustomPlayerSourceAddRequest(id.Trim(), name.Trim(), normalizedIcon,
            normalizedPresetId, normalizedPresetColor, enabled, lyricProviders);
        return true;
    }

    public static bool TryParseLyricProviderPreferences(
        JsonElement? value,
        out List<LyricProviderPreference> providers)
    {
        providers = [];
        if (value is not { ValueKind: JsonValueKind.Array } payload)
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<LyricProviderPreference>>(
                payload.GetRawText(), SettingsWebJson.Options);
            if (!PlayerSourceSettings.IsValidLyricProviders(parsed))
            {
                return false;
            }

            providers = parsed!;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static SettingsWebMessage? Parse(string? messageJson)
    {
        var message = WebViewMessageRouter.Parse(messageJson);
        if (message is null)
        {
            return null;
        }

        if (!IsSettingValueMessage(message.Type) ||
            message.Payload is not { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty("key", out var keyElement) ||
            !payload.TryGetProperty("value", out var valueElement))
        {
            return ToSettingsMessage(message, value: message.Payload?.Clone());
        }

        return ToSettingsMessage(
            message,
            key: keyElement.ValueKind == JsonValueKind.String ? keyElement.GetString() : null,
            value: valueElement.Clone());
    }

    public static bool TryParseLyricDiagnosticCandidateApplyRequest(
        JsonElement? value,
        out LyricDiagnosticCandidateApplyRequest request)
    {
        request = null!;
        if (value is not { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty("providerId", out var providerElement) ||
            !payload.TryGetProperty("candidateId", out var candidateElement) ||
            providerElement.ValueKind != JsonValueKind.String ||
            candidateElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var providerId = providerElement.GetString();
        var candidateId = candidateElement.GetString();
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(candidateId))
        {
            return false;
        }

        var mode = LyricDiagnosticApplyMode.Current;
        if (payload.TryGetProperty("mode", out var modeElement))
        {
            if (modeElement.ValueKind != JsonValueKind.String ||
                !TryParseLyricDiagnosticApplyMode(modeElement.GetString(), out mode))
            {
                return false;
            }
        }

        request = new LyricDiagnosticCandidateApplyRequest(providerId, candidateId, mode);
        return true;
    }

    private static SettingsWebMessage ToSettingsMessage(
        WebViewMessage message,
        string? key = null,
        JsonElement? value = null)
    {
        return new SettingsWebMessage
        {
            Version = message.Version,
            Type = message.Type,
            Payload = message.Payload,
            Key = key,
            Value = value
        };
    }

    private static bool IsSettingValueMessage(string? type)
    {
        return type is "update" or "previewUpdate";
    }

    private static bool TryParseLyricDiagnosticApplyMode(
        string? value,
        out LyricDiagnosticApplyMode mode)
    {
        mode = value switch
        {
            "current" => LyricDiagnosticApplyMode.Current,
            "remember" => LyricDiagnosticApplyMode.Remember,
            _ => default
        };
        return value is "current" or "remember";
    }
}
