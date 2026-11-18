using System.IO;
using Logvu.Core.Config;
using Logvu.Core.Store;
using Logvu.Core.Timeline;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;

namespace Logvu.Tui;

/// <summary>
/// Connects the store to the timeline and the event detail. It owns the store subscription and the <see cref="TimelineModel"/>, applies
/// the store's updates on the UI thread, opens and closes the detail, and keeps the status line current.
/// </summary>
/// <remarks>
/// <para>
/// <b>Threads.</b> The store raises its updates on its worker thread, the model's loads complete on the thread pool, and a detail read
/// completes on the thread pool. All of them post their work to the UI thread. Everything else runs on the UI thread.
/// </para>
/// <para>
/// <b>Start.</b> The timeline starts with the first update that is the initial run or a resync (a failed first run is followed by a
/// resync). Until then the view shows "Loading…".
/// </para>
/// <para>
/// <b>Detail.</b> Enter opens the selected event: the model pauses, the detail replaces the timeline in the window, and its read starts.
/// Esc closes it: the read is cancelled, the timeline comes back as it was, and the model is not touched. Store updates go on reaching the
/// model while the detail is open. A read that completes after the detail was closed is ignored.
/// </para>
/// </remarks>
internal sealed class TimelineController : IDisposable
{
    /// <summary>The status line's key hints.</summary>
    internal const string Hints = "Space pause/follow  End newest  ^Q quit";

    /// <summary>The status line's key hints while the detail is open.</summary>
    internal const string DetailHints = "Enter toggle  z folds  r raw  Esc back";

    /// <summary>How long a new retention gap stays on the status line. The gap stays in the store's status.</summary>
    internal static readonly TimeSpan GapShownFor = TimeSpan.FromSeconds(10);

    private readonly IApplication _app;
    private readonly LogStore _store;
    private readonly IDetailSource _details;
    private readonly LogvuConfig _config;
    private readonly Action<Action> _post;
    private readonly TimelineModel _model;
    private readonly TimelineView _view;
    private readonly StatusLine _statusLine;
    private StoreStatus? _storeStatus;
    private RetentionGap? _latestGap;
    private bool _gapShown;
    private int _gapEpoch;
    private bool _started;
    private DetailSession? _detail;
    private string _statusLeft = "Loading…";
    private volatile bool _disposed;

    /// <summary>Creates the controller and the window. Call <see cref="Start"/> to subscribe to the store and start it.</summary>
    /// <param name="app">The initialized application.</param>
    /// <param name="store">The store to show. It must not be started yet.</param>
    /// <param name="config">The configuration of the set: the time zone, and the highlights and styles of the detail.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public TimelineController(IApplication app, LogStore store, LogvuConfig config)
        : this(app, store, new LogStoreDetailSource(store), config, post: null)
    {
    }

    /// <summary>Creates the controller over a detail source, and an optional scheduler for the UI-thread continuations.</summary>
    /// <param name="app">The initialized application.</param>
    /// <param name="store">The store to show. It must not be started yet.</param>
    /// <param name="details">Reads the details of the events. The store's by default; a test may hold or fail the reads.</param>
    /// <param name="config">The configuration of the set.</param>
    /// <param name="post">
    /// Runs a continuation on the UI thread. The application's <see cref="IApplication.Invoke(Action)"/> by default.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="post"/> is <c>null</c>.</exception>
    internal TimelineController(IApplication app, LogStore store, IDetailSource details, LogvuConfig config, Action<Action>? post)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(config);

