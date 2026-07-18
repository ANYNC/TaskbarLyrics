using System.Text;
using Microsoft.EntityFrameworkCore;
using TaskbarLyrics.Core.Database;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Core.Services;

public sealed class TrackLyricOffsetStore : IDisposable
{
    public const int MinimumOffsetMilliseconds = -5000;
    public const int MaximumOffsetMilliseconds = 5000;
    private const int DurationBucketSizeSeconds = 2;
    private const int DurationToleranceSeconds = 4;

    private readonly object _syncRoot = new();
    private readonly Dictionary<TrackLyricOffsetKey, TrackLyricOffsetSnapshot> _offsets = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private bool _disposed;

    public TrackLyricOffsetStore()
    {
        Load();
    }

    public int GetOffsetMilliseconds(TrackInfo? track, string? lyricSource)
    {
        return Resolve(track, lyricSource).OffsetMilliseconds;
    }

    public TrackLyricOffsetResolution Resolve(TrackInfo? track, string? lyricSource)
    {
        if (track is null || !TryCreateIdentity(track, lyricSource, out var identity))
        {
            return TrackLyricOffsetResolution.None;
        }

        lock (_syncRoot)
        {
            if (_offsets.TryGetValue(identity.Key, out var exact))
            {
                return CreateResolution(exact, TrackLyricOffsetMatchKind.Exact);
            }

            if (identity.DurationBucketSeconds > 0)
            {
                var nearby = _offsets
                    .Where(pair => IsSameTrackAndSource(pair.Key, identity.Key) &&
                        pair.Key.DurationBucketSeconds > 0 &&
                        Math.Abs(pair.Key.DurationBucketSeconds - identity.DurationBucketSeconds) <= DurationToleranceSeconds)
                    .OrderBy(pair => Math.Abs(pair.Key.DurationBucketSeconds - identity.DurationBucketSeconds))
                    .ThenByDescending(pair => pair.Value.UpdatedAtUtc)
                    .Select(pair => pair.Value)
                    .FirstOrDefault();

                if (nearby is not null)
                {
                    return CreateResolution(nearby, TrackLyricOffsetMatchKind.NearbyDuration);
                }

                var unknownDurationKey = identity.Key with { DurationBucketSeconds = 0 };
                if (_offsets.TryGetValue(unknownDurationKey, out var unknownDuration))
                {
                    return CreateResolution(unknownDuration, TrackLyricOffsetMatchKind.UnknownDurationFallback);
                }
            }

            return TrackLyricOffsetResolution.None;
        }
    }

    public async Task<TrackLyricOffsetPage> QueryEntriesAsync(
        int page,
        int pageSize,
        string? search,
        string? lyricSource,
        TrackLyricOffsetSort sort,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        await using var context = new UserDataDbContext();
        var allEntries = context.TrackLyricOffsets.AsNoTracking();
        var unfilteredCount = await allEntries.CountAsync(cancellationToken).ConfigureAwait(false);
        var sources = await allEntries
            .Where(offset => offset.LyricSource != string.Empty)
            .Select(offset => offset.LyricSource)
            .Distinct()
            .OrderBy(source => source)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IQueryable<TrackLyricOffset> query = allEntries;
        if (!string.IsNullOrWhiteSpace(lyricSource))
        {
            var selectedSource = lyricSource.Trim();
            query = query.Where(offset => offset.LyricSource == selectedSource);
        }

        foreach (var token in SplitSearchTerms(search))
        {
            var normalizedToken = NormalizeIdentityPart(token);
            if (normalizedToken.Length == 0)
            {
                continue;
            }

            query = query.Where(offset =>
                offset.NormalizedTitle.Contains(normalizedToken) ||
                offset.NormalizedArtist.Contains(normalizedToken));
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, pageCount);

        var orderedQuery = sort switch
        {
            TrackLyricOffsetSort.Title => query
                .OrderBy(offset => offset.DisplayTitle)
                .ThenBy(offset => offset.DisplayArtist)
                .ThenByDescending(offset => offset.UpdatedAtUtcStorage),
            TrackLyricOffsetSort.OffsetMagnitude => query
                .OrderByDescending(offset => Math.Abs(offset.OffsetMilliseconds))
                .ThenByDescending(offset => offset.UpdatedAtUtcStorage),
            _ => query.OrderByDescending(offset => offset.UpdatedAtUtcStorage)
        };
        var records = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var entries = records
            .Select(record => new TrackLyricOffsetEntry(
                new TrackLyricOffsetRecordKey(
                    record.NormalizedTitle,
                    record.NormalizedArtist,
                    record.NormalizedLyricSource,
                    record.DurationBucketSeconds),
                record.DisplayTitle,
                record.DisplayArtist,
                record.Album,
                record.SourceApp,
                record.LyricSource,
                record.SongId,
                record.OffsetMilliseconds,
                record.UpdatedAtUtc))
            .ToArray();

        return new TrackLyricOffsetPage(
            entries,
            sources,
            page,
            pageSize,
            pageCount,
            totalCount,
            unfilteredCount);
    }

