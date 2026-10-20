using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Tests;

internal sealed class RecordingLyricPipelineCache : ILyricPipelineCache
{
    private readonly Dictionary<string, RawLyricPayload> _raw = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ParsedLyrics> _parsed = new(StringComparer.Ordinal);
    private readonly object _syncRoot = new();

    public int RawStoreCalls { get; private set; }
    public int ParsedStoreCalls { get; private set; }
    public List<string> StoredRawCandidateIds { get; } = [];

    public bool TryGetRaw(
        LyricProviderId providerId,
        string candidateId,
        out RawLyricPayload? payload,
        out LyricAcquisitionKind acquisition)
    {
        lock (_syncRoot)
        {
            if (_raw.TryGetValue(Key(providerId, candidateId), out payload))
            {
                acquisition = LyricAcquisitionKind.MemoryCache;
                return true;
            }
        }

        payload = null;
        acquisition = LyricAcquisitionKind.Unknown;
        return false;
    }

    public void StoreRaw(RawLyricPayload payload, DateTimeOffset fetchedAtUtc)
    {
        lock (_syncRoot)
        {
            RawStoreCalls++;
            StoredRawCandidateIds.Add(payload.CandidateId);
            _raw[Key(payload.ProviderId, payload.CandidateId)] = payload;
        }
    }

    public void Invalidate(LyricProviderId providerId, string candidateId)
    {
        lock (_syncRoot)
        {
            var key = Key(providerId, candidateId);
            _raw.Remove(key);
            foreach (var parsedKey in _parsed.Keys.Where(value => value.StartsWith(key + "\u001f", StringComparison.Ordinal)).ToArray())
            {
                _parsed.Remove(parsedKey);
            }
        }
    }

    public bool TryGetParsed(
        RawLyricPayload rawPayload,
        string parserId,
        string parserVersion,
        string normalizationVersion,
        out ParsedLyrics? parsedLyrics,
        out LyricAcquisitionKind acquisition)
    {
        lock (_syncRoot)
        {
            if (_parsed.TryGetValue(Key(rawPayload, parserId, parserVersion, normalizationVersion), out parsedLyrics))
            {
                acquisition = LyricAcquisitionKind.MemoryCache;
                return true;
            }
        }

        parsedLyrics = null;
        acquisition = LyricAcquisitionKind.Unknown;
        return false;
    }

    public void StoreParsed(
        RawLyricPayload rawPayload,
        ParsedLyrics parsedLyrics,
        string parserId,
        string parserVersion,
        string normalizationVersion)
    {
        lock (_syncRoot)
        {
            ParsedStoreCalls++;
            _parsed[Key(rawPayload, parserId, parserVersion, normalizationVersion)] = parsedLyrics;
        }
    }

    private static string Key(LyricProviderId providerId, string candidateId) =>
        $"{providerId.Value}\u001f{candidateId}";

    private static string Key(
        RawLyricPayload payload,
        string parserId,
        string parserVersion,
        string normalizationVersion) =>
        $"{Key(payload.ProviderId, payload.CandidateId)}\u001f{parserId}\u001f{parserVersion}\u001f{normalizationVersion}";
}
