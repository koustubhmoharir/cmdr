using System.Diagnostics;
using Logvu.Core.Config;

namespace Logvu.Core.Store;

/// <summary>
/// The live view of a configured set. It keeps a catalog and a ring of the newest rows current with the files, and answers reads of history and
/// detail. This is the only code in Logvu.Core that starts threads.
/// </summary>
/// <remarks>
/// <para>
/// <b>Threads.</b> One worker thread owns the <see cref="Catalog"/>, the <see cref="RowRing"/> and the <see cref="Reconciler"/>. Nothing else
/// touches them. After each run the worker publishes an immutable <see cref="StoreState"/> (the timeline, the ring's rows and the status) with
/// one volatile write. Readers take the latest state with one volatile read, so they never lock and never see a catalog and a ring from
/// different runs. The reads run on the thread pool and work on that state alone; see <see cref="StoreReader"/>.
/// </para>
/// <para>
/// <b>When the worker runs.</b> It sleeps on a signal, and runs when one of these is due: a rescan was requested and the coalescing delay
/// after the first request has passed; the tail interval has elapsed; the rescan interval has elapsed; or a run left events pending, in which
/// case the next run is at once. A run rescans the directory when a request is due or the rescan interval has elapsed. A run always reads
/// the tail. Each run is one <see cref="Reconciler.Run"/>.
/// </para>
/// <para>
/// <b>Ring warm-up.</b> The first run is the initial one: its batch holds only the live provisional row, so the worker then prepends the
/// newest history to the ring, keeping only the rows older than the ring's oldest row. Afterwards the reconciler keeps the ring warm itself,
/// after a reset.
/// </para>
/// <para>
/// <b>Watcher.</b> A <see cref="DirectoryWatcher"/> on the logs directory requests a rescan when an entry is created, deleted or renamed, or
/// when the watcher fails. Growth of a file is not a request: the tail polls read it. Its callbacks only record the request and signal the
/// worker. While the directory is missing there is no watcher, and each rescan tries again to start one. A watcher
/// failure (an overflow or an error) requests a rescan and makes the next rescan replace the watcher.
/// </para>
/// <para>
/// <b>Failures.</b> A run that throws is recorded in <see cref="StoreStatus.RecentFailures"/> and the worker goes on at the next tick. The
/// failed run may have advanced the reconciler without its rows reaching subscribers, so a failure raises an update with
/// <see cref="StoreUpdate.Resync"/>, and the next run that succeeds raises one too. The reconciler is not rebuilt: its generations must
/// never be reused, because the rows a reader holds keep them. A handler of <see cref="Updated"/> that throws is recorded the same way, and
/// the other handlers still run.
/// </para>
/// </remarks>
public sealed class LogStore : IAsyncDisposable
{
    /// <summary>The most history rows the ring is warmed with after the initial run.</summary>
    private const int WarmRows = 2000;

    private const int RecentErrorCap = 20;
    private const int RecentGapCap = 100;
    private const int RecentFailureCap = 10;

    /// <summary>Whether the current thread is the worker, so that <see cref="DisposeAsync"/> does not wait for itself.</summary>
    [ThreadStatic]
    private static bool onWorkerThread;

    private readonly LogvuConfig _config;
    private readonly Catalog _catalog;
    private readonly RowRing _ring;
    private readonly Reconciler _reconciler;
    private readonly HistoryReader _history;
    private readonly StoreReader _reader;
    private readonly Thread _thread;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // The published state. Written by the worker only, with Volatile.Write; read by anyone with Volatile.Read.
    private StoreState _state;

    // Worker-only state.
    private readonly List<MemberReadError> _recentErrors = [];
    private readonly List<RetentionGap> _gaps = [];
    private readonly List<string> _failures = [];
    private DirectoryWatcher? _watcher;
    private string? _directoryError;
    private bool _ranOnce;
    private bool _warmed;
    private bool _morePending;
    private bool _failureAdded;
    private bool _resyncPending;
    private HashSet<(int Generation, string Message)> _lastRaisedErrors = [];
    private long _lastRescan;
    private long _lastTail;

    // Written by the watcher threads, and by the worker.
    private long _requestedAt;
    private int _watcherFailed;
    private int _rescanCount;
    private Action? _beforeRun;

