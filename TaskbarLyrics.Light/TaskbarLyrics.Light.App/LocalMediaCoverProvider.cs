using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Light.App;

internal sealed class LocalMediaCoverProvider
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".aac", ".wav", ".ogg", ".opus", ".wma"
    };

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] CommonCoverNames = ["cover", "folder", "front", "album"];

    private static readonly Regex TrackNumberPrefixRegex = new(
        @"^\s*(?:\(?\d{1,3}\)?\s*[-._ ]*)+",
        RegexOptions.Compiled);

    private static readonly Regex BracketArtistFileRegex = new(
        @"^\s*\[(?<artist>[^\]]+)\]\s*(?<title>.+)$",
        RegexOptions.Compiled);

    private readonly IReadOnlyList<string> _rootFolders;
    private readonly object _indexLock = new();
    private readonly Dictionary<string, byte[]?> _coverCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LocalMediaEntry> _index = new();
    private readonly Task _indexTask;

    static LocalMediaCoverProvider()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public LocalMediaCoverProvider(IEnumerable<string>? rootFolders)
    {
        _rootFolders = (rootFolders ?? Enumerable.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim().Trim('"'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _indexTask = Task.Run(() => BuildIndexAsync(CancellationToken.None));
    }

    public byte[]? TryGetCover(TrackInfo? track, CancellationToken cancellationToken = default)
    {
        if (track is null ||
            _rootFolders.Count == 0 ||
            string.IsNullOrWhiteSpace(track.Title) ||
            string.Equals(track.Title, "Unknown Title", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var index = SnapshotIndex();
        if (index.Count == 0)
        {
            return null;
        }

        var entry = FindBestMatch(track, index);
        if (entry is null)
        {
            return null;
        }

        lock (_coverCache)
        {
            if (_coverCache.TryGetValue(entry.Path, out var cachedCover))
            {
                return cachedCover;
            }
        }

        var cover = TryReadSidecarCover(entry.Path) ?? TryReadEmbeddedCover(entry.Path);
        lock (_coverCache)
        {
            _coverCache[entry.Path] = cover;
        }

        return cover;
    }

    private IReadOnlyList<LocalMediaEntry> SnapshotIndex()
    {
        lock (_indexLock)
        {
            return _index.ToList();
        }
    }

    private async Task BuildIndexAsync(CancellationToken cancellationToken)
    {
        try
        {
            var pending = new List<LocalMediaEntry>();
            foreach (var folder in _rootFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (var file in SafeEnumerateFiles(folder, cancellationToken))
                {
                    if (!AudioExtensions.Contains(Path.GetExtension(file)))
                    {
                        continue;
                    }

                    var entry = CreateEntry(file);
                    pending.Add(entry);
                    if (pending.Count >= 200)
                    {
                        FlushPendingIndexEntries(pending);
                        await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            FlushPendingIndexEntries(pending);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private void FlushPendingIndexEntries(List<LocalMediaEntry> pending)
    {
        if (pending.Count == 0)
        {
            return;
        }

        lock (_indexLock)
        {
            _index.AddRange(pending);
        }

        pending.Clear();
    }

    private static LocalMediaEntry CreateEntry(string path)
    {
        var stem = TrackNumberPrefixRegex.Replace(Path.GetFileNameWithoutExtension(path), string.Empty).Trim();
        var (fileArtist, fileTitle) = SplitArtistTitle(stem);
        return new LocalMediaEntry(path, stem, fileArtist, fileTitle);
    }

    private static LocalMediaEntry? FindBestMatch(TrackInfo track, IReadOnlyList<LocalMediaEntry> entries)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        LocalMediaEntry? bestEntry = null;
        var bestScore = 0;
        foreach (var entry in entries)
        {
            if (stopwatch.ElapsedMilliseconds > 80)
            {
                break;
            }

            var score = ScoreEntry(track, entry);
            if (score < 70)
            {
                continue;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestEntry = entry;
            }
        }

        return bestEntry;
    }

    private static int ScoreEntry(TrackInfo track, LocalMediaEntry entry)
    {
        var score = LyricMatcher.Score(track, entry.Title, entry.Artist);
        if (score >= 70)
        {
            return score;
        }

        var normalizedStem = LyricMatcher.NormalizeForSearch(entry.Stem);
        var normalizedTitle = LyricMatcher.NormalizeForSearch(track.Title);
        if (!string.IsNullOrWhiteSpace(normalizedTitle) &&
            (normalizedStem.Contains(normalizedTitle, StringComparison.Ordinal) ||
             normalizedTitle.Contains(normalizedStem, StringComparison.Ordinal)))
        {
            return Math.Max(score, 88);
        }

        return score;
    }

    private static byte[]? TryReadSidecarCover(string audioPath)
    {
        var directory = Path.GetDirectoryName(audioPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(audioPath);
        foreach (var name in CommonCoverNames.Prepend(stem))
        {
            foreach (var extension in ImageExtensions)
            {
                var imagePath = Path.Combine(directory, name + extension);
                if (!File.Exists(imagePath))
                {
                    continue;
                }

                try
                {
                    return File.ReadAllBytes(imagePath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return null;
                }
            }
        }

        return null;
    }

    private static byte[]? TryReadEmbeddedCover(string audioPath)
    {
        try
        {
            var bytes = File.ReadAllBytes(audioPath);
            return TryExtractJpeg(bytes) ?? TryExtractPng(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static byte[]? TryExtractJpeg(byte[] bytes)
    {
        var start = IndexOf(bytes, [0xFF, 0xD8, 0xFF]);
        if (start < 0)
        {
            return null;
        }

        for (var i = start + 3; i < bytes.Length - 1; i++)
        {
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xD9)
            {
                var length = i + 2 - start;
                return length is > 128 and < 10 * 1024 * 1024
                    ? bytes[start..(start + length)]
                    : null;
            }
        }

        return null;
    }

    private static byte[]? TryExtractPng(byte[] bytes)
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var start = IndexOf(bytes, signature);
        if (start < 0)
        {
            return null;
        }

        byte[] endMarker = [0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];
        var end = IndexOf(bytes, endMarker, start + signature.Length);
        if (end < 0)
        {
            return null;
        }

        var length = end + endMarker.Length - start;
        return length is > 128 and < 10 * 1024 * 1024
            ? bytes[start..(start + length)]
            : null;
    }

    private static (string? Title, string? Artist, string? AlbumArtist) TryExtractEmbeddedMetadata(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return (
                TryExtractVorbisComment(bytes, "TITLE"),
                TryExtractVorbisComment(bytes, "ARTIST"),
                TryExtractVorbisComment(bytes, "ALBUMARTIST"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null, null);
        }
    }

    private static string? TryExtractVorbisComment(byte[] bytes, string key)
    {
        var marker = Encoding.ASCII.GetBytes(key + "=");
        var index = IndexOfAsciiIgnoreCase(bytes, marker);
        while (index >= 4)
        {
            var commentLength = BitConverter.ToInt32(bytes, index - 4);
            if (commentLength >= marker.Length &&
                commentLength <= 5 * 1024 * 1024 &&
                index + commentLength <= bytes.Length)
            {
                var comment = DecodeText(bytes.AsSpan(index, commentLength).ToArray());
                var separatorIndex = comment.IndexOf('=');
                if (separatorIndex >= 0 &&
                    string.Equals(comment[..separatorIndex], key, StringComparison.OrdinalIgnoreCase))
                {
                    return comment[(separatorIndex + 1)..];
                }
            }

            var nextStart = index + marker.Length;
            var next = IndexOfAsciiIgnoreCase(bytes.AsSpan(nextStart).ToArray(), marker);
            index = next >= 0 ? nextStart + next : -1;
        }

        return null;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string rootFolder, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(rootFolder);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(folder);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                pending.Push(directory);
            }
        }
    }

    private static (string Artist, string Title) SplitArtistTitle(string stem)
    {
        var bracketArtist = BracketArtistFileRegex.Match(stem);
        if (bracketArtist.Success)
        {
            return (
                bracketArtist.Groups["artist"].Value.Trim(),
                bracketArtist.Groups["title"].Value.Trim());
        }

        var separators = new[] { " - ", " – ", " — ", " _ " };
        foreach (var separator in separators)
        {
            var index = stem.IndexOf(separator, StringComparison.Ordinal);
            if (index <= 0 || index + separator.Length >= stem.Length)
            {
                continue;
            }

            return (stem[..index].Trim(), stem[(index + separator.Length)..].Trim());
        }

        return (string.Empty, stem);
    }

    private static string MergeArtists(params string?[] artists)
    {
        return string.Join(" ", artists
            .Where(artist => !string.IsNullOrWhiteSpace(artist))
            .SelectMany(artist => artist!
                .Split([';', '/', ',', '、', '&'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string DecodeText(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            try
            {
                return Encoding.GetEncoding(936).GetString(bytes);
            }
            catch
            {
                return Encoding.UTF8.GetString(bytes);
            }
        }
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int startIndex = 0)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return -1;
        }

        for (var i = Math.Max(0, startIndex); i <= haystack.Length - needle.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfAsciiIgnoreCase(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return -1;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < needle.Length; j++)
            {
                var left = haystack[i + j];
                var right = needle[j];
                if (left >= (byte)'a' && left <= (byte)'z')
                {
                    left = (byte)(left - 32);
                }

                if (right >= (byte)'a' && right <= (byte)'z')
                {
                    right = (byte)(right - 32);
                }

                if (left != right)
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return i;
            }
        }

        return -1;
    }

    private sealed record LocalMediaEntry(string Path, string Stem, string Artist, string Title);
}
