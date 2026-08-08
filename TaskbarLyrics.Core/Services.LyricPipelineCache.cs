using System.Security.Cryptography;
using System.Text;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Core.Services;

public sealed class LyricPipelineCache : ILyricPipelineCache
{
    public const int RawCacheVersion = 1;
    public const int ParsedCacheVersion = 1;

    private static readonly string DefaultCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TaskbarLyrics",
        "database");
    private static readonly JsonLyricCacheStore<RawLyricCacheEnvelope> DefaultRawStore = new(
        Path.Combine(DefaultCacheDirectory, "lyric-pipeline-raw-v1.json"));
    private static readonly JsonLyricCacheStore<ParsedLyricCacheEnvelope> DefaultParsedStore = new(
        Path.Combine(DefaultCacheDirectory, "lyric-pipeline-parsed-v1.json"));

    private readonly ILyricCacheStore<RawLyricCacheEnvelope> _rawStore;
    private readonly ILyricCacheStore<ParsedLyricCacheEnvelope> _parsedStore;

    public LyricPipelineCache(
        ILyricCacheStore<RawLyricCacheEnvelope> rawStore,
        ILyricCacheStore<ParsedLyricCacheEnvelope> parsedStore)
    {
        _rawStore = rawStore ?? throw new ArgumentNullException(nameof(rawStore));
        _parsedStore = parsedStore ?? throw new ArgumentNullException(nameof(parsedStore));
    }

    public static LyricPipelineCache CreateDefault() => new(DefaultRawStore, DefaultParsedStore);

    public static void ClearDefault()
    {
        DefaultRawStore.Clear();
        DefaultParsedStore.Clear();
        DeleteLegacyCacheFiles();
    }

    public bool TryGetRaw(
        LyricProviderId providerId,
        string candidateId,
        out RawLyricPayload? payload,
        out LyricAcquisitionKind acquisition)
    {
        var key = BuildKey(providerId, candidateId);
        if (!_rawStore.TryGet(key, out var envelope, out acquisition) || envelope is null)
        {
            payload = null;
            return false;
        }

        if (!IsValidRawEnvelope(envelope, providerId, candidateId))
        {
            _rawStore.Remove(key);
            payload = null;
            acquisition = LyricAcquisitionKind.Unknown;
            return false;
        }

        payload = new RawLyricPayload(
            providerId,
            candidateId,
            envelope.Format,
            envelope.OriginalLyrics,
            envelope.TranslationLyrics,
            envelope.IsEncrypted,
            envelope.IsPureMusic,
            envelope.Diagnostics,
            hasStableIdentity: true);
        return true;
    }

    public void StoreRaw(RawLyricPayload payload, DateTimeOffset fetchedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!payload.HasStableIdentity)
        {
            return;
        }

        var envelope = new RawLyricCacheEnvelope(
            RawCacheVersion,
            payload.ProviderId.Value,
            payload.CandidateId,
            payload.Format,
            ComputePayloadHash(payload),
            fetchedAtUtc,
            payload.OriginalLyrics,
            payload.TranslationLyrics,
            payload.IsEncrypted,
            payload.IsPureMusic,
            payload.Diagnostics);
        _rawStore.Store(BuildKey(payload.ProviderId, payload.CandidateId), envelope);
    }

    public bool TryGetParsed(
        RawLyricPayload rawPayload,
        string parserId,
        string parserVersion,
        string normalizationVersion,
        out ParsedLyrics? parsedLyrics,
        out LyricAcquisitionKind acquisition)
    {
        ArgumentNullException.ThrowIfNull(rawPayload);
        ValidateVersion(parserId, nameof(parserId));
        ValidateVersion(parserVersion, nameof(parserVersion));
        ValidateVersion(normalizationVersion, nameof(normalizationVersion));

        if (!rawPayload.HasStableIdentity)
        {
            parsedLyrics = null;
            acquisition = LyricAcquisitionKind.Unknown;
            return false;
        }

        var key = BuildKey(rawPayload.ProviderId, rawPayload.CandidateId);
        if (!_parsedStore.TryGet(key, out var envelope, out acquisition) || envelope is null)
        {
            parsedLyrics = null;
            return false;
        }

        var expectedHash = ComputePayloadHash(rawPayload);
        if (!HasMatchingIdentity(envelope, rawPayload) ||
            envelope.CacheVersion != ParsedCacheVersion ||
            string.IsNullOrWhiteSpace(envelope.RawPayloadHash) ||
            !IsUsable(envelope.Content))
        {
            _parsedStore.Remove(key);
            parsedLyrics = null;
            acquisition = LyricAcquisitionKind.Unknown;
            return false;
        }

        if (!string.Equals(envelope.RawPayloadHash, expectedHash, StringComparison.Ordinal) ||
            !string.Equals(envelope.ParserId, parserId, StringComparison.Ordinal) ||
            !string.Equals(envelope.ParserVersion, parserVersion, StringComparison.Ordinal) ||
            !string.Equals(envelope.NormalizationVersion, normalizationVersion, StringComparison.Ordinal))
        {
            parsedLyrics = null;
            acquisition = LyricAcquisitionKind.Unknown;
            return false;
        }

        parsedLyrics = envelope.Content;
        return true;
    }

    public void StoreParsed(
        RawLyricPayload rawPayload,
        ParsedLyrics parsedLyrics,
        string parserId,
        string parserVersion,
        string normalizationVersion)
    {
        ArgumentNullException.ThrowIfNull(rawPayload);
        ArgumentNullException.ThrowIfNull(parsedLyrics);
        ValidateVersion(parserId, nameof(parserId));
        ValidateVersion(parserVersion, nameof(parserVersion));
        ValidateVersion(normalizationVersion, nameof(normalizationVersion));
        if (!rawPayload.HasStableIdentity || !IsUsable(parsedLyrics))
        {
            return;
        }

        var envelope = new ParsedLyricCacheEnvelope(
            ParsedCacheVersion,
            rawPayload.ProviderId.Value,
            rawPayload.CandidateId,
            ComputePayloadHash(rawPayload),
            parserId,
            parserVersion,
            normalizationVersion,
            parsedLyrics);
        _parsedStore.Store(BuildKey(rawPayload.ProviderId, rawPayload.CandidateId), envelope);
    }

    public static string ComputePayloadHash(RawLyricPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var canonical = string.Join('\u001f',
            ((int)payload.Format).ToString(System.Globalization.CultureInfo.InvariantCulture),
            payload.IsEncrypted ? "1" : "0",
            payload.IsPureMusic ? "1" : "0",
            Encode(payload.OriginalLyrics),
            Encode(payload.TranslationLyrics));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool IsValidRawEnvelope(
        RawLyricCacheEnvelope envelope,
        LyricProviderId providerId,
        string candidateId)
    {
        if (envelope.CacheVersion != RawCacheVersion ||
            !string.Equals(envelope.ProviderId, providerId.Value, StringComparison.Ordinal) ||
            !string.Equals(envelope.CandidateId, candidateId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(envelope.PayloadHash) ||
            (!envelope.IsPureMusic && string.IsNullOrWhiteSpace(envelope.OriginalLyrics)))
        {
            return false;
        }

        var payload = new RawLyricPayload(
            providerId,
            candidateId,
            envelope.Format,
            envelope.OriginalLyrics,
            envelope.TranslationLyrics,
            envelope.IsEncrypted,
            envelope.IsPureMusic,
            envelope.Diagnostics);
        return string.Equals(envelope.PayloadHash, ComputePayloadHash(payload), StringComparison.Ordinal);
    }

    private static bool HasMatchingIdentity(
        ParsedLyricCacheEnvelope envelope,
        RawLyricPayload payload) =>
        string.Equals(envelope.ProviderId, payload.ProviderId.Value, StringComparison.Ordinal) &&
        string.Equals(envelope.CandidateId, payload.CandidateId, StringComparison.Ordinal);

    private static bool IsUsable(ParsedLyrics? content) =>
        content is not null && (content.IsPureMusic || content.Lines.Count > 0);

    private static string BuildKey(LyricProviderId providerId, string candidateId)
    {
        if (string.IsNullOrWhiteSpace(candidateId))
        {
            throw new ArgumentException("Candidate ID cannot be empty.", nameof(candidateId));
        }

        return $"{providerId.Value}:{candidateId}";
    }

    private static string Encode(string? value) => value is null ? "-1:" : $"{value.Length}:{value}";

    private static void ValidateVersion(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Cache version identity cannot be empty.", parameterName);
        }
    }

    private static void DeleteLegacyCacheFiles()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskbarLyrics");
        var legacyPaths = new[]
        {
            Path.Combine(DefaultCacheDirectory, "smtc-generic-lyrics.json"),
            Path.Combine(appDataDirectory, "cache", "unified-lyrics-v8.json"),
            Path.Combine(appDataDirectory, "cache", "unified-lyrics-v9.json"),
            Path.Combine(appDataDirectory, "cache", "unified-lyrics-v10.json")
        };
        foreach (var legacyPath in legacyPaths)
        {
            try
            {
                if (File.Exists(legacyPath))
                {
                    File.Delete(legacyPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Failed to clear legacy lyric cache '{legacyPath}': {exception.Message}");
            }
        }
    }
}