        _app = app;
        _store = store;
        _details = details;
        _config = config;
        _post = post ?? (action => app.Invoke(action));
        _model = new TimelineModel(new LogStoreTimelineSource(store), Post, store.Compare);
        _view = new TimelineView(_model, config.TimeZone) { Placeholder = "Loading…" };
        _statusLine = new StatusLine();
        Window = new MainWindow(() => _app.RequestStop(Window), _view, _statusLine);
    }

    /// <summary>The main window. Run it with the application.</summary>
    public MainWindow Window { get; }

    /// <summary>The timeline model. Its owner is the controller; the test seam reads it.</summary>
    internal TimelineModel Model => _model;

    /// <summary>The timeline view, the window's first content.</summary>
    internal TimelineView Timeline => _view;

    /// <summary>The detail view while an event is open, or <c>null</c> when the timeline is shown.</summary>
    internal DetailView? Detail => _detail?.View;

    /// <summary>The left part of the status line as last shown.</summary>
    internal string StatusLeft => _statusLeft;

    /// <summary>Subscribes to the store and to the model, focuses the timeline and starts the store.</summary>
    /// <exception cref="InvalidOperationException">The store is already started.</exception>
    public void Start()
    {
        _store.Updated += OnStoreUpdated;
        _model.Changed += OnModelChanged;
        _view.ViewportRowsChanged += OnViewportRowsChanged;
        _view.OpenRequested += OpenDetail;
        _view.SetFocus();
        _store.Start();
        RefreshStatus();
    }

    /// <summary>
    /// Unsubscribes from the store and the model, and cancels an open detail read. Updates and reads that arrive after this one are
    /// ignored.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.Updated -= OnStoreUpdated;
        _model.Changed -= OnModelChanged;
        _view.ViewportRowsChanged -= OnViewportRowsChanged;
        _view.OpenRequested -= OpenDetail;
        if (_detail is { } session)
        {
            _detail = null;
            session.Cancellation.Cancel();
        }
    }

    /// <summary>Runs on the store's worker thread. Hands the update to the UI thread.</summary>
    private void OnStoreUpdated(StoreUpdate update) => Post(() => Apply(update));

    /// <summary>Applies a store update on the UI thread.</summary>
    private void Apply(StoreUpdate update)
    {
        _storeStatus = update.Status;
        ShowNewGap(update.Status);
        if (!_started)
        {
            if (update.Batch.Initial || update.Resync)
            {
                _started = true;
                _view.Placeholder = "Waiting for events…";
                _model.Start(TimelineView.RowsFor(_view.Viewport.Height));
            }

            RefreshStatus();
            return;
        }

        if (update.Resync || update.Batch.RingReset)
        {
            _model.Reset();
        }
        else
        {
            _model.Arrive(update.Batch.Added, update.Batch.Updated);
        }

        RefreshStatus();
    }

    /// <summary>
    /// Shows a gap that appeared since the last update for <see cref="GapShownFor"/>. A timeout on the UI thread hides it again, and a
    /// newer gap restarts the time: the timeout of an older gap does nothing.
    /// </summary>
    private void ShowNewGap(StoreStatus status)
    {
        RetentionGap? latest = status.Gaps.Count > 0 ? status.Gaps[^1] : null;
        if (latest is null || latest == _latestGap)
        {
            return;
        }

        _latestGap = latest;
        _gapShown = true;
        int epoch = ++_gapEpoch;
        _app.AddTimeout(GapShownFor, () =>
        {
            if (!_disposed && epoch == _gapEpoch)
            {
                _gapShown = false;
                RefreshStatus();
            }

            return false;
        });
    }

    private void OnModelChanged()
    {
        _view.SetNeedsDraw();
        RefreshStatus();
    }

    private void OnViewportRowsChanged(int rows)
    {
        if (_started && rows != _model.Height)
        {
            _model.Resize(rows);
        }
    }

    /// <summary>
    /// Opens the detail of the event at <paramref name="key"/>: pauses the timeline, shows the detail view with "Loading…", and starts the
    /// read. Does nothing while a detail is open.
    /// </summary>
    private void OpenDetail(RowKey key)
    {
        if (_disposed || _detail is not null)
        {
            return;
        }

        _model.Pause();
        var view = new DetailView { Placeholder = "Loading…" };
        var session = new DetailSession(key, view, new CancellationTokenSource());
        view.CloseRequested += CloseDetail;
        view.DocumentChanged += RefreshStatus;
        _detail = session;
        Window.SetContent(view);
        RefreshStatus();
        _ = LoadDetailAsync(session);
    }

    /// <summary>Reads the detail of an open event, and posts its arrival to the UI thread. A read that fails shows as unavailable.</summary>
    private async Task LoadDetailAsync(DetailSession session)
    {
        EventDetail detail;
        try
        {
            detail = await _details.ReadDetailAsync(session.Key, session.Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            detail = new EventDetail.Unavailable(session.Key, ex.Message);
        }

        Post(() => ShowDetail(session, detail));
    }

    /// <summary>Shows a read detail on the UI thread, unless the detail was closed or replaced since the read started.</summary>
    private void ShowDetail(DetailSession session, EventDetail detail)
    {
        if (!ReferenceEquals(_detail, session))
        {
            return;
        }

        session.Detail = detail;
        session.View.Load(detail, _config);
        RefreshStatus();
    }

    /// <summary>
    /// Closes the open detail: cancels its read, shows the timeline again and disposes the detail view. The timeline keeps its model,
    /// so its rows, selection and viewport are as they were.
    /// </summary>
    private void CloseDetail()
    {
        if (_detail is not { } session)
        {
            return;
        }

        _detail = null;
        session.Cancellation.Cancel();
        Window.SetContent(_view);

        // The detail is still handling the key that closes it, so it is disposed once that has returned.
        Post(() => session.View.Dispose());
        RefreshStatus();
    }

    /// <summary>Runs a continuation on the UI thread. Work posted after the application stopped is dropped.</summary>
    private void Post(Action action)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _post(action);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The application has stopped or been disposed; there is nothing left to update.
        }
    }

    private void RefreshStatus()
    {
        _statusLeft = _detail is { } session ? DetailLeft(session) : LeftText();
        _statusLine.Show(_statusLeft, message: false, right: _detail is null ? Hints : DetailHints);
    }

    /// <summary>
    /// The left part of the status line of the detail: <c>DETAIL</c>, then the file and offset of a loaded event, then <c>raw</c> in the raw
    /// view and <c>truncated</c> for a cut event. An unavailable event shows only <c>DETAIL</c>.
    /// </summary>
    private static string DetailLeft(DetailSession session)
    {
        var parts = new List<string> { "DETAIL" };
        if (session.Detail is EventDetail.Loaded loaded)
        {
            parts.Add($"{Path.GetFileName(loaded.Path)}:{loaded.Offset}");
            if (session.View.Document is { } document && document.Options.Raw)
            {
                parts.Add("raw");
            }

            if (loaded.Truncated)
            {
                parts.Add("truncated");
            }
        }

        return string.Join(" · ", parts);
    }

    /// <summary>The left part of the status line: the mode, the page day, the live file and the latest problem.</summary>
    private string LeftText()
    {
        if (!_started)
        {
            return "Loading…";
        }

        var parts = new List<string> { ModeText() };
        if (TimelineLayout.PageDate(_model.VisibleRows, _config.TimeZone) is { } day)
        {
            parts.Add(day);
        }

        if (_storeStatus?.LiveMemberName is { } live)
        {
            parts.Add(live);
        }

        if (Problem(_storeStatus, _gapShown) is { } problem)
        {
            parts.Add(problem);
        }

        return string.Join(" · ", parts);
    }

    private string ModeText()
    {
        if (_model.Mode == TimelineMode.Follow)
        {
            return "FOLLOW";
        }

        return _model.NewCount > 0 ? $"PAUSED · {_model.NewCount} new" : "PAUSED";
    }

    /// <summary>
    /// The most important problem of the store, short: the directory error first, since it is current; then the latest read error,
    /// the latest retention gap while it is shown, and the latest failure. The store lists read errors only while they are current.
    /// </summary>
    private static string? Problem(StoreStatus? status, bool showGap)
    {
        if (status is null)
        {
            return null;
        }

        if (status.DirectoryError is { } directory)
        {
            return "directory: " + directory;
        }

        if (status.RecentErrors.Count > 0)
        {
            MemberReadError error = status.RecentErrors[^1];
            return $"{Path.GetFileName(error.Path)}: {error.Message}";
        }

        if (showGap && status.Gaps.Count > 0)
        {
            RetentionGap gap = status.Gaps[^1];
            return $"gap: {gap.Name} lost {gap.LostBytes} bytes";
        }

        return status.RecentFailures.Count > 0 ? status.RecentFailures[^1] : null;
    }

    /// <summary>The detail open in the window: its event, its view, the read's cancellation, and the detail once it has arrived.</summary>
    private sealed class DetailSession
    {
        public DetailSession(RowKey key, DetailView view, CancellationTokenSource cancellation)
        {
            Key = key;
            View = view;
            Cancellation = cancellation;
        }

        /// <summary>The key of the event.</summary>
        public RowKey Key { get; }

        /// <summary>The detail view showing the event.</summary>
        public DetailView View { get; }

        /// <summary>Cancels the read. Cancelled when the detail is closed.</summary>
        public CancellationTokenSource Cancellation { get; }

        /// <summary>The detail once it has arrived, or <c>null</c> while it is loading.</summary>
        public EventDetail? Detail { get; set; }
    }
}
