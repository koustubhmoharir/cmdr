using System.IO;
using Logvu.Core.Config;
using Logvu.Core.Detail;
using Logvu.Core.Filter;
using Logvu.Core.Parsing;
using Logvu.Core.Store;
using Logvu.Core.Timeline;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
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
/// <b>Detail.</b> Enter, Right or <c>l</c> opens the selected event: the model pauses, the detail replaces the timeline in the window, and
/// its read starts. Esc, Left or <c>h</c> closes it: the read is cancelled, the timeline comes back as it was, and the model is not touched.
/// Store updates go on reaching the model while the detail is open. A read that completes after the detail was closed is ignored.
/// </para>
/// <para>
/// <b>Switch.</b> Ctrl+O shows the set picker over the window. A chosen config is kept in <see cref="SwitchTo"/> and the window stops, so
/// the host can run that set next. Ctrl+Q in the picker stops the window with no switch, as Ctrl+Q does in the window.
/// </para>
/// </remarks>
internal sealed class TimelineController : IDisposable, ISearchHost
{
    /// <summary>How the set picker ended: Back to the current set, Chosen to switch to its config, or Quit the application.</summary>
    internal enum PickOutcome
    {
        /// <summary>Esc: the current set stays as it was.</summary>
        Back,

        /// <summary>A config was opened: the window stops with it as <see cref="SwitchTo"/>.</summary>
        Chosen,

        /// <summary>Ctrl+Q: the window stops with no switch.</summary>
        Quit,
    }

    /// <summary>The text of the key hints with no selection in the timeline. The data is <see cref="KeyHints.Timeline"/>.</summary>
    internal static string Hints => KeyHints.Timeline.Text;

    /// <summary>The text of the key hints while the timeline's selected row has a selection, which <c>c</c> copies.</summary>
    internal static string SelectionHints => KeyHints.TimelineSelection.Text;

    /// <summary>The text of the key hints while the detail is open and has no selection.</summary>
    internal static string DetailHints => KeyHints.Detail.Text;

    /// <summary>The text of the key hints while the detail is open and has a selection, which <c>c</c> copies.</summary>
    internal static string DetailSelectionHints => KeyHints.DetailSelection.Text;

    /// <summary>How long a new retention gap stays on the status line. The gap stays in the store's status.</summary>
    internal static readonly TimeSpan GapShownFor = TimeSpan.FromSeconds(10);

    /// <summary>How long a notice of a filter key stays on the status line.</summary>
    internal static readonly TimeSpan NoticeShownFor = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long a reload may hold the page on screen before the page is drawn faint. A reload that settles sooner shows the page as it
    /// was, with no change in its look; one that takes longer shows that it is still loading.
    /// </summary>
    internal static readonly TimeSpan HeldDimAfter = TimeSpan.FromMilliseconds(300);

    private readonly IApplication _app;
    private readonly LogStore _store;
    private readonly IDetailSource _details;
    private readonly LogvuConfig _config;
    private readonly Action<Action> _post;
    private readonly TimelineModel _model;
    private readonly TimelineView _view;
    private readonly StatusBar _statusLine;
    private readonly HintsLine _hintsLine;
    private readonly SearchController _search;
    private readonly Func<EventFilter?, FilterEdit?> _editFilter;
    private readonly Func<(PickOutcome Outcome, LogvuConfig? Config)>? _pickSet;
    private StoreStatus? _storeStatus;
    private RetentionGap? _latestGap;
    private bool _gapShown;
    private int _gapEpoch;
    private bool _started;
    private DetailSession? _detail;
    private EventFilter? _filter;
    private string? _notice;
    private int _noticeEpoch;
    private int _armedHold;
    private string _statusLeft = "Loading…";
    private string _statusHints = string.Empty;
    private volatile bool _disposed;

    /// <summary>Creates the controller and the window. Call <see cref="Start"/> to subscribe to the store and start it.</summary>
    /// <param name="app">The initialized application.</param>
    /// <param name="store">The store to show. It must not be started yet.</param>
    /// <param name="config">The configuration of the set: the time zone, and the highlights and styles of the detail.</param>
    /// <param name="pickSet">Shows the set picker for Ctrl+O, as the internal constructor describes. Ctrl+O does nothing when it is <c>null</c>.</param>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="pickSet"/> is <c>null</c>.</exception>
    public TimelineController(
        IApplication app,
        LogStore store,
        LogvuConfig config,
        Func<(PickOutcome Outcome, LogvuConfig? Config)>? pickSet = null)
        : this(app, store, new LogStoreDetailSource(store), config, post: null, pickSet: pickSet)
    {
    }