    private volatile bool _stopping;
    private int _started;
    private int _disposed;

    /// <summary>
    /// Creates a store. It reads nothing until <see cref="Start"/>; until its initial run is published, reads see an empty timeline.
    /// </summary>
    /// <param name="config">The configuration of the set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is <c>null</c>.</exception>
    public LogStore(LogvuConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _catalog = new Catalog(config);
        _ring = new RowRing(config.RecentCapacity, _catalog.Compare);
        _reconciler = new Reconciler(config, _catalog, _ring);
        _history = new HistoryReader(config);
        _reader = new StoreReader(config, _history);
        _state = new StoreState(_catalog.Snapshot(), _ring.Snapshot(), _ring.CoversNewest, new StoreStatus(0, null, null, [], [], []));
        _thread = new Thread(Work) { IsBackground = true, Name = "logvu store" };
    }

    /// <summary>
    /// Raised on the worker thread after each run that changed something, after the initial run, and after a failed run (with
    /// <see cref="StoreUpdate.Resync"/>). Raised one at a time, in order. A subscriber reloads its window when the update is a resync or its
    /// batch has <see cref="ChangeBatch.RingReset"/>. A handler that throws is recorded in <see cref="StoreStatus.RecentFailures"/>; the store
    /// and the other handlers go on. A batch whose only content is read errors repeated from the previous update is not raised; the
    /// status still carries them.
    /// </summary>
    public event Action<StoreUpdate>? Updated;

    /// <summary>The latest published timeline. Thread-safe; the same instance until the next publication.</summary>
    public CatalogSnapshot Snapshot => Volatile.Read(ref _state).Catalog;

    /// <summary>
    /// Compares two events by the latest published timeline. Both keys' generations must be in it.
    /// </summary>
    /// <returns>A negative number, zero, or a positive number as <paramref name="a"/> is before, the same as, or after <paramref name="b"/>.</returns>
    /// <exception cref="ArgumentException">A key's generation is not in the timeline.</exception>
    public int Compare(RowKey a, RowKey b) => Volatile.Read(ref _state).Catalog.Compare(a, b);

