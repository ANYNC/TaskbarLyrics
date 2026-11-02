using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace TaskbarLyrics.App;

internal static class BrowserMessageProtocol
{
    internal const int Version = 1;
    internal const int MaximumBytes = 256 * 1024;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static JsonElement Message(string type, object payload) =>
        JsonSerializer.SerializeToElement(new { version = Version, type, payload }, Json);

    internal static bool TryReadEnvelope(JsonElement message, out string type, out JsonElement payload)
    {
        type = string.Empty;
        payload = default;
        if (message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != Version ||
            !message.TryGetProperty("type", out var kind) || kind.ValueKind != JsonValueKind.String ||
            !message.TryGetProperty("payload", out payload) || payload.ValueKind != JsonValueKind.Object) return false;
        type = kind.GetString() ?? string.Empty;
        return type.Length is > 0 and <= 40;
    }

    internal static async Task<JsonElement?> ReadAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        var first = await stream.ReadAsync(header.AsMemory(0, 1), token).ConfigureAwait(false);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaximumBytes) throw new InvalidDataException("Invalid browser message length.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        return document.RootElement.Clone();
    }

    internal static async Task WriteAsync(Stream stream, JsonElement message, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Browser message is too large.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }
}
