using System.Collections.Concurrent;
using System.Text.Json;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Core.Services;

public interface ILyricCacheStore<TPayload>
    where TPayload : class
{
    bool TryGet(string key, out TPayload? payload, out LyricAcquisitionKind acquisition);
    void Set(string key, TPayload payload);
    void Clear();
}

public sealed class JsonLyricCacheStore<TPayload> : ILyricCacheStore<TPayload>
    where TPayload : class
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    private readonly string _filePath;
    private readonly ConcurrentDictionary<string, TPayload> _memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _diskGate = new();
    private Dictionary<string, TPayload>? _disk;

    public JsonLyricCacheStore(string filePath)
    {
        _filePath = filePath;
    }

    public bool TryGet(string key, out TPayload? payload, out LyricAcquisitionKind acquisition)
    {
        if (_memory.TryGetValue(key, out var cached))
        {
            payload = cached;
            acquisition = LyricAcquisitionKind.MemoryCache;
            return true;
        }

        lock (_diskGate)
        {
            EnsureDiskLoaded();
            if (_disk!.TryGetValue(key, out cached))
            {
                _memory[key] = cached;
                payload = cached;
                acquisition = LyricAcquisitionKind.DiskCache;
                return true;
            }
        }

        payload = null;
        acquisition = LyricAcquisitionKind.Unknown;
        return false;
    }

    public void Set(string key, TPayload payload)
    {
        _memory[key] = payload;
        lock (_diskGate)
        {
            EnsureDiskLoaded();
            _disk![key] = payload;
            SaveDisk();
        }
    }

    public void Clear()
    {
        _memory.Clear();
        lock (_diskGate)
        {
            _disk = new Dictionary<string, TPayload>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Failed to clear lyric cache '{_filePath}': {exception.Message}");
            }
        }
    }

    private void EnsureDiskLoaded()
    {
        if (_disk is not null)
        {
            return;
        }

        try
        {
            _disk = !File.Exists(_filePath)
                ? new Dictionary<string, TPayload>(StringComparer.OrdinalIgnoreCase)
                : JsonSerializer.Deserialize<Dictionary<string, TPayload>>(File.ReadAllText(_filePath), SerializerOptions)
                    ?? new Dictionary<string, TPayload>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"Failed to read lyric cache '{_filePath}': {exception.Message}");
            _disk = new Dictionary<string, TPayload>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveDisk()
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
                JsonSerializer.Serialize(stream, _disk, SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
            temporaryPath = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"Failed to save lyric cache '{_filePath}': {exception.Message}");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Log.Warn($"Failed to remove lyric cache temp file '{temporaryPath}': {exception.Message}");
                }
            }
        }
    }
}