    /// <summary>
    /// Starts the worker. Its first run is the initial run, which makes every member's existing content history.
    /// </summary>
    /// <exception cref="InvalidOperationException">The store is already started.</exception>
    /// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The store is already started.");
        }

        _thread.Start();
    }

    /// <summary>Whether the worker holds a directory watcher. Read after the worker has ended, it shows that the watcher was disposed.</summary>
    internal bool HasWatcher => _watcher is not null;

    /// <summary>The number of directory rescans run so far. Read by tests.</summary>
    internal int RescanCount => Volatile.Read(ref _rescanCount);

    /// <summary>The latest published status, including the status changes that were not raised as updates. Read by tests.</summary>
    internal StoreStatus CurrentStatus => Current.Status;

    /// <summary>
    /// Called on the worker before each run, inside the run's error handling: a test makes a run fail by throwing here.
    /// </summary>
    internal Action? BeforeRun
    {
        get => Volatile.Read(ref _beforeRun);
        set => Volatile.Write(ref _beforeRun, value);
    }

    /// <summary>The delay between two tail passes. Tests shorten it.</summary>
    internal TimeSpan TailInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The interval between two directory rescans. Tests shorten it.</summary>
    internal TimeSpan RescanInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>The delay after the first watcher event before a rescan, so that a burst of events is one rescan. Tests shorten it.</summary>
    internal TimeSpan CoalesceDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Reads up to <paramref name="count"/> of the newest rows, oldest first, that match the filter (or any row, when it is <c>null</c>).
    /// </summary>
    /// <param name="count">The most rows to return. It is at least 1.</param>
    /// <param name="filter">The predicate a row must match, or <c>null</c>.</param>
    /// <param name="progress">Receives the event bytes scanned in history. May be <c>null</c>.</param>
    /// <param name="ct">Cancels the read. A superseded read is cancelled by its caller.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is below 1.</exception>
    public Task<RowPage> ReadNewestAsync(int count, IRowFilter? filter, IProgress<long>? progress, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        return Task.Run(() => ReadPage(state => _reader.Newest(state, count, filter, progress, ct)), ct);
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> rows strictly older than <paramref name="anchor"/>, oldest first, that match the filter.
    /// </summary>
    /// <param name="anchor">The row the page is older than.</param>
    /// <param name="count">The most rows to return. It is at least 1.</param>
    /// <param name="filter">The predicate a row must match, or <c>null</c>.</param>
    /// <param name="progress">Receives the event bytes scanned in history. May be <c>null</c>.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is below 1, or the anchor's offset is negative.</exception>
    /// <exception cref="ArgumentException">The anchor's generation is not in the timeline.</exception>
    public Task<RowPage> ReadOlderAsync(RowKey anchor, int count, IRowFilter? filter, IProgress<long>? progress, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(anchor.Offset, nameof(anchor));
        return Task.Run(() => ReadPage(state => _reader.Older(state, anchor, count, filter, progress, ct)), ct);
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> rows strictly newer than <paramref name="anchor"/>, oldest first, that match the filter.
    /// </summary>
    /// <param name="anchor">The row the page is newer than.</param>
    /// <param name="count">The most rows to return. It is at least 1.</param>
    /// <param name="filter">The predicate a row must match, or <c>null</c>.</param>
    /// <param name="progress">Receives the event bytes scanned in history. May be <c>null</c>.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is below 1, or the anchor's offset is negative.</exception>
    /// <exception cref="ArgumentException">The anchor's generation is not in the timeline.</exception>
    public Task<RowPage> ReadNewerAsync(RowKey anchor, int count, IRowFilter? filter, IProgress<long>? progress, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(anchor.Offset, nameof(anchor));
        return Task.Run(() => ReadPage(state => _reader.Newer(state, anchor, count, filter, progress, ct)), ct);
    }

    /// <summary>
    /// Reads the full text and the parsed event of one row. An event whose file has expired is <see cref="EventDetail.Unavailable"/> with
    /// <c>Source file expired</c>. The file is opened with its identity verified, so content is never taken from another file.
    /// </summary>
    /// <param name="key">The row's key.</param>
    /// <param name="ct">Cancels the read.</param>
    public Task<EventDetail> ReadDetailAsync(RowKey key, CancellationToken ct) =>
        Task.Run(() => _reader.Detail(() => Current, key, ct), ct);

    /// <summary>
    /// Stops the worker and disposes its watcher. When it completes, no further <see cref="Updated"/> is raised. Called from inside an
    /// <see cref="Updated"/> handler, it only asks the worker to stop: the worker stops when that handler returns.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _stopping = true;
            Signal();
        }

        if (Volatile.Read(ref _started) == 0 || onWorkerThread)
        {
            return;
        }

        await _finished.Task.ConfigureAwait(false);
    }

    private StoreState Current => Volatile.Read(ref _state);

    /// <summary>
    /// Runs a page read against the latest state. A read that reports errors is repeated once against the state of a newer version: a rename
    /// that raced it is then in the state it reads.
    /// </summary>
    private RowPage ReadPage(Func<StoreState, RowPage> read)
    {
        StoreState first = Current;
        RowPage page = read(first);
        if (page.Errors.Count == 0)
        {
            return page;
        }

        StoreState latest = Current;
        return latest.Catalog.Version == first.Catalog.Version ? page : read(latest);
    }

    /// <summary>The worker's loop. It ends when the store is disposed.</summary>
    private void Work()
    {
        onWorkerThread = true;

        // Both are due at once, so that the first run starts straight away. Every attempt sets them to its own time.
        _lastRescan = Stopwatch.GetTimestamp() - Math.Max(Ticks(RescanInterval), Ticks(TailInterval));
        _lastTail = _lastRescan;
        try
        {
            while (!_stopping)
            {
                long now = Stopwatch.GetTimestamp();
                long requested = Volatile.Read(ref _requestedAt);
                bool rescanRequested = requested != 0 && now - requested >= Ticks(CoalesceDelay);
                bool rescanDue = now - _lastRescan >= Ticks(RescanInterval);
                bool tailDue = now - _lastTail >= Ticks(TailInterval);
                if (!_morePending && !rescanRequested && !rescanDue && !tailDue)
                {
                    WaitForNextDue(now, requested);
                    continue;
                }

                // The reconciler's first run must rescan, so a run that failed before it completed rescans again.
                bool rescan = rescanRequested || rescanDue || !_ranOnce;
                if (rescan)
                {
                    Interlocked.Exchange(ref _requestedAt, 0);
                }

                RunOnce(rescan, now);
            }
        }
        finally
        {
            _watcher?.Dispose();
            _watcher = null;
            _finished.TrySetResult();
        }
    }

    /// <summary>Sleeps until the next run is due, or until a signal arrives (a watcher event, or a dispose).</summary>
    private void WaitForNextDue(long now, long requested)
    {
        long next = Math.Min(_lastRescan + Ticks(RescanInterval), _lastTail + Ticks(TailInterval));
        if (requested != 0)
        {
            next = Math.Min(next, requested + Ticks(CoalesceDelay));
        }

        long remaining = Math.Max(0, next - now);
        _wake.Wait(TimeSpan.FromSeconds((double)remaining / Stopwatch.Frequency));
    }

    /// <summary>
    /// One run: the reconciler, the initial warm-up, the status, and the publication. An exception ends the run as a recorded failure, which
    /// raises a resync.
    /// </summary>
    private void RunOnce(bool rescan, long now)
    {
        _lastTail = now;
        if (rescan)
        {
            _lastRescan = now;
            Interlocked.Increment(ref _rescanCount);
        }

        _failureAdded = false;
        try
        {
            if (rescan)
            {
                EnsureWatcher();
            }

            BeforeRun?.Invoke();
            ChangeBatch batch = _reconciler.Run(rescan);
            _ranOnce = true;
            _morePending = batch.MorePending;
            if (!_warmed)
            {
                Warm();
                _warmed = true;
            }

            foreach (MemberReadError error in batch.Errors)
            {
                Remember(_recentErrors, error, RecentErrorCap);
            }

            foreach (RetentionGap gap in batch.Gaps)
            {
                Remember(_gaps, gap, RecentGapCap);
            }

            bool directoryChanged = false;
            if (rescan)
            {
                directoryChanged = batch.DirectoryError != _directoryError;
                _directoryError = batch.DirectoryError;
            }

            StoreState state = Publish();
            bool errorsRepeated = batch.Errors.Count > 0 && !HasContent(batch)
                && _lastRaisedErrors.SetEquals(ErrorSet(batch));
            bool raise = batch.Initial || _resyncPending || directoryChanged || _failureAdded
                || ((HasContent(batch) || batch.Errors.Count > 0) && !errorsRepeated);
            if (raise)
            {
                bool resync = _resyncPending;
                _resyncPending = false;
                Raise(new StoreUpdate(batch, state.Catalog, state.Status, resync));
                if (_failureAdded)
                {
                    PublishStatus();
                }
            }

            return;
        }
        catch (Exception ex)
        {
            _morePending = false;
            _resyncPending = true;
            RecordFailure($"run failed: {ex.GetType().Name}: {ex.Message}");
        }

        if (_failureAdded)
        {
            StoreState state = PublishStatus();
            Raise(new StoreUpdate(EmptyBatch, state.Catalog, state.Status, Resync: true));
        }
    }

    /// <summary>Whether a batch changed the rows, the timeline, the directory state or the gaps. Read errors do not count.</summary>
    private static bool HasContent(ChangeBatch batch) =>
        batch.Added.Count > 0
        || batch.Updated.Count > 0
        || batch.Expired.Count > 0
        || batch.Gaps.Count > 0
        || batch.CatalogChanged
        || batch.RingReset;

    private static HashSet<(int Generation, string Message)> ErrorSet(ChangeBatch batch) =>
        batch.Errors.Select(error => (error.Generation, error.Message)).ToHashSet();

    /// <summary>
    /// Prepends the newest history to the ring, after the initial run. Only the rows older than the ring's oldest row are added: the rows
    /// the initial tail pass published are already in the ring, and history reads them too.
    /// </summary>
    private void Warm()
    {
        CatalogSnapshot catalog = _catalog.Snapshot();
        RowPage history = _history.ReadNewest(catalog, Math.Min(_ring.Capacity, WarmRows), null, null, CancellationToken.None);
        foreach (MemberReadError error in history.Errors)
        {
            Remember(_recentErrors, error, RecentErrorCap);
        }

        RowKey? oldest = _ring.Oldest;
        List<EventRow> older = oldest is { } first
            ? history.Rows.Where(row => catalog.Compare(row.Key, first) < 0).ToList()
            : history.Rows.ToList();
        _ring.PrependOlder(older);
    }

    /// <summary>Publishes the worker's current catalog, ring and status.</summary>
    private StoreState Publish()
    {
        var state = new StoreState(_catalog.Snapshot(), _ring.Snapshot(), _ring.CoversNewest, BuildStatus());
        Volatile.Write(ref _state, state);
        return state;
    }

    /// <summary>Publishes the current status over the state's timeline and ring, which are unchanged.</summary>
    private StoreState PublishStatus()
    {
        StoreState state = _state with { Status = BuildStatus() };
        Volatile.Write(ref _state, state);
        return state;
    }

    private StoreStatus BuildStatus() => new(
        _catalog.All.Count(member => member.State != MemberState.Expired),
        _catalog.All.FirstOrDefault(member => member.IsReadable && member.IsLive)?.Name,
        _directoryError,
        _recentErrors.ToArray(),
        _gaps.ToArray(),
        _failures.ToArray());

    /// <summary>Calls each handler in turn. A handler that throws is recorded, and does not stop the others.</summary>
    private void Raise(StoreUpdate update)
    {
        _lastRaisedErrors = ErrorSet(update.Batch);
        Action<StoreUpdate>? handlers = Updated;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<StoreUpdate>)handler)(update);
            }
            catch (Exception ex)
            {
                RecordFailure($"update handler failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Records an unexpected failure, unless it repeats the latest one.</summary>
    private void RecordFailure(string message)
    {
        if (_failures.Count > 0 && _failures[^1] == message)
        {
            return;
        }

        Remember(_failures, message, RecentFailureCap);
        _failureAdded = true;
    }

    /// <summary>
    /// Makes sure the watcher exists while the directory does. A missing directory has no watcher, and each rescan tries to start one. A
    /// watcher that failed is replaced.
    /// </summary>
    private void EnsureWatcher()
    {
        bool failed = Interlocked.Exchange(ref _watcherFailed, 0) != 0;
        if (!Directory.Exists(_config.LogsDirectory))
        {
            _watcher?.Dispose();
            _watcher = null;
            return;
        }

        if (_watcher is not null && !failed)
        {
            return;
        }

        _watcher?.Dispose();
        _watcher = null;
        try
        {
            _watcher = new DirectoryWatcher(_config.LogsDirectory, OnDirectoryEvent, OnWatcherFailed);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException)
        {
            RecordFailure($"directory watcher could not start: {ex.Message}");
        }
    }

    /// <summary>A watcher event: requests a rescan. Runs on a watcher thread.</summary>
    private void OnDirectoryEvent()
    {
        if (_stopping)
        {
            return;
        }

        Interlocked.CompareExchange(ref _requestedAt, Stopwatch.GetTimestamp(), 0);
        Signal();
    }

    /// <summary>A watcher failure: the next rescan replaces the watcher, and this one requests that rescan.</summary>
    private void OnWatcherFailed()
    {
        Volatile.Write(ref _watcherFailed, 1);
        OnDirectoryEvent();
    }

    /// <summary>Wakes the worker. The signal is binary: one pending signal is enough, whatever the number of events.</summary>
    private void Signal()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // A signal is already pending.
        }
    }

    /// <summary>Adds an item to a bounded list of the most recent items. An item already there moves to the end.</summary>
    private static void Remember<T>(List<T> recent, T item, int capacity)
    {
        recent.Remove(item);
        recent.Add(item);
        if (recent.Count > capacity)
        {
            recent.RemoveAt(0);
        }
    }

    private static long Ticks(TimeSpan span) => (long)(span.TotalSeconds * Stopwatch.Frequency);

    private static ChangeBatch EmptyBatch { get; } = new(
        initial: false,
        added: [],
        updated: [],
        expired: [],
        gaps: [],
        catalogChanged: false,
        ringReset: false,
        errors: [],
        directoryError: null,
        morePending: false);
}
