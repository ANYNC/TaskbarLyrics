using System.Text;
using System.Text.RegularExpressions;
using Lyricify.Lyrics.Models;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Core.Services;

public sealed class LocalLyricProvider : ILyricProvider
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".aac", ".wav", ".ogg", ".opus", ".wma"
    };

    private static readonly HashSet<string> LyricExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lrc", ".qrc", ".krc"
    };

    private static readonly Regex TimestampRegex = new(
        @"\[(\d+):(\d+)(?:[\.:](\d{1,3}))?\]",
        RegexOptions.Compiled);

    private static readonly Regex OffsetRegex = new(
        @"\[offset\s*:\s*(?<value>[+-]?\d+)\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TrackNumberPrefixRegex = new(
        @"^\s*(?:\(?\d{1,3}\)?\s*[-._ ]*)+",
        RegexOptions.Compiled);

    private static readonly Regex BracketArtistFileRegex = new(
        @"^\s*\[(?<artist>[^\]]+)\]\s*(?<title>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex InlineTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    private static readonly Regex CreditRegex = new(
        @"^\s*(作词|作曲|编曲|词|曲|Composer|Lyricist|Lyrics?|Music|Arranger|Producer|Written\s+by|Composed\s+by)\s*[:：]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly TimeSpan OpeningCreditWindow = TimeSpan.FromSeconds(5);
    private readonly IReadOnlyList<string> _rootFolders;
    private readonly object _indexLock = new();
    private readonly List<LocalLyricEntry> _index = new();
    private readonly Task _indexTask;

    static LocalLyricProvider()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public LocalLyricProvider(IEnumerable<string>? rootFolders)
    {
        _rootFolders = (rootFolders ?? Enumerable.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim().Trim('"'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _indexTask = Task.Run(() => BuildIndexAsync(CancellationToken.None));
    }

    public string SourceApp => "Local";

    public async Task<LyricDocument?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        return (await GetLyricsWithDiagnosticsAsync(track, cancellationToken)).Document;
    }

    public Task<LyricFetchResult> GetLyricsWithDiagnosticsAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        if (_rootFolders.Count == 0 ||
            string.IsNullOrWhiteSpace(track.Title) ||
            string.Equals(track.Title, "Unknown Title", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        var index = SnapshotIndex();
        if (index.Count == 0)
        {
            return NotFound();
        }

        var best = FindBestMatch(track, index);
        if (best is null)
        {
            return NotFound();
        }

        var lines = ParseLyricFile(best.Entry.LyricPath);
        if (lines.Count == 0)
        {
            Log.Info($"Local lyrics matched but parsed no timed lines: {best.Entry.LyricPath}");
            return NotFound();
        }

        Log.Info($"Local lyrics matched: {track.Title} - {track.Artist} => {best.Entry.LyricPath} ({best.Score})");
        return Task.FromResult(new LyricFetchResult(
            new LyricDocument(EnsureSyllables(lines), best.Score),
            LyricAcquisitionKind.LocalFile,
            stopwatch.ElapsedMilliseconds));

        Task<LyricFetchResult> NotFound()
        {
            return Task.FromResult(new LyricFetchResult(
                null,
                LyricAcquisitionKind.NotFound,
                stopwatch.ElapsedMilliseconds));
        }
    }

    private IReadOnlyList<LocalLyricEntry> SnapshotIndex()
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
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new List<LocalLyricEntry>();
            foreach (var folder in _rootFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (var file in SafeEnumerateFiles(folder, cancellationToken))
                {
                    var extension = Path.GetExtension(file);
                    if (LyricExtensions.Contains(extension))
                    {
                        TryAddEntry(seen, pending, file, readEmbeddedMetadata: false);
                        continue;
                    }

                    if (!AudioExtensions.Contains(extension))
                    {
                        continue;
                    }

                    TryAddEntry(seen, pending, file, readEmbeddedMetadata: false);

                    foreach (var lyricExtension in LyricExtensions)
                    {
                        var lyricPath = Path.ChangeExtension(file, lyricExtension);
                        if (File.Exists(lyricPath))
                        {
                            TryAddEntry(seen, pending, lyricPath, readEmbeddedMetadata: false);
                        }
                    }

                    if (pending.Count >= 200)
                    {
                        FlushPendingIndexEntries(pending);
                        await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            FlushPendingIndexEntries(pending);
            Log.Info($"Local lyrics background index built. Folders={_rootFolders.Count}, Entries={seen.Count}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Warn($"Local lyrics background index failed: {ex.Message}");
        }
    }

    private void FlushPendingIndexEntries(List<LocalLyricEntry> pending)
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

    private static void TryAddEntry(ISet<string> seen, ICollection<LocalLyricEntry> entries, string lyricPath, bool readEmbeddedMetadata)
    {
        if (!seen.Add(lyricPath))
        {
            return;
        }

        var stem = TrackNumberPrefixRegex.Replace(Path.GetFileNameWithoutExtension(lyricPath), string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(stem))
        {
            return;
        }

        var (artist, title) = SplitArtistTitle(stem);
        if (readEmbeddedMetadata && AudioExtensions.Contains(Path.GetExtension(lyricPath)))
        {
            var embeddedMetadata = TryExtractEmbeddedMetadata(lyricPath);
            title = string.IsNullOrWhiteSpace(embeddedMetadata.Title) ? title : embeddedMetadata.Title;
            artist = MergeArtists(artist, embeddedMetadata.Artist, embeddedMetadata.AlbumArtist);
        }

        entries.Add(new LocalLyricEntry(lyricPath, stem, artist, title));
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
            Log.Warn($"Local embedded metadata read failed: {path}, {ex.Message}");
            return (null, null, null);
        }
    }

    private static string MergeArtists(params string?[] artists)
    {
        return string.Join(" ", artists
            .Where(artist => !string.IsNullOrWhiteSpace(artist))
            .SelectMany(artist => artist!
                .Split([';', '/', ',', '、', '&'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase));
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

    private static LocalLyricMatch? FindBestMatch(TrackInfo track, IReadOnlyList<LocalLyricEntry> entries)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        LocalLyricMatch? best = null;
        foreach (var entry in entries)
        {
            if (stopwatch.ElapsedMilliseconds > 150)
            {
                break;
            }

            var score = ScoreEntry(track, entry);
            if (score < LyricMatchingPolicy.MinimumAcceptedMatchScore)
            {
                continue;
            }

            if (best is null || score > best.Score)
            {
                best = new LocalLyricMatch(entry, score);
            }
        }

        return best;
    }

    private static int ScoreEntry(TrackInfo track, LocalLyricEntry entry)
    {
        var score = LyricMatcher.Score(track, entry.Title, entry.Artist);
        if (score >= LyricMatchingPolicy.MinimumAcceptedMatchScore)
        {
            return score;
        }

        var normalizedStem = LyricMatcher.NormalizeForSearch(entry.Stem);
        var normalizedTitle = LyricMatcher.NormalizeForSearch(track.Title);
        var normalizedArtist = LyricMatcher.NormalizeForSearch(track.Artist);
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return score;
        }

        var titleHit = normalizedStem.Contains(normalizedTitle, StringComparison.Ordinal) ||
                       normalizedTitle.Contains(normalizedStem, StringComparison.Ordinal);
        var artistHit = string.IsNullOrWhiteSpace(normalizedArtist) ||
                        normalizedStem.Contains(normalizedArtist, StringComparison.Ordinal);

        if (titleHit && artistHit)
        {
            return Math.Max(score, 88);
        }

        if (titleHit)
        {
            return Math.Max(score, 82);
        }

        return score;
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

    private static List<LyricLine> ParseLyricFile(string path)
    {
        var extension = Path.GetExtension(path);
        if (AudioExtensions.Contains(extension))
        {
            var embeddedLyric = TryExtractEmbeddedLyricText(path);
            return string.IsNullOrWhiteSpace(embeddedLyric)
                ? new List<LyricLine>()
                : ParseLrc(embeddedLyric);
        }

        var text = DecodeText(File.ReadAllBytes(path));
        if (extension.Equals(".qrc", StringComparison.OrdinalIgnoreCase))
        {
            return ParseQrc(text);
        }

        if (extension.Equals(".krc", StringComparison.OrdinalIgnoreCase))
        {
            var krcLines = ParseKrc(text);
            if (krcLines.Count > 0)
            {
                return krcLines;
            }
        }

        return ParseLrc(text);
    }

    private static string? TryExtractEmbeddedLyricText(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return TryExtractVorbisComment(bytes, "LYRICS") ??
                   TryExtractVorbisComment(bytes, "SYNCEDLYRICS") ??
                   TryExtractVorbisComment(bytes, "UNSYNCEDLYRICS");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Local embedded lyric read failed: {path}, {ex.Message}");
            return null;
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

    private static List<LyricLine> ParseLrc(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return new List<LyricLine>();
        }

        var offsetMs = 0;
        var offsetMatch = OffsetRegex.Match(lrc);
        if (offsetMatch.Success && int.TryParse(offsetMatch.Groups["value"].Value, out var parsedOffset))
        {
            offsetMs = parsedOffset;
        }

        var rawLines = new List<LyricLine>();
        foreach (var line in lrc.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var matches = TimestampRegex.Matches(line);
            if (matches.Count == 0)
            {
                continue;
            }

            var textStart = matches[^1].Index + matches[^1].Length;
            var text = textStart < line.Length ? CleanText(line[textStart..]) : string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (Match match in matches)
            {
                var timestamp = ParseTimestamp(match).Add(TimeSpan.FromMilliseconds(offsetMs));
                if (timestamp < TimeSpan.Zero)
                {
                    timestamp = TimeSpan.Zero;
                }

                if (timestamp <= OpeningCreditWindow && CreditRegex.IsMatch(text))
                {
                    continue;
                }

                rawLines.Add(new LyricLine(timestamp, text));
            }
        }

        return AlignDuplicateTimestamps(rawLines);
    }

    private static TimeSpan ParseTimestamp(Match match)
    {
        var minutes = int.Parse(match.Groups[1].Value);
        var seconds = int.Parse(match.Groups[2].Value);
        var milliseconds = ParseMilliseconds(match.Groups[3].Value);
        return new TimeSpan(0, 0, minutes, seconds, milliseconds);
    }

    private static int ParseMilliseconds(string fractionRaw)
    {
        if (string.IsNullOrWhiteSpace(fractionRaw))
        {
            return 0;
        }

        return fractionRaw.Length switch
        {
            1 => int.Parse(fractionRaw) * 100,
            2 => int.Parse(fractionRaw) * 10,
            _ => int.Parse(fractionRaw[..3])
        };
    }

    private static string CleanText(string text)
    {
        return InlineTagRegex
            .Replace(text, string.Empty)
            .Trim();
    }

    private static List<LyricLine> AlignDuplicateTimestamps(IEnumerable<LyricLine> rawLines)
    {
        return rawLines
            .Select((line, index) => new { Line = line, Index = index })
            .GroupBy(x => x.Line.Timestamp)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var ordered = group.OrderBy(x => x.Index).Select(x => x.Line).ToList();
                var primary = ordered[0];
                if (ordered.Count > 1)
                {
                    primary = primary with { Translation = ordered[1].Text };
                }

                return primary;
            })
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .ToList();
    }

    private static List<LyricLine> ParseQrc(string rawLyric)
    {
        try
        {
            var parsed = Lyricify.Lyrics.Parsers.QrcParser.Parse(rawLyric);
            var parsedLines = parsed?.Lines?
                .Where(line => !string.IsNullOrWhiteSpace(line.Text))
                .ToList();
            if (parsedLines is not { Count: > 0 })
            {
                return new List<LyricLine>();
            }

            var lines = new List<LyricLine>();
            foreach (var parsedLine in parsedLines)
            {
                var startMs = Math.Max(0, parsedLine.StartTime ?? 0);
                var text = CleanText(parsedLine.Text);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                List<LyricSyllable>? syllables = null;
                if (parsedLine is SyllableLineInfo syllableLine &&
                    syllableLine.Syllables is { Count: > 0 })
                {
                    syllables = syllableLine.Syllables
                        .Where(syllable => !string.IsNullOrEmpty(syllable.Text))
                        .Select(syllable =>
                        {
                            var syllableStartMs = Math.Max(0, syllable.StartTime - startMs);
                            var syllableDurationMs = Math.Max(1, syllable.EndTime - syllable.StartTime);
                            return new LyricSyllable(
                                TimeSpan.FromMilliseconds(syllableStartMs),
                                TimeSpan.FromMilliseconds(syllableDurationMs),
                                syllable.Text);
                        })
                        .ToList();
                }

                lines.Add(new LyricLine(TimeSpan.FromMilliseconds(startMs), text, Syllables: syllables));
            }

            return lines.OrderBy(line => line.Timestamp).ToList();
        }
        catch (Exception ex)
        {
            Log.Warn($"Local QRC parse failed: {ex.Message}");
            return new List<LyricLine>();
        }
    }

    private static List<LyricLine> ParseKrc(string rawLyric)
    {
        try
        {
            var parsed = Lyricify.Lyrics.Parsers.KrcParser.ParseLyrics(rawLyric);
            if (parsed is not { Count: > 0 })
            {
                return new List<LyricLine>();
            }

            return parsed
                .Where(line => line.StartTime is int && !string.IsNullOrWhiteSpace(line.Text))
                .Select(line => new LyricLine(TimeSpan.FromMilliseconds(Math.Max(0, line.StartTime!.Value)), CleanText(line.Text)))
                .OrderBy(line => line.Timestamp)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warn($"Local KRC parse failed: {ex.Message}");
            return new List<LyricLine>();
        }
    }

    private static List<LyricLine> EnsureSyllables(List<LyricLine> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Syllables is { Count: > 0 } || string.IsNullOrEmpty(line.Text))
            {
                continue;
            }

            var nextTimestamp = i + 1 < lines.Count
                ? lines[i + 1].Timestamp
                : line.Timestamp + TimeSpan.FromSeconds(5);
            var duration = Math.Clamp((nextTimestamp - line.Timestamp).TotalMilliseconds, 500, 10000);
            var msPerChar = duration / line.Text.Length;
            lines[i] = line with
            {
                Syllables = line.Text
                    .Select((character, index) => new LyricSyllable(
                        TimeSpan.FromMilliseconds(index * msPerChar),
                        TimeSpan.FromMilliseconds(msPerChar),
                        character.ToString()))
                    .ToList()
            };
        }

        return lines;
    }

    private sealed record LocalLyricEntry(string LyricPath, string Stem, string Artist, string Title);

    private sealed record LocalLyricMatch(LocalLyricEntry Entry, int Score);
}
