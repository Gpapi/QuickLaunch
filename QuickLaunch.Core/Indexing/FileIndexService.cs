using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using QuickLaunch.Core.Abstractions;

namespace QuickLaunch.Core.Indexing;

/// <summary>
/// Owns the file index: loads it, keeps it fresh, and hands out the current snapshot.
/// </summary>
/// <remarks>
/// Freshness is handled by rebuilding, not by patching. The index is a set of flat arrays
/// chosen for scan speed, and inserting or removing a single entry in the middle of those
/// is not cheap — so watchers only decide *when* a rebuild is worth doing. The cost of
/// that choice is real and worth stating: a file created seconds ago is not findable until
/// the next rebuild lands.
/// </remarks>
public sealed class FileIndexService(FileIndexOptions? options = null) : ISearchIndex, IDisposable
{
    /// <summary>How long the file system must be quiet before a rebuild is worth starting.</summary>
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Floor on how often a rebuild may run. Without it, ordinary work in a watched folder
    /// would keep the disk busy re-walking the tree.
    /// </summary>
    private static readonly TimeSpan MinimumRebuildInterval = TimeSpan.FromMinutes(5);

    private readonly FileIndexOptions _options = options ?? new FileIndexOptions();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Lock _gate = new();

    private Timer? _rebuildTimer;
    private DateTimeOffset _lastRebuild = DateTimeOffset.MinValue;
    private volatile FileIndex _index = FileIndex.Empty;
    private volatile bool _rebuilding;
    private bool _disposed;

    /// <summary>The index as it currently stands. Safe to read while a rebuild runs.</summary>
    public FileIndex Index => _index;

    /// <summary>Raised after the index is replaced.</summary>
    public event EventHandler? Updated;

    /// <summary>
    /// Brings the index up as fast as possible: the cached snapshot first so queries work
    /// immediately, then a fresh walk. Blocking; run it off the UI thread.
    /// </summary>
    public void Start()
    {
        if (FileIndexSnapshot.TryLoad(_options.SnapshotPath) is { Count: > 0 } cached)
        {
            Publish(cached);
        }

        Rebuild(CancellationToken.None);
        StartWatching();
    }

    /// <summary>Walks the roots and replaces the index. Blocking.</summary>
    public void Rebuild(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_rebuilding)
            {
                return;
            }

            _rebuilding = true;
        }

        try
        {
            var rebuilt = FileIndexBuilder.Build(_options, cancellationToken);

            if (rebuilt.Count == 0)
            {
                // Never replace a working index with an empty one; a transient I/O failure
                // would otherwise leave the launcher unable to find any file.
                return;
            }

            Publish(rebuilt);
            FileIndexSnapshot.Save(rebuilt, _options.SnapshotPath);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            lock (_gate)
            {
                _rebuilding = false;
                _lastRebuild = DateTimeOffset.UtcNow;
            }
        }
    }

    private void Publish(FileIndex index)
    {
        _index = index;
        Updated?.Invoke(this, EventArgs.Empty);
    }

    private void StartWatching()
    {
        foreach (string root in _options.Roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,

                    // A busy tree overruns the default buffer easily, and an overrun loses
                    // events rather than delaying them.
                    InternalBufferSize = 64 * 1024,
                };

                watcher.Created += OnFileSystemChanged;
                watcher.Deleted += OnFileSystemChanged;
                watcher.Renamed += OnFileSystemChanged;

                // Losing events is exactly the case a rebuild fixes.
                watcher.Error += (_, _) => ScheduleRebuild();

                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Without a watcher this root simply goes stale until the next rebuild.
            }
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e) => ScheduleRebuild();

    /// <summary>
    /// Restarts the quiet-period countdown. Bursts of activity therefore cost one rebuild
    /// after they settle, rather than one per change.
    /// </summary>
    private void ScheduleRebuild()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var earliest = _lastRebuild + MinimumRebuildInterval;
            var delay = QuietPeriod;

            if (DateTimeOffset.UtcNow + delay < earliest)
            {
                delay = earliest - DateTimeOffset.UtcNow;
            }

            _rebuildTimer ??= new Timer(_ => Rebuild(CancellationToken.None));
            _rebuildTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
        _rebuildTimer?.Dispose();
        _rebuildTimer = null;
    }
}