    public Task<TrackLyricOffsetSaveResult> SetOffsetAsync(
        TrackInfo track,
        string lyricSource,
        int offsetMilliseconds,
        CancellationToken cancellationToken = default)
    {
        if (!TryCreateIdentity(track, lyricSource, out var identity))
        {
            return Task.FromResult(new TrackLyricOffsetSaveResult(false, "当前歌曲或歌词源尚不可用。"));
        }

        var snapshot = new TrackLyricOffsetSnapshot(
            identity.Key,
            track.Title.Trim(),
            track.Artist.Trim(),
            track.Album?.Trim() ?? string.Empty,
            track.SourceApp?.Trim() ?? string.Empty,
            lyricSource.Trim(),
            track.SongId?.Trim() ?? string.Empty,
            NormalizeOffset(offsetMilliseconds),
            DateTimeOffset.UtcNow);
        return SetSnapshotAsync(snapshot, cancellationToken);
    }

    public Task<TrackLyricOffsetSaveResult> SetOffsetAsync(
        TrackLyricOffsetRecordKey recordKey,
        int offsetMilliseconds,
        CancellationToken cancellationToken = default)
    {
        TrackLyricOffsetSnapshot? existing;
        lock (_syncRoot)
        {
            _offsets.TryGetValue(ToInternalKey(recordKey), out existing);
        }

        if (existing is null)
        {
            return Task.FromResult(new TrackLyricOffsetSaveResult(false, "没有找到要修改的单曲偏移记录。"));
        }

        return SetSnapshotAsync(
            existing with
            {
                OffsetMilliseconds = NormalizeOffset(offsetMilliseconds),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            },
            cancellationToken);
    }

    public async Task<TrackLyricOffsetSaveResult> DeleteAsync(
        TrackLyricOffsetRecordKey recordKey,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = ToInternalKey(recordKey);
        lock (_syncRoot)
        {
            _offsets.Remove(key);
        }

        return await PersistAsync(null, new[] { key }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TrackLyricOffsetSaveResult> ClearAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_syncRoot)
        {
            _offsets.Clear();
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var context = new UserDataDbContext();
            await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            await context.TrackLyricOffsets.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            return new TrackLyricOffsetSaveResult(true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"清除单曲歌词偏移失败: {ex.Message}");
            return new TrackLyricOffsetSaveResult(false, ex.Message);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public static bool TryCreateIdentity(
        TrackInfo track,
        string? lyricSource,
        out TrackLyricOffsetIdentity identity)
    {
        var title = NormalizeIdentityPart(track.Title);
        var artist = NormalizeIdentityPart(track.Artist);
        var normalizedLyricSource = NormalizeIdentityPart(lyricSource);
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(normalizedLyricSource))
        {
            identity = default;
            return false;
        }

        identity = new TrackLyricOffsetIdentity(
            title,
            artist,
            normalizedLyricSource,
            CreateDurationBucket(track.Duration));
        return true;
    }

    public static int CreateDurationBucket(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return 0;
        }

        return Math.Max(
            DurationBucketSizeSeconds,
            (int)Math.Round(
                duration.TotalSeconds / DurationBucketSizeSeconds,
                MidpointRounding.AwayFromZero) * DurationBucketSizeSeconds);
    }

