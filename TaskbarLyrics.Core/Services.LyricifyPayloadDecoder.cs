using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Services;

public sealed class LyricifyPayloadDecoder : ILyricPayloadDecoder
{
    public bool CanDecode(LyricPayloadFormat format) =>
        format is LyricPayloadFormat.Qrc or LyricPayloadFormat.Krc;

    public Task<DecodedLyricPayload> DecodeAsync(
        RawLyricPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        cancellationToken.ThrowIfCancellationRequested();

        var original = payload.OriginalLyrics;
        if (payload.IsEncrypted && !string.IsNullOrWhiteSpace(original))
        {
            original = payload.Format switch
            {
                LyricPayloadFormat.Qrc =>
                    Lyricify.Lyrics.Decrypter.Qrc.Decrypter.DecryptLyrics(original),
                LyricPayloadFormat.Krc =>
                    Lyricify.Lyrics.Decrypter.Krc.Decrypter.DecryptLyrics(original),
                _ => throw new NotSupportedException(
                    $"No decoder is registered for encrypted {payload.Format} payloads.")
            };
        }

        return Task.FromResult(new DecodedLyricPayload(
            payload.ProviderId,
            payload.CandidateId,
            payload.Format,
            original,
            payload.TranslationLyrics,
            payload.IsPureMusic,
            payload.Diagnostics));
    }
}
