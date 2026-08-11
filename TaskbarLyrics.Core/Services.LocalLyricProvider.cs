using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Lyricify.Lyrics.Models;
using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Core.Services;

public sealed class LocalLyricProvider : ILyricProvider, IDisposable
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".aac", ".wav", ".ogg", ".opus", ".wma"
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

    private static readonly Regex EnhancedLrcBoundaryRegex = new(
        @"<(?<minutes>\d+):(?<seconds>\d+)(?:[\.:](?<fraction>\d{1,3}))?>",
        RegexOptions.Compiled);

    private static readonly Regex CreditRegex = new(
        @"^\s*(作词|作曲|编曲|词|曲|Composer|Lyricist|Lyrics?|Music|Arranger|Producer|Written\s+by|Composed\s+by)\s*[:：]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly TimeSpan OpeningCreditWindow = TimeSpan.FromSeconds(5);
    private readonly List<string> _rootFolders;
    private readonly object _indexLock = new();
    private readonly List<LocalLyricEntry> _index = new();
    private readonly ILocalMediaIndex _mediaIndex;
    private int _sharedIndexVersion = -1;
    private int _isDisposed;

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
        _mediaIndex = LocalMediaIndexRegistry.Acquire(_rootFolders);
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
        if (Volatile.Read(ref _isDisposed) != 0 ||
            _rootFolders.Count == 0 ||
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
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return Array.Empty<LocalLyricEntry>();
        }

        var sharedSnapshot = _mediaIndex.GetSnapshot();
        lock (_indexLock)
        {
            if (_sharedIndexVersion != sharedSnapshot.Version)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var entries = new List<LocalLyricEntry>();
                foreach (var file in sharedSnapshot.Files)
                {
                    TryAddEntry(seen, entries, file.Path);
                }

                _index.Clear();
                _index.AddRange(entries);
                _sharedIndexVersion = sharedSnapshot.Version;
            }

            return _index.ToList();
        }
    }

    private static void TryAddEntry(HashSet<string> seen, ICollection<LocalLyricEntry> entries, string lyricPath)
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

        entries.Add(new LocalLyricEntry(lyricPath, stem, artist, title));
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
            var sourceText = textStart < line.Length ? line[textStart..] : string.Empty;
            var text = CleanText(sourceText);
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

                rawLines.Add(new LyricLine(
                    timestamp,
                    text,
                    Syllables: TryParseEnhancedLrcSyllables(sourceText, timestamp, offsetMs)));
            }
        }

        return AlignDuplicateTimestamps(rawLines);
    }

    private static TimeSpan ParseTimestamp(Match match)
    {
        var minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
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
            1 => int.Parse(fractionRaw, CultureInfo.InvariantCulture) * 100,
            2 => int.Parse(fractionRaw, CultureInfo.InvariantCulture) * 10,
            _ => int.Parse(fractionRaw[..3], CultureInfo.InvariantCulture)
        };
    }

    private static List<LyricSyllable>? TryParseEnhancedLrcSyllables(
        string sourceText,
        TimeSpan lineTimestamp,
        int offsetMs)
    {
        var boundaries = EnhancedLrcBoundaryRegex.Matches(sourceText);
        if (boundaries.Count == 0 || !AreEnhancedBoundarySpansComplete(sourceText, boundaries))
        {
            return null;
        }

        var syllables = new List<LyricSyllable>();
        var cursor = lineTimestamp;
        var textStart = 0;
        foreach (Match boundaryMatch in boundaries)
        {
            if (!TryParseEnhancedLrcBoundary(boundaryMatch, offsetMs, out var boundary) ||
                boundary < cursor)
            {
                return null;
            }

            var fragment = CleanText(sourceText[textStart..boundaryMatch.Index]);
            if (!string.IsNullOrWhiteSpace(fragment))
            {
                if (boundary == cursor)
                {
                    return null;
                }

                syllables.Add(new LyricSyllable(
                    cursor - lineTimestamp,
                    boundary - cursor,
                    fragment));
            }

            cursor = boundary;
            textStart = boundaryMatch.Index + boundaryMatch.Length;
        }

        return string.IsNullOrWhiteSpace(CleanText(sourceText[textStart..])) && syllables.Count > 0
            ? syllables
            : null;
    }

    private static bool AreEnhancedBoundarySpansComplete(
        string sourceText,
        MatchCollection boundaries)
    {
        var inlineTags = InlineTagRegex.Matches(sourceText);
        if (inlineTags.Count != boundaries.Count)
        {
            return false;
        }

        var cursor = 0;
        for (var index = 0; index < boundaries.Count; index++)
        {
            var boundary = boundaries[index];
            var inlineTag = inlineTags[index];
            if (boundary.Index != inlineTag.Index ||
                boundary.Length != inlineTag.Length ||
                boundary.Index < cursor)
            {
                return false;
            }

            var textBeforeBoundary = sourceText[cursor..boundary.Index];
            if (textBeforeBoundary.Contains('<') || textBeforeBoundary.Contains('>'))
            {
                return false;
            }

            cursor = boundary.Index + boundary.Length;
        }

        var textAfterBoundaries = sourceText[cursor..];
        return !textAfterBoundaries.Contains('<') && !textAfterBoundaries.Contains('>');
    }

    private static bool TryParseEnhancedLrcBoundary(
        Match boundaryMatch,
        int offsetMs,
        out TimeSpan timestamp)
    {
        timestamp = TimeSpan.Zero;
        if (!int.TryParse(boundaryMatch.Groups["minutes"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            !int.TryParse(boundaryMatch.Groups["seconds"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        try
        {
            timestamp = new TimeSpan(0, 0, minutes, seconds, ParseMilliseconds(boundaryMatch.Groups["fraction"].Value))
                .Add(TimeSpan.FromMilliseconds(offsetMs));
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }

        if (timestamp < TimeSpan.Zero)
        {
            timestamp = TimeSpan.Zero;
        }

        return true;
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _mediaIndex.Dispose();
        lock (_indexLock)
        {
            _index.Clear();
        }
    }

    private sealed record LocalLyricEntry(string LyricPath, string Stem, string Artist, string Title);

    private sealed record LocalLyricMatch(LocalLyricEntry Entry, int Score);
}
