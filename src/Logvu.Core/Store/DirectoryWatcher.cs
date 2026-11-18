namespace Logvu.Core.Store;

/// <summary>
/// A non-recursive watch on the logs directory. Its events only call the given actions, which the store makes thread-safe; the watcher
/// never looks at the catalog or the ring.
/// </summary>
internal sealed class DirectoryWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;

    /// <summary>Starts watching the directory.</summary>
    /// <param name="directory">The directory. It must exist.</param>
    /// <param name="changed">Called for a created, deleted or renamed entry. It runs on a watcher thread.</param>
    /// <param name="failed">Called when the watcher fails or its buffer overflows; the events in between are lost. It runs on a watcher thread.</param>
    /// <exception cref="IOException">The watcher cannot be started on the directory.</exception>
    /// <exception cref="ArgumentException">The directory is not valid for a watcher.</exception>
    public DirectoryWatcher(string directory, Action changed, Action failed)
    {
        _watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = false,
            // Only names: growth of a file is read by the tail polls, and changes to backups by the periodic rescan.
            NotifyFilter = NotifyFilters.FileName,
        };
        _watcher.Created += (_, _) => changed();
        _watcher.Deleted += (_, _) => changed();
        _watcher.Renamed += (_, _) => changed();
        _watcher.Error += (_, _) => failed();
        try
        {
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            _watcher.Dispose();
            throw;
        }
    }

    /// <summary>Stops the watch. Events already in flight may still call their actions once.</summary>
    public void Dispose() => _watcher.Dispose();
}
