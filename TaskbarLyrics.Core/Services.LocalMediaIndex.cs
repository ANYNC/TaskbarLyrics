using System.Collections.Concurrent;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.Core.Services;

public enum LocalMediaFileKind
{
    Audio,
    Lyric
}

public sealed record LocalMediaFileEntry(string Path, LocalMediaFileKind Kind);

public sealed record LocalMediaIndexSnapshot(int Version, IReadOnlyList<LocalMediaFileEntry> Files);

public interface ILocalMediaIndex : IDisposable
{
    LocalMediaIndexSnapshot GetSnapshot();
}

public static class LocalMediaIndexRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, SharedLocalMediaIndex> Indices = new(StringComparer.OrdinalIgnoreCase);

    public static ILocalMediaIndex Acquire(IEnumerable<string>? rootFolders)
    {
        var folders = NormalizeFolders(rootFolders);
        var key = string.Join("|", folders);
        lock (Gate)
        {
            if (!Indices.TryGetValue(key, out var index))
            {
                index = new SharedLocalMediaIndex(folders);
                Indices[key] = index;
            }

            index.AddReference();
            return new LocalMediaIndexLease(key, index);
        }
    }

    private static void Release(string key, SharedLocalMediaIndex index)
    {
        lock (Gate)
        {
            if (index.ReleaseReference() &&
                Indices.TryGetValue(key, out var current) &&
                ReferenceEquals(current, index))
            {
                Indices.Remove(key);
                index.Dispose();
            }
        }
    }

    private static string[] NormalizeFolders(IEnumerable<string>? rootFolders) => (rootFolders ?? [])
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => path.Trim().Trim('"'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private sealed class LocalMediaIndexLease(string key, SharedLocalMediaIndex index) : ILocalMediaIndex
    {
        private int _isDisposed;

        public LocalMediaIndexSnapshot GetSnapshot() =>
            Volatile.Read(ref _isDisposed) == 0 ? index.GetSnapshot() : new LocalMediaIndexSnapshot(0, []);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                Release(key, index);
            }
        }
    }

    private sealed class SharedLocalMediaIndex : IDisposable
    {
        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".m4a", ".aac", ".wav", ".ogg", ".opus", ".wma"
        };

        private static readonly HashSet<string> LyricExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".lrc", ".qrc", ".krc"
        };

        private readonly IReadOnlyList<string> _rootFolders;
        private readonly object _filesGate = new();
        private readonly List<LocalMediaFileEntry> _files = [];
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _indexTask;
        private int _version;
        private int _referenceCount;
        private int _isDisposed;

        public SharedLocalMediaIndex(IReadOnlyList<string> rootFolders)
        {
            _rootFolders = rootFolders;
            _indexTask = Task.Run(() => BuildIndexAsync(_cancellation.Token));
        }

        public void AddReference() => Interlocked.Increment(ref _referenceCount);

        public bool ReleaseReference() => Interlocked.Decrement(ref _referenceCount) == 0;

        public LocalMediaIndexSnapshot GetSnapshot()
        {
            if (Volatile.Read(ref _isDisposed) != 0)
            {
                return new LocalMediaIndexSnapshot(0, []);
            }

            lock (_filesGate)
            {
                return new LocalMediaIndexSnapshot(_version, _files.ToArray());
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            {
                return;
            }

            _cancellation.Cancel();
            _ = _indexTask.ContinueWith(
                _ => _cancellation.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private async Task BuildIndexAsync(CancellationToken cancellationToken)
        {
            try
            {
                var pending = new List<LocalMediaFileEntry>();
                var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var folder in _rootFolders)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Directory.Exists(folder))
                    {
                        continue;
                    }

                    foreach (var path in SafeEnumerateFiles(folder, cancellationToken))
                    {
                        var kind = GetKind(path);
                        if (kind is null)
                        {
                            continue;
                        }

                        if (!seenPaths.Add(path))
                        {
                            continue;
                        }

                        pending.Add(new LocalMediaFileEntry(path, kind.Value));
                        if (pending.Count >= 200)
                        {
                            Flush(pending);
                            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }

                Flush(pending);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Log.Warn($"Local media background index failed: {exception}");
            }
        }

        private void Flush(List<LocalMediaFileEntry> pending)
        {
            if (pending.Count == 0 || Volatile.Read(ref _isDisposed) != 0)
            {
                return;
            }

            lock (_filesGate)
            {
                _files.AddRange(pending);
                _version++;
            }

            pending.Clear();
        }

        private static LocalMediaFileKind? GetKind(string path)
        {
            var extension = Path.GetExtension(path);
            if (AudioExtensions.Contains(extension))
            {
                return LocalMediaFileKind.Audio;
            }

            return LyricExtensions.Contains(extension) ? LocalMediaFileKind.Lyric : null;
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
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
                {
                    continue;
                }

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return file;
                }

                IEnumerable<string> directories;
                try
                {
                    directories = Directory.EnumerateDirectories(folder);
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
                {
                    continue;
                }

                foreach (var directory in directories)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    pending.Push(directory);
                }
            }
        }
    }
}