    /// <summary>Creates the controller over a detail source, a timeline source and an optional scheduler for the UI-thread continuations.</summary>
    /// <param name="app">The initialized application.</param>
    /// <param name="store">The store to show. It must not be started yet.</param>
    /// <param name="details">Reads the details of the events. The store's by default; a test may hold or fail the reads.</param>
    /// <param name="config">The configuration of the set.</param>
    /// <param name="post">
    /// Runs a continuation on the UI thread. The application's <see cref="IApplication.Invoke(Action)"/> by default.
    /// </param>
    /// <param name="source">The rows the timeline loads and searches. The store's by default; a test may supply its own.</param>
    /// <param name="compare">Orders the keys of <paramref name="source"/>. The store's order by default.</param>
    /// <param name="editFilter">
    /// Shows the filter editor over the active filter and returns its edit, or <c>null</c> when it was cancelled. The editor is shown with
    /// <see cref="FilterDialog.Ask(IApplication, EventFilter?, TimeZoneInfo)"/> by default.
    /// </param>
    /// <param name="pickSet">
    /// Shows the set picker and returns how it ended, with the chosen config. Ctrl+O does nothing when it is <c>null</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="post"/>, <paramref name="source"/>, <paramref name="compare"/>, <paramref name="editFilter"/> and <paramref name="pickSet"/> is <c>null</c>.</exception>
    internal TimelineController(
        IApplication app,
        LogStore store,
        IDetailSource details,
        LogvuConfig config,
        Action<Action>? post,
        ITimelineSource? source = null,
        Comparison<RowKey>? compare = null,
        Func<EventFilter?, FilterEdit?>? editFilter = null,
        Func<(PickOutcome Outcome, LogvuConfig? Config)>? pickSet = null)
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
        _editFilter = editFilter ?? (current => FilterDialog.Ask(app, current, config.TimeZone));
        _pickSet = pickSet;
        ITimelineSource timeline = source ?? new LogStoreTimelineSource(store);
        _filter = config.MinLevel is { } minLevel ? new EventFilter(new FilterSpec(MinLevel: minLevel)) : null;
        _model = new TimelineModel(timeline, Post, compare ?? store.Compare);
        _view = new TimelineView(_model, config.TimeZone, config.Columns) { Placeholder = "Loading…" };
        _statusLine = new StatusBar();
        _hintsLine = new HintsLine();
        _search = new SearchController(this, _view, _model, timeline, config.TimeZone, Post);
        WriteRaw = sequence => _app.Driver?.WriteRaw(sequence);
        Window = new MainWindow(() => _app.RequestStop(Window), _view, _statusLine, _hintsLine, _search.Bar, HandleShortcut, _search.CancelScan);
    }

    /// <summary>
    /// Writes a raw sequence to the terminal. The application's driver by default; a test replaces it to capture the OSC 52 sequence of a copy.
    /// </summary>
    internal Action<string> WriteRaw { get; set; }

    /// <summary>The main window. Run it with the application.</summary>
    public MainWindow Window { get; }

    /// <summary>The timeline model. Its owner is the controller; the test seam reads it.</summary>
    internal TimelineModel Model => _model;

    /// <summary>The timeline view, the window's first content.</summary>
    internal TimelineView Timeline => _view;

    /// <summary>The detail view while an event is open, or <c>null</c> when the timeline is shown.</summary>
    internal DetailView? Detail => _detail?.View;

    /// <summary>The status text on the status bar at the top of the window as last shown.</summary>
    internal string StatusLeft => _statusLeft;

    /// <summary>The key hints on the last row of the window as last shown, as text: the hints of an active search first, then the keys.</summary>
    internal string StatusHints => _statusHints;

    /// <summary>The search of the timeline and the detail. Its bar is the window's search bar.</summary>
    internal SearchController Search => _search;

    /// <summary>The active filter of the timeline, or <c>null</c> when every event is shown.</summary>
    internal EventFilter? Filter => _filter;

    /// <summary>
    /// The config chosen in the set picker, or <c>null</c>. Set when the window stopped for a switch; the host runs that set next.
    /// </summary>
    internal LogvuConfig? SwitchTo { get; private set; }

    /// <summary>Subscribes to the store and to the model, focuses the timeline and starts the store.</summary>
    /// <exception cref="InvalidOperationException">The store is already started.</exception>
    public void Start()
    {
        _store.Updated += OnStoreUpdated;
        _model.Changed += OnModelChanged;
        _view.ViewportRowsChanged += OnViewportRowsChanged;
        _view.OpenRequested += OpenDetail;
        _view.CopyRequested += CopySelection;
        _view.CopyEventRequested += CopyEvent;
        _view.SelectionChanged += RefreshStatus;
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
        _view.CopyRequested -= CopySelection;
        _view.CopyEventRequested -= CopyEvent;
        _view.SelectionChanged -= RefreshStatus;
        _search.StopScan();
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
                _model.Start(TimelineView.RowsFor(_view.Viewport.Height), _filter);
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
        UpdateHold();
        RefreshStatus();
    }

    /// <summary>
    /// Keeps the held page's dimming in step with the model. While the model holds a page that has not been timed yet, a timeout of
    /// <see cref="HeldDimAfter"/> is armed for that hold; when it fires, the page is dimmed if the same hold is still outstanding. A hold
    /// that settles first is never dimmed, and when the model holds nothing the page is not dimmed.
    /// </summary>
    private void UpdateHold()
    {
        if (!_model.IsHolding)
        {
            if (_view.DimHeld)
            {
                _view.DimHeld = false;
            }

            return;
        }

        int generation = _model.HoldGeneration;
        if (generation == _armedHold)
        {
            return;
        }

        _armedHold = generation;
        _app.AddTimeout(HeldDimAfter, () =>
        {
            if (!_disposed && _model.IsHolding && _model.HoldGeneration == generation)
            {
                _view.DimHeld = true;
            }

            return false;
        });
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
        _search.StopScan();
        var view = new DetailView { Placeholder = "Loading…" };
        var session = new DetailSession(key, view, new CancellationTokenSource());
        view.CloseRequested += CloseDetail;
        view.DocumentChanged += RefreshStatus;
        view.FilterRequested += FilterFromDetail;
        view.CopyRequested += CopyFromDetail;
        view.SelectionChanged += RefreshStatus;
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
        _search.OnDetailLoaded(session.View);
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
        _search.OnDetailClosed();

        // The detail is still handling the key that closes it, so it is disposed once that has returned.
        Post(() => session.View.Dispose());
        RefreshStatus();
    }

    /// <summary>
    /// The keys the window routes when no focused view took them. In the timeline <c>f</c> opens the filter editor, <c>F</c> clears the
    /// filter, <c>=</c> or <c>+</c> raises the minimum level one step and <c>-</c> or <c>_</c> lowers it; the search's keys come after them.
    /// The detail handles its own <c>f</c>, so only the search's keys reach here while it is open.
    /// </summary>
    private bool HandleShortcut(Key key)
    {
        if (key == Key.O.WithCtrl && _pickSet is not null)
        {
            OpenSetPicker();
            return true;
        }

        if (_detail is null)
        {
            if (IsPlain(key, 'f'))
            {
                OpenFilterEditor();
                return true;
            }

            if (IsPlain(key, 'F'))
            {
                ClearFilter();
                return true;
            }

            if (IsPlain(key, '=') || IsPlain(key, '+'))
            {
                ChangeMinLevel(direction: 1);
                return true;
            }

            if (IsPlain(key, '-') || IsPlain(key, '_'))
            {
                ChangeMinLevel(direction: -1);
                return true;
            }
        }

        return _search.HandleShortcut(key);
    }

    /// <summary>
    /// Shows the set picker over the window. A chosen config becomes <see cref="SwitchTo"/> and the window stops; Ctrl+Q stops it with no
    /// switch; Esc leaves the window as it was.
    /// </summary>
    private void OpenSetPicker()
    {
        (PickOutcome outcome, LogvuConfig? config) = _pickSet!();
        switch (outcome)
        {
            case PickOutcome.Chosen when config is not null:
                SwitchTo = config;
                _app.RequestStop(Window);
                break;
            case PickOutcome.Quit:
                _app.RequestStop(Window);
                break;
        }
    }

    /// <summary>
    /// Opens the filter editor over the active filter, in the timeline. Apply replaces the filter; Cancel and Esc keep it. The timeline keeps
    /// its selection and viewport when the filter changes.
    /// </summary>
    private void OpenFilterEditor()
    {
        if (_disposed || _detail is not null)
        {
            return;
        }

        FilterEdit? edit = _editFilter(_filter);
        _view.SetFocus();
        if (edit is { } applied)
        {
            ApplyFilter(applied.Filter);
        }
        else
        {
            RefreshStatus();
        }
    }

    /// <summary>
    /// Makes <paramref name="filter"/> the active filter, or none for <c>null</c>: the model reloads with it, and a running search is cancelled,
    /// since it was started with the filter it replaces.
    /// </summary>
    private void ApplyFilter(EventFilter? filter)
    {
        _filter = filter;
        _notice = null;
        _noticeEpoch++;
        if (_search.IsScanning)
        {
            _search.CancelScan();
        }

        _model.SetFilter(filter);
        RefreshStatus();
    }

    /// <summary>Clears the active filter, as <c>F</c> does in the timeline, and says so on the status line.</summary>
    private void ClearFilter()
    {
        if (_disposed)
        {
            return;
        }

        ApplyFilter(null);
        ShowNotice(FilterMessages.Cleared);
    }

    /// <summary>
    /// Steps the filter's minimum level one step up (<paramref name="direction"/> above zero) or down, as <see cref="FilterForm.StepMinLevel"/>
    /// gives it. The other predicates of the filter are kept. At an end the filter is not changed and not applied again. A minimum shows in
    /// the active filter's description; when no minimum is left, a notice says so.
    /// </summary>
    /// <param name="direction">Above zero to raise the minimum level, below zero to lower it.</param>
    private void ChangeMinLevel(int direction)
    {
        if (_disposed)
        {
            return;
        }

        LogLevel? current = _filter?.MinLevel;
        LogLevel? next = FilterForm.StepMinLevel(current, direction);
        if (next != current)
        {
            ApplyFilter(FilterForm.WithMinLevel(_filter, next));
        }

        if (next is null)
        {
            ShowNotice(FilterMessages.AnyLevel);
        }
    }

    /// <summary>
    /// Filters on the field under the cursor of the detail: the predicate is added to the active filter, the detail closes, and the timeline
    /// shows the filter. A line that is not a field only shows a notice, and the detail stays open.
    /// </summary>
    /// <param name="line">The line under the cursor.</param>
    private void FilterFromDetail(DetailLine line)
    {
        if (_disposed || _detail is null)
        {
            return;
        }

        if (FilterForm.FieldPredicateOf(line) is not { } predicate)
        {
            ShowNotice(FilterMessages.NotAField);
            return;
        }

        EventFilter filter = FilterForm.Extend(_filter, predicate);
        CloseDetail();
        ApplyFilter(filter);
    }

    /// <summary>Copies the text a detail asked to copy (its selection, or its cursor line), and reports the number of characters copied.</summary>
    /// <param name="text">The text to copy.</param>
    private void CopyFromDetail(string text)
    {
        if (_disposed || _detail is null)
        {
            return;
        }

        CopyToClipboard(text, $"Copied {TextSelection.CharacterCount(text)} characters");
    }

    /// <summary>Copies the timeline's selected text, and reports its number of characters. Nothing is copied while a detail is open.</summary>
    /// <param name="text">The selected text.</param>
    private void CopySelection(string text)
    {
        if (_disposed || _detail is not null)
        {
            return;
        }

        CopyToClipboard(text, $"Copied {TextSelection.CharacterCount(text)} characters");
    }

    /// <summary>
    /// Copies the whole event at <paramref name="key"/>, as its detail shows it with everything expanded. The read runs off the UI thread and
    /// its result is copied on the UI thread.
    /// </summary>
    /// <param name="key">The key of the selected event.</param>
    private void CopyEvent(RowKey key)
    {
        if (_disposed)
        {
            return;
        }

        _ = CopyEventAsync(key);
    }

    /// <summary>Reads the event for a copy, and posts its arrival to the UI thread. A read that fails shows as unavailable.</summary>
    private async Task CopyEventAsync(RowKey key)
    {
        EventDetail detail;
        try
        {
            detail = await _details.ReadDetailAsync(key, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            detail = new EventDetail.Unavailable(key, ex.Message);
        }

        Post(() => CopyDetail(detail));
    }

    /// <summary>
    /// Copies an event's text: every line of its document with all nodes and folds expanded, in the structured view, with the indentation the
    /// lines share removed. An unavailable event shows its reason as a notice instead.
    /// </summary>
    private void CopyDetail(EventDetail detail)
    {
        if (detail is EventDetail.Unavailable unavailable)
        {
            ShowNotice(unavailable.Reason);
            return;
        }

        DetailDocument document = DetailDocument.Build(detail, _config, DetailOptions.Default);
        string text = CopyText.Unindent(string.Join('\n', document.FullLines.Select(line => line.Text)));
        CopyToClipboard(text, $"Copied {TextSelection.CharacterCount(text)} characters");
    }

    /// <summary>
    /// Writes the text to the system clipboard with OSC 52 and shows <paramref name="successMessage"/> as a notice. Text over
    /// <see cref="Osc52.MaxBytes"/> is not copied, and the notice says so.
    /// </summary>
    /// <param name="text">The text to copy.</param>
    /// <param name="successMessage">The notice shown once the text is copied.</param>
    internal void CopyToClipboard(string text, string successMessage)
    {
        if (!Osc52.Fits(text))
        {
            ShowNotice("Selection too large to copy");
            return;
        }

        WriteRaw(Osc52.Build(text));
        ShowNotice(successMessage);
    }

    /// <summary>Shows a notice of a filter key in the left part of the status line for <see cref="NoticeShownFor"/>. A newer notice restarts the time.</summary>
    private void ShowNotice(string text)
    {
        _notice = text;
        int epoch = ++_noticeEpoch;
        _app.AddTimeout(NoticeShownFor, () =>
        {
            if (!_disposed && epoch == _noticeEpoch)
            {
                _notice = null;
                RefreshStatus();
            }

            return false;
        });
        RefreshStatus();
    }

    private static bool IsPlain(Key key, char ch) => !key.IsCtrl && !key.IsAlt && key.AsRune.Value == ch;

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

    /// <summary>
    /// Refreshes both lines: the status bar at the top gets the status text, and the hints on the last row get the keys. The hints name the
    /// keys of the matches first while a query is active.
    /// </summary>
    private void RefreshStatus()
    {
        bool inDetail = _detail is not null;
        _view.Placeholder = BodyText();
        _statusLeft = _detail is { } session ? DetailLeft(session, _notice, _search.Status) : LeftText();
        Hint keys = _detail is { } open
            ? (open.View.HasSelection ? KeyHints.DetailSelection : KeyHints.Detail)
            : (_view.HasSelection ? KeyHints.TimelineSelection : KeyHints.Timeline);
        Hint hints = _search.KeyHint(inDetail) is { } search ? KeyHints.Join(search, keys) : keys;
        _statusHints = hints.Text;
        _statusLine.Show(_statusLeft, message: false);
        _hintsLine.Show(hints);
    }

    DetailView? ISearchHost.Detail => _detail?.View;

    IRowFilter? ISearchHost.Filter => _filter;

    void ISearchHost.ShowSearchBar(bool open) => Window.ShowSearchBar(open);

    void ISearchHost.FocusContent()
    {
        View content = _detail is { } session ? session.View : _view;
        content.SetFocus();
    }

    void ISearchHost.RefreshStatus() => RefreshStatus();

    /// <summary>
    /// The left part of the status line of the detail: <c>DETAIL</c>, then the file name of a loaded event, then <c>raw</c> in the raw
    /// view and <c>truncated</c> for a cut event, then a notice of a filter key, and last the search's status. An unavailable event shows only
    /// <c>DETAIL</c>.
    /// </summary>
    private static string DetailLeft(DetailSession session, string? notice, string? search)
    {
        var parts = new List<string> { "DETAIL" };
        if (session.Detail is EventDetail.Loaded loaded)
        {
            parts.Add(Path.GetFileName(loaded.Path));
            if (session.View.Document is { } document && document.Options.Raw)
            {
                parts.Add("raw");
            }

            if (loaded.Truncated)
            {
                parts.Add("truncated");
            }
        }

        if (notice is not null)
        {
            parts.Add(notice);
        }

        if (search is not null)
        {
            parts.Add(search);
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
        if (_filter is { } filter)
        {
            parts.Add(FilterMessages.Active(filter.Describe()));
        }

        if (_model.IsScanning)
        {
            parts.Add(FilterMessages.Scanning(_model.ScannedBytes));
        }
        else if (_model.NoMatches)
        {
            parts.Add(FilterMessages.NoMatches);
        }

        if (_model.FilterTimedOut)
        {
            parts.Add(FilterMessages.TimedOut);
        }

        if (_notice is not null)
        {
            parts.Add(_notice);
        }

        if (_search.Status is { } search)
        {
            parts.Add(search);
        }

        if (TimelineLayout.PageDate(_model.ShownRows, _config.TimeZone) is { } day)
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

    /// <summary>
    /// The text of the timeline's body while it has no rows: why there is none. A filter that matched nothing says so, a scan in progress
    /// says that it is scanning, and only otherwise does the timeline wait for events.
    /// </summary>
    private string BodyText()
    {
        if (!_started)
        {
            return "Loading…";
        }

        if (_model.NoMatches)
        {
            return FilterMessages.NoMatches;
        }

        if (_model.IsScanning)
        {
            return "Scanning…";
        }

        return "Waiting for events…";
    }

    private string ModeText()
    {
        if (_model.Mode == TimelineMode.Follow)
        {
            return "FOLLOW";
        }

        if (_model.HasUnseenArrivals)
        {
            return "PAUSED · " + FilterMessages.NewEvents;
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
