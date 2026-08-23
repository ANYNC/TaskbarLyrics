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

internal static class SettingsWebJson
{
    public static JsonSerializerOptions Options => WebViewMessageRouter.JsonOptions;
}

internal static class SettingsWebMessageRouter
{
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
