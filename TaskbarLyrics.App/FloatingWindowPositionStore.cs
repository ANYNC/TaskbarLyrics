using System.IO;
using System.Text.Json;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

internal sealed record FloatingWindowPosition(string DisplayId, double X, double Y, bool UseScreenBounds = false)
{
    internal bool IsValid => !string.IsNullOrWhiteSpace(DisplayId) && double.IsFinite(X) && double.IsFinite(Y)
        && X is >= 0 and <= 1 && Y is >= 0 and <= 1;
}

internal sealed class FloatingWindowPositionStore
{
    private sealed record Document(int Version, Dictionary<string, FloatingWindowPosition>? Positions);
    private readonly string _path;
    private readonly Dictionary<string, FloatingWindowPosition> _positions = new(StringComparer.OrdinalIgnoreCase);
    private bool _writable = true;

    internal FloatingWindowPositionStore(string path)
    {
        _path = path;
        try
        {
            if (!File.Exists(path)) return;
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path));
            if (document is not { Version: 1 or 2, Positions: not null })
                throw new JsonException("Unsupported floating window position document.");
            foreach (var (key, position) in document.Positions)
            {
                if (string.IsNullOrWhiteSpace(key) || position is null || !position.IsValid)
                    throw new JsonException("Invalid floating window position.");
                // V1 coordinates were relative to the work area; retain that meaning until moved.
                _positions.Add(key, document.Version == 1 ? position with { UseScreenBounds = false } : position);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            _positions.Clear();
            _writable = false; // Preserve unreadable or newer data instead of replacing it on the next drag.
            Log.Warn($"Floating window positions could not be loaded: {ex.Message}");
        }
    }

    internal FloatingWindowPosition? Get(string key) => _positions.GetValueOrDefault(key);

    internal void Remember(string key, FloatingWindowPosition position)
    {
        if (string.IsNullOrWhiteSpace(key) || !position.IsValid) return;
        _positions[key] = position;
        Save();
    }

    internal void Forget(string key)
    {
        if (_positions.Remove(key)) Save();
    }

    internal void Clear()
    {
        if (_positions.Count == 0) return;
        _positions.Clear();
        Save();
    }

    private void Save()
    {
        if (!_writable) return;
        string? temporary = null;
        try
        {
            var path = Path.GetFullPath(_path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, new Document(2, _positions));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
            temporary = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            Log.Warn($"Floating window position could not be saved: {ex.Message}");
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { Log.Warn($"Floating position temporary file could not be removed: {ex.Message}"); }
            }
        }
    }
}
