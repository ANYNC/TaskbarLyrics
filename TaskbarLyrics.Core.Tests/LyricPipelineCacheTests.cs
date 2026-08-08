using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricPipelineCacheTests
{
    [Theory]
    [InlineData(LyricPayloadFormat.Qrc, LyricTimingKind.WordTimed)]
    [InlineData(LyricPayloadFormat.Krc, LyricTimingKind.WordTimed)]
    [InlineData(LyricPayloadFormat.Yrc, LyricTimingKind.CharacterTimed)]
    public void JsonStoresRoundTripRawAndParsedProviderTiming(
        LyricPayloadFormat format,
        LyricTimingKind timingKind)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"taskbar-lyrics-pipeline-cache-{Guid.NewGuid():N}");
        var rawPath = Path.Combine(directory, "raw.json");
        var parsedPath = Path.Combine(directory, "parsed.json");
        var candidateId = $"candidate-{format}";
        var raw = CreateRawPayload(format, candidateId);
        var parsed = CreateParsedLyrics(format, timingKind);
        var fetchedAt = new DateTimeOffset(2026, 8, 8, 1, 2, 3, TimeSpan.Zero);

        try
        {
            var firstCache = new LyricPipelineCache(
                new JsonLyricCacheStore<RawLyricCacheEnvelope>(rawPath),
                new JsonLyricCacheStore<ParsedLyricCacheEnvelope>(parsedPath));

            firstCache.StoreRaw(raw, fetchedAt);
            firstCache.StoreParsed(raw, parsed, "parser", "parser-v1", "normalization-v1");

            var secondCache = new LyricPipelineCache(
                new JsonLyricCacheStore<RawLyricCacheEnvelope>(rawPath),
                new JsonLyricCacheStore<ParsedLyricCacheEnvelope>(parsedPath));

            Assert.True(
                secondCache.TryGetRaw(
                    raw.ProviderId,
                    raw.CandidateId,
                    out var restoredRaw,
                    out var rawAcquisition));
            Assert.Equal(LyricAcquisitionKind.DiskCache, rawAcquisition);
            Assert.NotNull(restoredRaw);
            Assert.Equal(raw.ProviderId, restoredRaw!.ProviderId);
            Assert.Equal(raw.CandidateId, restoredRaw.CandidateId);
            Assert.Equal(raw.Format, restoredRaw.Format);
            Assert.Equal(raw.OriginalLyrics, restoredRaw.OriginalLyrics);
            Assert.Equal(raw.TranslationLyrics, restoredRaw.TranslationLyrics);
            Assert.Equal(raw.IsEncrypted, restoredRaw.IsEncrypted);
            Assert.Equal(raw.IsPureMusic, restoredRaw.IsPureMusic);
            Assert.Equal(raw.HasStableIdentity, restoredRaw.HasStableIdentity);
            Assert.Equal(raw.Diagnostics["queryVariant"], restoredRaw.Diagnostics["queryVariant"]);

            Assert.True(
                secondCache.TryGetParsed(
                    restoredRaw,
                    "parser",
                    "parser-v1",
                    "normalization-v1",
                    out var restoredParsed,
                    out var parsedAcquisition));
            Assert.Equal(LyricAcquisitionKind.DiskCache, parsedAcquisition);
            Assert.NotNull(restoredParsed);
            Assert.Equal(parsed.TimingKind, restoredParsed!.TimingKind);
            Assert.Equal(parsed.TimingProvenance, restoredParsed.TimingProvenance);
            Assert.Equal(parsed.Format, restoredParsed.Format);
            Assert.Equal(parsed.IsPureMusic, restoredParsed.IsPureMusic);

            var expectedLine = Assert.Single(parsed.Lines);
            var actualLine = Assert.Single(restoredParsed.Lines);
            Assert.Equal(expectedLine.StartTime, actualLine.StartTime);
            Assert.Equal(expectedLine.EndTime, actualLine.EndTime);
            Assert.Equal(expectedLine.Text, actualLine.Text);
            Assert.Equal(expectedLine.Translation, actualLine.Translation);
            Assert.False(actualLine.IsInformationLine);
            Assert.Equal(expectedLine.Segments.Count, actualLine.Segments.Count);
            Assert.Equal(expectedLine.Segments[0].StartTime, actualLine.Segments[0].StartTime);
            Assert.Equal(expectedLine.Segments[0].EndTime, actualLine.Segments[0].EndTime);
            Assert.Equal(expectedLine.Segments[0].Text, actualLine.Segments[0].Text);
            Assert.Equal(expectedLine.Segments[1].StartTime, actualLine.Segments[1].StartTime);
            Assert.Equal(expectedLine.Segments[1].EndTime, actualLine.Segments[1].EndTime);
            Assert.Equal(expectedLine.Segments[1].Text, actualLine.Segments[1].Text);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ParserOrNormalizationVersionMissLeavesRawCacheReusable()
    {
        var rawStore = new InMemoryLyricCacheStore<RawLyricCacheEnvelope>();
        var parsedStore = new InMemoryLyricCacheStore<ParsedLyricCacheEnvelope>();
        var cache = new LyricPipelineCache(rawStore, parsedStore);
        var raw = CreateRawPayload(LyricPayloadFormat.Qrc, "stable-version-candidate");

        cache.StoreRaw(raw, DateTimeOffset.UtcNow);
        cache.StoreParsed(
            raw,
            CreateParsedLyrics(LyricPayloadFormat.Qrc, LyricTimingKind.WordTimed),
            "parser",
            "parser-v1",
            "normalization-v1");

        Assert.False(
            cache.TryGetParsed(
                raw,
                "parser",
                "parser-v2",
                "normalization-v1",
                out _,
                out var parserMissAcquisition));
        Assert.Equal(LyricAcquisitionKind.Unknown, parserMissAcquisition);
        Assert.Empty(parsedStore.RemovedKeys);

        Assert.True(
            cache.TryGetRaw(
                raw.ProviderId,
                raw.CandidateId,
                out var rawAfterParserMiss,
                out var parserMissRawAcquisition));
        Assert.Equal(LyricAcquisitionKind.MemoryCache, parserMissRawAcquisition);
        Assert.NotNull(rawAfterParserMiss);

        Assert.False(
            cache.TryGetParsed(
                rawAfterParserMiss!,
                "parser",
                "parser-v1",
                "normalization-v2",
                out _,
                out var normalizationMissAcquisition));
        Assert.Equal(LyricAcquisitionKind.Unknown, normalizationMissAcquisition);
        Assert.Empty(parsedStore.RemovedKeys);

        Assert.True(
            cache.TryGetRaw(
                raw.ProviderId,
                raw.CandidateId,
                out _,
                out var normalizationMissRawAcquisition));
        Assert.Equal(LyricAcquisitionKind.MemoryCache, normalizationMissRawAcquisition);
    }

    [Fact]
    public void CorruptedRawAndParsedEntriesAreRemovedThroughStores()
    {
        var rawStore = new InMemoryLyricCacheStore<RawLyricCacheEnvelope>();
        var parsedStore = new InMemoryLyricCacheStore<ParsedLyricCacheEnvelope>();
        var cache = new LyricPipelineCache(rawStore, parsedStore);
        var providerId = KnownLyricProviders.QQMusic;
        var rawCandidateId = "corrupted-raw-candidate";
        var rawKey = $"{providerId.Value}:{rawCandidateId}";

        rawStore.Store(
            rawKey,
            new RawLyricCacheEnvelope(
                LyricPipelineCache.RawCacheVersion,
                providerId.Value,
                rawCandidateId,
                LyricPayloadFormat.Lrc,
                "not-the-payload-hash",
                DateTimeOffset.UtcNow,
                "synthetic raw",
                null,
                false,
                false,
                new Dictionary<string, string>()));

        Assert.False(cache.TryGetRaw(providerId, rawCandidateId, out _, out var rawAcquisition));
        Assert.Equal(LyricAcquisitionKind.Unknown, rawAcquisition);
        Assert.Contains(rawKey, rawStore.RemovedKeys);

        var parsedRaw = CreateRawPayload(LyricPayloadFormat.Krc, "corrupted-parsed-candidate");
        var parsedKey = $"{parsedRaw.ProviderId.Value}:{parsedRaw.CandidateId}";
        parsedStore.Store(
            parsedKey,
            new ParsedLyricCacheEnvelope(
                LyricPipelineCache.ParsedCacheVersion,
                parsedRaw.ProviderId.Value,
                parsedRaw.CandidateId,
                LyricPipelineCache.ComputePayloadHash(parsedRaw),
                "parser",
                "parser-v1",
                "normalization-v1",
                new ParsedLyrics(
                    [],
                    LyricTimingKind.LineTimed,
                    LyricTimingProvenance.ProviderSupplied,
                    parsedRaw.Format)));

        Assert.False(
            cache.TryGetParsed(
                parsedRaw,
                "parser",
                "parser-v1",
                "normalization-v1",
                out _,
                out var parsedAcquisition));
        Assert.Equal(LyricAcquisitionKind.Unknown, parsedAcquisition);
        Assert.Contains(parsedKey, parsedStore.RemovedKeys);
    }

    [Fact]
    public void UnstableIdentityNeverPersistsRawOrParsed()
    {
        var rawStore = new InMemoryLyricCacheStore<RawLyricCacheEnvelope>();
        var parsedStore = new InMemoryLyricCacheStore<ParsedLyricCacheEnvelope>();
        var cache = new LyricPipelineCache(rawStore, parsedStore);
        var raw = CreateRawPayload(
            LyricPayloadFormat.Lrc,
            "unstable-candidate",
            hasStableIdentity: false);

        cache.StoreRaw(raw, DateTimeOffset.UtcNow);
        cache.StoreParsed(
            raw,
            CreateParsedLyrics(LyricPayloadFormat.Lrc, LyricTimingKind.LineTimed),
            "parser",
            "parser-v1",
            "normalization-v1");

        Assert.Empty(rawStore.Keys);
        Assert.Empty(parsedStore.Keys);
        Assert.False(
            cache.TryGetParsed(
                raw,
                "parser",
                "parser-v1",
                "normalization-v1",
                out _,
                out var acquisition));
        Assert.Equal(LyricAcquisitionKind.Unknown, acquisition);
    }

    [Fact]
    public void CacheIdentityUsesProviderAndCandidateInsteadOfRelaxedQuery()
    {
        var rawStore = new InMemoryLyricCacheStore<RawLyricCacheEnvelope>();
        var parsedStore = new InMemoryLyricCacheStore<ParsedLyricCacheEnvelope>();
        var cache = new LyricPipelineCache(rawStore, parsedStore);
        var first = CreateRawPayload(
            LyricPayloadFormat.Qrc,
            "stable-query-candidate",
            queryVariant: "exact title artist");
        var second = CreateRawPayload(
            LyricPayloadFormat.Qrc,
            "stable-query-candidate",
            queryVariant: "relaxed title artist");

        cache.StoreRaw(first, DateTimeOffset.UtcNow);
        cache.StoreRaw(second, DateTimeOffset.UtcNow);
        cache.StoreParsed(
            first,
            CreateParsedLyrics(LyricPayloadFormat.Qrc, LyricTimingKind.WordTimed),
            "parser",
            "parser-v1",
            "normalization-v1");
        cache.StoreParsed(
            second,
            CreateParsedLyrics(LyricPayloadFormat.Qrc, LyricTimingKind.WordTimed),
            "parser",
            "parser-v1",
            "normalization-v1");

        var rawKey = Assert.Single(rawStore.Keys);
        var parsedKey = Assert.Single(parsedStore.Keys);
        Assert.Equal("QQMusic:stable-query-candidate", rawKey);
        Assert.Equal(rawKey, parsedKey);
        Assert.DoesNotContain("relaxed title artist", rawKey, StringComparison.Ordinal);
    }

    private static RawLyricPayload CreateRawPayload(
        LyricPayloadFormat format,
        string candidateId,
        bool hasStableIdentity = true,
        string queryVariant = "relaxed title artist") =>
        new(
            KnownLyricProviders.QQMusic,
            candidateId,
            format,
            $"[{format}] synthetic original lyrics",
            $"[{format}] synthetic translation",
            isEncrypted: false,
            isPureMusic: false,
            new Dictionary<string, string>
            {
                ["queryVariant"] = queryVariant,
                ["source"] = "synthetic"
            },
            hasStableIdentity);

    private static ParsedLyrics CreateParsedLyrics(
        LyricPayloadFormat format,
        LyricTimingKind timingKind)
    {
        var lineStart = TimeSpan.FromSeconds(1);
        var lineEnd = TimeSpan.FromSeconds(2);
        return new ParsedLyrics(
            [
                new ParsedLyricLine(
                    lineStart,
                    lineEnd,
                    "synthetic provider timing",
                    translation: "synthetic translation",
                    segments:
                    [
                        new ParsedLyricSegment(
                            lineStart,
                            lineStart + TimeSpan.FromMilliseconds(250),
                            "synthetic"),
                        new ParsedLyricSegment(
                            lineStart + TimeSpan.FromMilliseconds(250),
                            lineStart + TimeSpan.FromMilliseconds(700),
                            "provider")
                    ])
            ],
            timingKind,
            LyricTimingProvenance.ProviderSupplied,
            format);
    }

    private sealed class InMemoryLyricCacheStore<TPayload> : ILyricCacheStore<TPayload>
        where TPayload : class
    {
        private readonly Dictionary<string, TPayload> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _removedKeys = [];

        public IReadOnlyCollection<string> Keys => _entries.Keys.ToArray();

        public IReadOnlyList<string> RemovedKeys => _removedKeys;

        public bool TryGet(
            string key,
            out TPayload? payload,
            out LyricAcquisitionKind acquisition)
        {
            if (_entries.TryGetValue(key, out var found))
            {
                payload = found;
                acquisition = LyricAcquisitionKind.MemoryCache;
                return true;
            }

            payload = null;
            acquisition = LyricAcquisitionKind.Unknown;
            return false;
        }

        public void Store(string key, TPayload payload) => _entries[key] = payload;

        public void Remove(string key)
        {
            _removedKeys.Add(key);
            _entries.Remove(key);
        }

        public void Clear()
        {
            _entries.Clear();
            _removedKeys.Clear();
        }
    }
}