    public static string NormalizeIdentityPart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var simplified = ChineseScriptConverter.ToSimplified(
            value.Trim().Normalize(NormalizationForm.FormKC)).ToLowerInvariant();
        var builder = new StringBuilder(simplified.Length);
        foreach (var ch in simplified)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writeGate.Dispose();
    }

    private async Task<TrackLyricOffsetSaveResult> SetSnapshotAsync(
        TrackLyricOffsetSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        List<TrackLyricOffsetKey> keysToDelete;
        TrackLyricOffsetSnapshot? snapshotToSave = snapshot.OffsetMilliseconds == 0 ? null : snapshot;

        lock (_syncRoot)
        {
            keysToDelete = ResolveKeysToReplace(snapshot.Key);
            foreach (var key in keysToDelete)
            {
                _offsets.Remove(key);
            }

            if (snapshotToSave is not null)
            {
                _offsets[snapshotToSave.Key] = snapshotToSave;
            }
        }

        return await PersistAsync(snapshotToSave, keysToDelete, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TrackLyricOffsetSaveResult> PersistAsync(
        TrackLyricOffsetSnapshot? snapshot,
        IReadOnlyCollection<TrackLyricOffsetKey> keysToDelete,
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var context = new UserDataDbContext();
            await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

            var persistentDeletes = snapshot is null
                ? keysToDelete
                : keysToDelete.Where(key => key != snapshot.Key).ToArray();
            foreach (var key in persistentDeletes.Distinct())
            {
                var existing = await FindEntityAsync(context, key, cancellationToken).ConfigureAwait(false);
                if (existing is not null)
                {
                    context.TrackLyricOffsets.Remove(existing);
                }
            }

            if (snapshot is not null)
            {
                var existing = await FindEntityAsync(context, snapshot.Key, cancellationToken).ConfigureAwait(false);
                if (existing is null)
                {
                    existing = new TrackLyricOffset();
                    context.TrackLyricOffsets.Add(existing);
                }

                ApplySnapshot(existing, snapshot);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new TrackLyricOffsetSaveResult(true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"保存单曲歌词偏移失败: {ex.Message}");
            return new TrackLyricOffsetSaveResult(false, ex.Message);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void Load()
    {
        try
        {
            using var context = new UserDataDbContext();
            context.Database.EnsureCreated();
            context.Database.ExecuteSqlRaw(
                "CREATE INDEX IF NOT EXISTS IX_TrackLyricOffsets_UpdatedAtUtc ON TrackLyricOffsets (UpdatedAtUtc)");
            context.Database.ExecuteSqlRaw(
                "CREATE INDEX IF NOT EXISTS IX_TrackLyricOffsets_LyricSource_UpdatedAtUtc ON TrackLyricOffsets (LyricSource, UpdatedAtUtc)");
            var records = context.TrackLyricOffsets.AsNoTracking().ToList();
            lock (_syncRoot)
            {
                _offsets.Clear();
                foreach (var record in records)
                {
                    var key = new TrackLyricOffsetKey(
                        record.NormalizedTitle,
                        record.NormalizedArtist,
                        record.NormalizedLyricSource,
                        record.DurationBucketSeconds);
                    _offsets[key] = new TrackLyricOffsetSnapshot(
                        key,
                        record.DisplayTitle,
                        record.DisplayArtist,
                        record.Album,
                        record.SourceApp,
                        record.LyricSource,
                        record.SongId,
                        NormalizeOffset(record.OffsetMilliseconds),
                        record.UpdatedAtUtc);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"加载单曲歌词偏移失败: {ex.Message}");
        }
    }

    private List<TrackLyricOffsetKey> ResolveKeysToReplace(TrackLyricOffsetKey targetKey)
    {
        var keys = new List<TrackLyricOffsetKey>();
        if (_offsets.ContainsKey(targetKey))
        {
            keys.Add(targetKey);
        }

        if (targetKey.DurationBucketSeconds <= 0)
        {
            return keys;
        }

        var nearby = _offsets
            .Where(pair => IsSameTrackAndSource(pair.Key, targetKey) &&
                pair.Key.DurationBucketSeconds > 0 &&
                Math.Abs(pair.Key.DurationBucketSeconds - targetKey.DurationBucketSeconds) <= DurationToleranceSeconds)
            .OrderBy(pair => Math.Abs(pair.Key.DurationBucketSeconds - targetKey.DurationBucketSeconds))
            .ThenByDescending(pair => pair.Value.UpdatedAtUtc)
            .Select(pair => pair.Key)
            .FirstOrDefault();
        if (nearby != default && !keys.Contains(nearby))
        {
            keys.Add(nearby);
        }

        var unknownDurationKey = targetKey with { DurationBucketSeconds = 0 };
        if (_offsets.ContainsKey(unknownDurationKey) && !keys.Contains(unknownDurationKey))
        {
            keys.Add(unknownDurationKey);
        }

        return keys;
    }

    private static bool IsSameTrackAndSource(TrackLyricOffsetKey left, TrackLyricOffsetKey right)
    {
        return left.NormalizedTitle == right.NormalizedTitle &&
            left.NormalizedArtist == right.NormalizedArtist &&
            left.NormalizedLyricSource == right.NormalizedLyricSource;
    }

    private static async Task<TrackLyricOffset?> FindEntityAsync(
        UserDataDbContext context,
        TrackLyricOffsetKey key,
        CancellationToken cancellationToken)
    {
        return await context.TrackLyricOffsets.FirstOrDefaultAsync(
            offset =>
                offset.NormalizedTitle == key.NormalizedTitle &&
                offset.NormalizedArtist == key.NormalizedArtist &&
                offset.NormalizedLyricSource == key.NormalizedLyricSource &&
                offset.DurationBucketSeconds == key.DurationBucketSeconds,
            cancellationToken).ConfigureAwait(false);
    }

    private static void ApplySnapshot(TrackLyricOffset target, TrackLyricOffsetSnapshot snapshot)
    {
        target.NormalizedTitle = snapshot.Key.NormalizedTitle;
        target.NormalizedArtist = snapshot.Key.NormalizedArtist;
        target.NormalizedLyricSource = snapshot.Key.NormalizedLyricSource;
        target.DurationBucketSeconds = snapshot.Key.DurationBucketSeconds;
        target.DisplayTitle = snapshot.DisplayTitle;
        target.DisplayArtist = snapshot.DisplayArtist;
        target.Album = snapshot.Album;
        target.SourceApp = snapshot.SourceApp;
        target.LyricSource = snapshot.LyricSource;
        target.SongId = snapshot.SongId;
        target.OffsetMilliseconds = snapshot.OffsetMilliseconds;
        target.UpdatedAtUtc = snapshot.UpdatedAtUtc;
    }

    private static TrackLyricOffsetResolution CreateResolution(
        TrackLyricOffsetSnapshot snapshot,
        TrackLyricOffsetMatchKind matchKind)
    {
        return new TrackLyricOffsetResolution(
            snapshot.OffsetMilliseconds,
            matchKind,
            snapshot.Key.DurationBucketSeconds,
            snapshot.UpdatedAtUtc);
    }

    private static int NormalizeOffset(int value)
    {
        return Math.Clamp(value, MinimumOffsetMilliseconds, MaximumOffsetMilliseconds);
    }

    private static IEnumerable<string> SplitSearchTerms(string? search)
    {
        return string.IsNullOrWhiteSpace(search)
            ? Array.Empty<string>()
            : search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static TrackLyricOffsetKey ToInternalKey(TrackLyricOffsetRecordKey key)
    {
        return new TrackLyricOffsetKey(
            key.NormalizedTitle,
            key.NormalizedArtist,
            key.NormalizedLyricSource,
            key.DurationBucketSeconds);
    }

    private sealed record TrackLyricOffsetSnapshot(
        TrackLyricOffsetKey Key,
        string DisplayTitle,
        string DisplayArtist,
        string Album,
        string SourceApp,
        string LyricSource,
        string SongId,
        int OffsetMilliseconds,
        DateTimeOffset UpdatedAtUtc);
}

public readonly record struct TrackLyricOffsetIdentity(
    string NormalizedTitle,
    string NormalizedArtist,
    string NormalizedLyricSource,
    int DurationBucketSeconds)
{
    internal TrackLyricOffsetKey Key => new(
        NormalizedTitle,
        NormalizedArtist,
        NormalizedLyricSource,
        DurationBucketSeconds);
}

public readonly record struct TrackLyricOffsetRecordKey(
    string NormalizedTitle,
    string NormalizedArtist,
    string NormalizedLyricSource,
    int DurationBucketSeconds);

internal readonly record struct TrackLyricOffsetKey(
    string NormalizedTitle,
    string NormalizedArtist,
    string NormalizedLyricSource,
    int DurationBucketSeconds);

public sealed record TrackLyricOffsetEntry(
    TrackLyricOffsetRecordKey Key,
    string DisplayTitle,
    string DisplayArtist,
    string Album,
    string SourceApp,
    string LyricSource,
    string SongId,
    int OffsetMilliseconds,
    DateTimeOffset UpdatedAtUtc);

public sealed record TrackLyricOffsetPage(
    IReadOnlyList<TrackLyricOffsetEntry> Entries,
    IReadOnlyList<string> LyricSources,
    int Page,
    int PageSize,
    int PageCount,
    int TotalCount,
    int UnfilteredCount);

public enum TrackLyricOffsetSort
{
    Updated,
    Title,
    OffsetMagnitude
}

public readonly record struct TrackLyricOffsetResolution(
    int OffsetMilliseconds,
    TrackLyricOffsetMatchKind MatchKind,
    int MatchedDurationBucketSeconds,
    DateTimeOffset? UpdatedAtUtc)
{
    public static TrackLyricOffsetResolution None => new(0, TrackLyricOffsetMatchKind.None, 0, null);
    public bool HasStoredOffset => MatchKind != TrackLyricOffsetMatchKind.None;
}

public readonly record struct TrackLyricOffsetSaveResult(bool IsSaved, string? ErrorMessage);

public enum TrackLyricOffsetMatchKind
{
    None,
    Exact,
    NearbyDuration,
    UnknownDurationFallback
}
