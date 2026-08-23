using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Core.Services;

/// <summary>
/// Persists final lyric resolutions by normalized title and artist.
/// </summary>
public sealed class JsonResolvedLyricCache : IResolvedLyricCache, IDisposable
{
    public const int CurrentVersion = 1;
    public const string DefaultFileName = "resolved-lyrics-v1.json";
    public const string LegacyFileName = "user-lyric-bindings-v1.json";

    private const string CacheKeyVersion = "resolved-lyrics-key-v1";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly string _legacyFilePath;
    private readonly object _gate = new();
    private Dictionary<string, ResolvedLyricCacheEntry>? _entries;
    private readonly HashSet<string> _diskEntries = new(StringComparer.Ordinal);
    private bool _loadAttempted;
    private bool _disposed;

    public JsonResolvedLyricCache()
        : this(GetDefaultFilePath())
    {
    }

    public JsonResolvedLyricCache(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Resolved lyric cache path cannot be empty.", nameof(filePath));
        }

        _filePath = Path.GetFullPath(filePath);
        _legacyFilePath = GetLegacyFilePath(_filePath);
    }

    public static JsonResolvedLyricCache CreateDefault() =>
        new(GetDefaultFilePath());

    public bool TryGet(TrackInfo track, out ResolvedLyrics? resolvedLyrics)
    {
        resolvedLyrics = null;
        if (track is null || !TryCreateCacheKey(track.Title, track.Artist, out var key))
        {
            return false;
        }

        lock (_gate)
        {
            if (_disposed || !EnsureLoaded() || !_entries!.TryGetValue(key, out var entry))
            {
                return false;
            }

            var acquisition = _diskEntries.Remove(key)
                ? LyricAcquisitionKind.DiskCache
                : LyricAcquisitionKind.MemoryCache;
            if (!TryCreateResolvedLyrics(entry, acquisition, out resolvedLyrics))
            {
                RemoveInvalidEntry(key);
                resolvedLyrics = null;
                return false;
            }

            return true;
        }
    }

    public bool Store(TrackInfo track, ResolvedLyrics resolvedLyrics)
    {
        if (track is null || resolvedLyrics is null ||
            !TryCreateCacheKey(track.Title, track.Artist, out var key) ||
            !TryCreateEntry(track, resolvedLyrics, out var entry))
        {
            return false;
        }

        lock (_gate)
        {
            if (_disposed || !EnsureLoaded())
            {
                return false;
            }

            var updatedEntries = new Dictionary<string, ResolvedLyricCacheEntry>(_entries!, StringComparer.Ordinal)
            {
                [key] = entry
            };
            if (!PersistEntries(updatedEntries))
            {
                return false;
            }

            _entries = updatedEntries;
            _diskEntries.Remove(key);
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _entries = new Dictionary<string, ResolvedLyricCacheEntry>(StringComparer.Ordinal);
            _diskEntries.Clear();
            _loadAttempted = true;
            DeleteFile(_filePath, "resolved lyric cache");
            if (!string.Equals(_legacyFilePath, _filePath, StringComparison.OrdinalIgnoreCase))
            {
                DeleteFile(_legacyFilePath, "legacy user lyric binding cache");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _entries = null;
            _diskEntries.Clear();
        }
    }

    private static string GetDefaultFilePath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskbarLyrics",
            "database");
        return Path.Combine(directory, DefaultFileName);
    }

    private static string GetLegacyFilePath(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        return string.IsNullOrWhiteSpace(directory)
            ? Path.GetFullPath(LegacyFileName)
            : Path.Combine(directory, LegacyFileName);
    }

    private bool EnsureLoaded()
    {
        if (_loadAttempted)
        {
            return _entries is not null;
        }

        _loadAttempted = true;
        if (!File.Exists(_filePath))
        {
            return TryLoadLegacyOrInitializeEmpty();
        }

        try
        {
            ResolvedLyricCacheEnvelope? envelope;
            using (var stream = new FileStream(
                       _filePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       4096,
                       FileOptions.SequentialScan))
            {
                envelope = JsonSerializer.Deserialize<ResolvedLyricCacheEnvelope>(stream, SerializerOptions);
            }

            if (envelope is null ||
                envelope.Version != CurrentVersion ||
                envelope.Entries is null)
            {
                return MarkLoadFailure("version or structure is invalid");
            }

            var validEntries = new Dictionary<string, ResolvedLyricCacheEntry>(StringComparer.Ordinal);
            var invalidCount = 0;
            foreach (var pair in envelope.Entries)
            {
                if (!IsValidEntry(pair.Key, pair.Value))
                {
                    invalidCount++;
                    continue;
                }

                validEntries[pair.Key] = pair.Value!;
            }

            _entries = validEntries;
            _diskEntries.Clear();
            foreach (var key in validEntries.Keys)
            {
                _diskEntries.Add(key);
            }
            if (invalidCount > 0 && !PersistEntries(validEntries))
            {
                Log.Warn($"Invalid records in resolved lyric cache '{_filePath}' could not be removed from disk.");
            }

            return true;
        }
        catch (FileNotFoundException)
        {
            return TryLoadLegacyOrInitializeEmpty();
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            return MarkLoadFailure(exception.Message);
        }
    }

    private bool TryLoadLegacyOrInitializeEmpty()
    {
        if (!File.Exists(_legacyFilePath))
        {
            _entries = new Dictionary<string, ResolvedLyricCacheEntry>(StringComparer.Ordinal);
            return true;
        }

        if (!TryReadLegacyEntries(out var migratedEntries))
        {
            return MarkLoadFailure("legacy cache migration failed");
        }

        if (!PersistEntries(migratedEntries))
        {
            return MarkLoadFailure("legacy cache migration could not be persisted");
        }

        _entries = migratedEntries;
        _diskEntries.Clear();
        foreach (var key in migratedEntries.Keys)
        {
            _diskEntries.Add(key);
        }
        DeleteFile(_legacyFilePath, "legacy user lyric binding cache after migration");
        return true;
    }

    private bool TryReadLegacyEntries(
        out Dictionary<string, ResolvedLyricCacheEntry> migratedEntries)
    {
        migratedEntries = new Dictionary<string, ResolvedLyricCacheEntry>(StringComparer.Ordinal);
        try
        {
            using var stream = new FileStream(
                _legacyFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan);
            var envelope = JsonSerializer.Deserialize<LegacyStoreEnvelope>(stream, SerializerOptions);
            if (envelope is null ||
                envelope.Version != CurrentVersion ||
                envelope.Bindings is null)
            {
                return false;
            }

            foreach (var binding in envelope.Bindings)
            {
                if (!TryConvertLegacyBinding(binding, out var key, out var entry))
                {
                    migratedEntries = new Dictionary<string, ResolvedLyricCacheEntry>(StringComparer.Ordinal);
                    return false;
                }

                // The old store appended newer selections, so assignment order
                // intentionally gives the last record precedence.
                migratedEntries[key] = entry!;
            }

            return true;
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            Log.Warn($"Failed to read legacy user lyric bindings '{_legacyFilePath}': {exception.Message}");
            return false;
        }
    }

    private bool PersistEntries(IReadOnlyDictionary<string, ResolvedLyricCacheEntry> entries)
    {
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            temporaryPath = _filePath + $".{Guid.NewGuid():N}.tmp";
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(
                    stream,
                    new ResolvedLyricCacheEnvelope(CurrentVersion, entries),
                    SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
            temporaryPath = null;
            return true;
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            Log.Warn($"Failed to persist resolved lyric cache '{_filePath}': {exception.Message}");
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                DeleteFile(temporaryPath, "resolved lyric cache temporary file");
            }
        }
    }

    private void RemoveInvalidEntry(string key)
    {
        if (_entries is null || !_entries.Remove(key))
        {
            return;
        }

        if (!PersistEntries(_entries))
        {
            Log.Warn($"Invalid record '{key}' in resolved lyric cache '{_filePath}' could not be removed from disk.");
        }
    }

    private static bool TryCreateEntry(
        TrackInfo track,
        ResolvedLyrics resolvedLyrics,
        out ResolvedLyricCacheEntry entry)
    {
        entry = new ResolvedLyricCacheEntry(
            track.Id,
            track.Title,
            track.Artist,
            track.Album,
            track.SourceApp,
            track.Duration,
            track.SongId,
            resolvedLyrics.ProviderId.Value,
            resolvedLyrics.CandidateId,
            resolvedLyrics.Acquisition,
            resolvedLyrics.Diagnostics is null
                ? null
                : new Dictionary<string, string>(resolvedLyrics.Diagnostics, StringComparer.Ordinal),
            resolvedLyrics.Content);
        return IsValidEntryForTrack(track, entry);
    }

    private static bool TryConvertLegacyBinding(
        LegacyBindingRecord? binding,
        out string key,
        out ResolvedLyricCacheEntry? entry)
    {
        key = string.Empty;
        entry = null;
        if (binding is null ||
            !TryCreateCacheKey(binding.Title, binding.Artist, out key) ||
            binding.Duration <= TimeSpan.Zero)
        {
            return false;
        }

        entry = new ResolvedLyricCacheEntry(
            binding.TrackId,
            binding.Title,
            binding.Artist,
            binding.Album,
            binding.SourceApp,
            binding.Duration,
            binding.SongId,
            binding.ProviderId,
            binding.CandidateId,
            binding.Acquisition,
            binding.Diagnostics,
            binding.Content);
        if (!IsValidEntry(key, entry))
        {
            entry = null;
            return false;
        }

        return true;
    }

    private static bool IsValidEntry(string key, ResolvedLyricCacheEntry? entry)
    {
        return entry is not null &&
            TryCreateCacheKey(entry.Title, entry.Artist, out var expectedKey) &&
            string.Equals(key, expectedKey, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(entry.ProviderId) &&
            !string.IsNullOrWhiteSpace(entry.CandidateId) &&
            Enum.IsDefined(entry.Acquisition) &&
            entry.Diagnostics is not null &&
            entry.Diagnostics.All(pair =>
                !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null) &&
            IsValidContent(entry.Content);
    }

    private static bool IsValidEntryForTrack(TrackInfo track, ResolvedLyricCacheEntry entry) =>
        TryCreateCacheKey(track.Title, track.Artist, out var key) &&
        IsValidEntry(key, entry);

    private static bool TryCreateResolvedLyrics(
        ResolvedLyricCacheEntry entry,
        LyricAcquisitionKind acquisition,
        out ResolvedLyrics? resolvedLyrics)
    {
        resolvedLyrics = null;
        if (!IsValidContent(entry.Content) ||
            string.IsNullOrWhiteSpace(entry.ProviderId) ||
            string.IsNullOrWhiteSpace(entry.CandidateId) ||
            entry.Diagnostics is null)
        {
            return false;
        }

        try
        {
            resolvedLyrics = CreateResolvedLyrics(entry, acquisition);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ResolvedLyrics CreateResolvedLyrics(
        ResolvedLyricCacheEntry entry,
        LyricAcquisitionKind acquisition) =>
        new(
            entry.Content!,
            new LyricProviderId(entry.ProviderId!),
            entry.CandidateId!,
            acquisition,
            new Dictionary<string, string>(entry.Diagnostics!, StringComparer.Ordinal));

    private static bool IsValidContent(ParsedLyrics? content)
    {
        if (content is null ||
            !Enum.IsDefined(content.TimingKind) ||
            !Enum.IsDefined(content.TimingProvenance) ||
            !Enum.IsDefined(content.Format) ||
            content.Lines is null ||
            (!content.IsPureMusic && content.Lines.Count == 0))
        {
            return false;
        }

        foreach (var line in content.Lines)
        {
            if (line is null ||
                string.IsNullOrWhiteSpace(line.Text) ||
                line.StartTime < TimeSpan.Zero ||
                (line.EndTime is { } endTime && endTime < line.StartTime) ||
                line.Segments is null)
            {
                return false;
            }

            foreach (var segment in line.Segments)
            {
                if (segment is null ||
                    segment.StartTime < line.StartTime ||
                    segment.EndTime <= segment.StartTime ||
                    string.IsNullOrWhiteSpace(segment.Text) ||
                    (line.EndTime is { } lineEnd && segment.EndTime > lineEnd))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryCreateCacheKey(
        string? title,
        string? artist,
        out string key)
    {
        key = string.Empty;
        var normalizedTitle = NormalizeRequired(title);
        var normalizedArtist = NormalizeRequired(artist);
        if (normalizedTitle is null || normalizedArtist is null)
        {
            return false;
        }

        var canonical = string.Concat(
            CacheKeyVersion,
            ":title:", normalizedTitle.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ":", normalizedTitle,
            ":artist:", normalizedArtist.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ":", normalizedArtist);
        key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return true;
    }

    private static string? NormalizeRequired(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string normalized;
        try
        {
            normalized = value.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var builder = new StringBuilder(normalized.Length);
        var pendingWhitespace = false;
        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingWhitespace = builder.Length > 0;
                continue;
            }

            if (pendingWhitespace)
            {
                builder.Append(' ');
                pendingWhitespace = false;
            }

            builder.Append(character);
        }

        var result = builder.ToString().Trim();
        return string.IsNullOrEmpty(result) ? null : result.ToUpperInvariant();
    }

    private bool MarkLoadFailure(string detail)
    {
        _entries = null;
        Log.Warn($"Failed to read resolved lyric cache '{_filePath}': {detail}");
        return false;
    }

    private static void DeleteFile(string filePath, string description)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Failed to delete {description} '{filePath}': {exception.Message}");
        }
    }

    private static bool IsPersistenceFailure(Exception exception) =>
        exception is IOException or
        UnauthorizedAccessException or
        JsonException or
        NotSupportedException or
        InvalidOperationException or
        ArgumentException;

    private sealed record LegacyStoreEnvelope(
        int Version,
        IReadOnlyList<LegacyBindingRecord?>? Bindings);

    private sealed record LegacyBindingRecord(
        string? TrackId,
        string? Title,
        string? Artist,
        string? Album,
        string? SourceApp,
        TimeSpan Duration,
        string? SongId,
        string? ProviderId,
        string? CandidateId,
        LyricAcquisitionKind Acquisition,
        IReadOnlyDictionary<string, string>? Diagnostics,
        ParsedLyrics? Content);
}
