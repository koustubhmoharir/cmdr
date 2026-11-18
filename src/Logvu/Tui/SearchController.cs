using System.Diagnostics;
using Logvu.Core.Store;
using Logvu.Core.Timeline;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Logvu.Tui;

/// <summary>What the search needs from the window that owns it: the detail in view, the bar's row, the focus and the status line.</summary>
internal interface ISearchHost
{
    /// <summary>The detail view while an event is open, or <c>null</c> while the timeline is shown.</summary>
    DetailView? Detail { get; }

    /// <summary>Shows or hides the search bar's row above the status line.</summary>
    void ShowSearchBar(bool open);

    /// <summary>Gives the focus to the content in view: the detail while it is open, otherwise the timeline.</summary>
    void FocusContent();

    /// <summary>Refreshes the status line.</summary>
    void RefreshStatus();
}

/// <summary>
/// The search of the timeline and of the detail: the search bar, the query and its compiled pattern, the scan of the history from the
/// selected row toward older or newer events, and the moves between the matches of a detail.
/// </summary>
/// <remarks>
/// <para>
/// <b>Query.</b> The bar's text and options. Typing compiles the pattern and highlights the matches of the visible rows and of the detail,
/// without scanning. An invalid pattern is shown in the bar and highlights nothing. Esc clears the text, and then closes the bar; closing ends
/// the highlights and keeps the last non-empty query for <c>n</c>, <c>N</c> and F3, which reopen the bar with it.
/// </para>
/// <para>
/// <b>Scan.</b> Enter in the bar, <c>n</c> or F3 in the timeline start a scan toward older events from the selected row, and <c>N</c> or
/// Shift+F3 toward newer ones. Every search compiles a fresh pattern. A new search, a change of the query, closing the bar or opening a
/// detail cancels the running scan, and its late result is ignored. A found row is jumped to; the end of history is a boundary, which
/// leaves the selection where it is. The scan reports the bytes it has read, at most every 100 ms.
/// </para>
/// <para>
/// <b>Detail.</b> While a detail is open the same keys move between its matches within the event, in document order: <c>n</c> and F3 to the
/// next, <c>N</c> and Shift+F3 to the previous. A match in a collapsed node or fold is revealed. The position of the search is the active
/// match when the cursor is on its line; otherwise the cursor's line. There is no wrap: the end is reported.
/// </para>
/// </remarks>
internal sealed class SearchController
{
    /// <summary>The least time between two progress reports of a scan, in milliseconds.</summary>
    internal const long ProgressIntervalMs = 100;

    private readonly ISearchHost _host;
    private readonly TimelineView _timeline;
    private readonly TimelineModel _model;
    private readonly ITimelineSource _source;
    private readonly TimeZoneInfo _zone;
    private readonly Action<Action> _post;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private SearchQuery _lastQuery = new(string.Empty, false, false, false);
    private SearchPattern? _pattern;
    private bool _open;
    private CancellationTokenSource? _scan;
    private int _epoch;
    private string? _message;
    private string? _problem;

    /// <summary>Creates the search over a timeline. The bar is not shown until a search opens.</summary>
    /// <param name="host">The window that owns the search.</param>
    /// <param name="timeline">The timeline view, whose matches are highlighted.</param>
    /// <param name="model">The timeline model. A found row is jumped to.</param>
    /// <param name="source">Finds the matches in the history. The model's source.</param>
    /// <param name="zone">The time zone that the status shows a match's time in.</param>
    /// <param name="post">Runs a continuation on the UI thread.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public SearchController(
        ISearchHost host,
        TimelineView timeline,
        TimelineModel model,
        ITimelineSource source,
        TimeZoneInfo zone,
        Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(post);

        _host = host;
        _timeline = timeline;
        _model = model;
        _source = source;
        _zone = zone;
        _post = post;

        Bar = new SearchBar();
        Bar.QueryChanged += OnQueryChanged;
        Bar.FocusPaneRequested += OnFocusPaneRequested;
        Bar.Closed += CloseBar;
        Bar.FindRequested += backward => Find(next: !backward);
    }

    /// <summary>The search bar. The window places it above the status line.</summary>
    public SearchBar Bar { get; }

    /// <summary>Whether the search bar is open. A closed bar has no highlights and no active query.</summary>
    internal bool IsOpen => _open;

    /// <summary>Whether a scan of the history is running.</summary>
    internal bool IsScanning => _scan is not null;

    /// <summary>
    /// The status of the search: the progress, the outcome of the last search and its read error, joined with <c>·</c>. Null when there is
    /// nothing to report.
    /// </summary>
    public string? Status => _message is null && _problem is null
        ? null
        : string.Join(" · ", new[] { _message, _problem }.Where(part => part is not null));

    /// <summary>
    /// The key hint of the matches, while a query is active in the open bar: <c>n older  N newer</c> in the timeline, <c>n next  N prev</c>
    /// in a detail. Null when no query is active.
    /// </summary>
    /// <param name="inDetail">Whether a detail is open.</param>
    public string? KeyHint(bool inDetail)
    {
        if (!_open || Bar.Query.Text.Length == 0)
        {
            return null;
        }

        return inDetail ? SearchMessages.DetailKeys : SearchMessages.TimelineKeys;
    }

    /// <summary>
    /// Handles a key that no focused view took: <c>/</c> and Ctrl+F open the bar, F3 and <c>n</c> find toward the next match, and Shift+F3 and
    /// <c>N</c> toward the previous one. Returns <c>true</c> for a key of the search.
    /// </summary>
    /// <param name="key">The key.</param>
    public bool HandleShortcut(Key key)
    {
        if (key == Key.F.WithCtrl || IsPlain(key, '/'))
        {
            Open(focusBar: true);
            return true;
        }

        if (key == Key.F3 || IsPlain(key, 'n'))
        {
            Find(next: true);
            return true;
        }

        if (key == Key.F3.WithShift || IsPlain(key, 'N'))
        {
            Find(next: false);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds the next (or previous) match: in the detail while one is open, otherwise toward older (or newer) events from the selected row.
    /// When the bar is closed and a query was used before, the bar reopens with it, without taking the focus.
    /// </summary>
    /// <param name="next"><c>true</c> for the next match (toward older events in the timeline), <c>false</c> for the previous one.</param>
    public void Find(bool next)
    {
        if (!_open)
        {
            if (_lastQuery.Text.Length == 0)
            {
                return;
            }

            Open(focusBar: false);
        }

        if (_host.Detail is { } detail)
        {
            FindInDetail(detail, next);
            return;
        }

        FindInTimeline(older: next);
    }

    /// <summary>Cancels a running scan, as Esc does in the timeline, and shows that it was cancelled. Does nothing when no scan runs.</summary>
    public void CancelScan()
    {
        if (!IsScanning)
        {
            return;
        }

        StopScan();
        _message = SearchMessages.Cancelled;
        _problem = null;
        _host.RefreshStatus();
    }

    /// <summary>Cancels a running scan without a message. The timeline calls it when a detail opens or the controller stops.</summary>
    public void StopScan()
    {
        _epoch++;
        if (_scan is { } scan)
        {
            _scan = null;
            scan.Cancel();
        }
    }

    /// <summary>
    /// Applies the query to a detail that was just loaded: its matches are highlighted, and it scrolls to the first match, when there is one.
    /// The messages of the last search are forgotten.
    /// </summary>
    /// <param name="view">The detail view, loaded.</param>
    public void OnDetailLoaded(DetailView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        _message = null;
        _problem = null;
        view.Search = _pattern;
        if (_pattern is null || view.Document is not { } document)
        {
            return;
        }

        if (DetailSearch.First(document.FullLines, _pattern) is { } first)
        {
            view.ShowHit(first);
        }
    }

    /// <summary>Forgets the messages of the detail once it is closed, so that they do not stay on the timeline's status line.</summary>
    public void OnDetailClosed()
    {
        _message = null;
        _problem = null;
    }

    /// <summary>Opens the bar with the last query, compiled, and highlights its matches. Focuses the field with its text selected when asked.</summary>
    private void Open(bool focusBar)
    {
        if (!_open)
        {
            _open = true;
            Bar.SetQuery(_lastQuery);
            _pattern = Compile(_lastQuery);
            ClearActive();
            ApplyHighlights();
            _host.ShowSearchBar(true);
        }

        if (focusBar)
        {
            Bar.FocusField(selectAll: true);
        }

        _host.RefreshStatus();
    }

    /// <summary>
    /// Starts a scan from the selected row toward older or newer events. The scan runs off the UI thread; its result comes back as
    /// <see cref="Completed"/>. Does nothing with no query or no selected row.
    /// </summary>
    private void FindInTimeline(bool older)
    {
        SearchPattern? pattern = Compile(Bar.Query);
        if (pattern is null || _model.SelectedRow is not { } selected)
        {
            _host.RefreshStatus();
            return;
        }

        RowKey from = selected.Key;
        _pattern = pattern;
        ApplyHighlights();

        StopScan();
        var cancellation = new CancellationTokenSource();
        _scan = cancellation;
        int epoch = _epoch;
        _message = SearchMessages.Progress(0);
        _problem = null;
        _host.RefreshStatus();

        var progress = new ScanProgress(
            bytes => _post(() => Progressed(epoch, bytes)),
            ProgressIntervalMs,
            () => _clock.ElapsedMilliseconds);
        _ = RunScanAsync(epoch, from, older, pattern, cancellation.Token, progress);
    }

    /// <summary>Runs the scan on the thread pool and posts its outcome to the UI thread. A cancelled scan posts nothing.</summary>
    private async Task RunScanAsync(int epoch, RowKey from, bool older, SearchPattern pattern, CancellationToken ct, IProgress<long> progress)
    {
        Logvu.Core.Store.SearchResult result;
        try
        {
            SearchDirection direction = older ? SearchDirection.Older : SearchDirection.Newer;
            result = await _source.FindAsync(from, direction, pattern, null, progress, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            string message = ex.Message;
            _post(() => Failed(epoch, message));
            return;
        }

        _post(() => Completed(epoch, result, older, pattern));
    }

    /// <summary>A scan's result on the UI thread: a found row is jumped to, and the outcome is shown. A superseded scan's result is ignored.</summary>
    private void Completed(int epoch, Logvu.Core.Store.SearchResult result, bool older, SearchPattern pattern)
    {
        if (epoch != _epoch || _scan is null)
        {
            return;
        }

        _scan = null;
        if (result.IsTimedOut || pattern.TimedOut)
        {
            _message = SearchMessages.TimedOut;
        }
        else if (result.Row is { } row)
        {
            _model.JumpTo(row.Key);
            _timeline.ActiveRow = row.Key;
            _message = SearchMessages.Match(row, _zone);
        }
        else
        {
            _message = SearchMessages.Boundary(older);
        }

        _problem = result.Errors.Count > 0 ? SearchMessages.Problem(result.Errors[^1]) : null;
        _host.RefreshStatus();
    }

    private void Failed(int epoch, string message)
    {
        if (epoch != _epoch || _scan is null)
        {
            return;
        }

        _scan = null;
        _message = SearchMessages.Failed(message);
        _problem = null;
        _host.RefreshStatus();
    }

    private void Progressed(int epoch, long bytes)
    {
        if (epoch != _epoch || _scan is null)
        {
            return;
        }

        _message = SearchMessages.Progress(bytes);
        _host.RefreshStatus();
    }

    /// <summary>Moves to the next (or previous) match of the detail, from the cursor, and reveals it. Reports the end of the event.</summary>
    private void FindInDetail(DetailView detail, bool next)
    {
        SearchPattern? pattern = Compile(Bar.Query);
        if (pattern is null || detail.Document is not { Lines.Count: > 0 } document)
        {
            _host.RefreshStatus();
            return;
        }

        _pattern = pattern;
        ApplyHighlights();

        int cursor = document.FullIndex(detail.CursorLine);
        DetailHit? active = detail.ActiveHit is { } hit && hit.Line == cursor ? hit : null;
        int line = active?.Line ?? cursor;
        int start = active?.Start ?? -1;
        DetailHit? found = next
            ? DetailSearch.Next(document.FullLines, pattern, line, start)
            : DetailSearch.Previous(document.FullLines, pattern, line, start);

        if (pattern.TimedOut)
        {
            _message = SearchMessages.TimedOut;
        }
        else if (found is { } target)
        {
            detail.ShowHit(target);
            _message = null;
        }
        else
        {
            _message = SearchMessages.NoMatchesInEvent;
        }

        _problem = null;
        _host.RefreshStatus();
    }

    /// <summary>Every change of the bar's text or options: compiles the query and highlights its matches. Nothing is scanned.</summary>
    private void OnQueryChanged(SearchQuery query)
    {
        if (query.Text.Length > 0)
        {
            _lastQuery = query;
        }

        StopScan();
        _message = null;
        _problem = null;
        _pattern = Compile(query);
        ClearActive();
        ApplyHighlights();
        _host.RefreshStatus();
    }

    /// <summary>Enter in the bar: the focus goes back to the content, and the search runs from there.</summary>
    private void OnFocusPaneRequested()
    {
        _host.FocusContent();
        Find(next: true);
    }

    /// <summary>
    /// Closes the bar: remembers the text, keeps the last non-empty query for <c>n</c>, <c>N</c> and F3 (with the bar's options), cancels
    /// the scan, clears the highlights and hides the bar. The focus goes back to the content when the bar had it.
    /// </summary>
    private void CloseBar()
    {
        if (!_open)
        {
            return;
        }

        Bar.RememberQuery();
        SearchQuery query = Bar.Query;
        _lastQuery = query.Text.Length > 0
            ? query
            : _lastQuery with { CaseSensitive = query.CaseSensitive, WholeWord = query.WholeWord, Regex = query.Regex };

        bool hadFocus = Bar.HasFieldFocus;
        StopScan();
        _open = false;
        _pattern = null;
        _message = null;
        _problem = null;
        ClearActive();
        ApplyHighlights();
        if (hadFocus)
        {
            _host.FocusContent();
        }

        _host.ShowSearchBar(false);
        _host.RefreshStatus();
    }

    /// <summary>Compiles a query for the bar. Shows an invalid pattern in the bar; an empty query compiles to <c>null</c> without a message.</summary>
    private SearchPattern? Compile(SearchQuery query)
    {
        SearchPattern? pattern = SearchPattern.Create(query, out string? error);
        Bar.SetStatus(error is null ? string.Empty : SearchMessages.InvalidPattern);
        return pattern;
    }

    /// <summary>Gives the timeline and the open detail the current pattern. Their active matches are kept.</summary>
    private void ApplyHighlights()
    {
        _timeline.Search = _pattern;
        if (_host.Detail is { } detail)
        {
            detail.Search = _pattern;
        }
    }

    /// <summary>Forgets the active match of the timeline and of the detail.</summary>
    private void ClearActive()
    {
        _timeline.ActiveRow = null;
        _host.Detail?.ClearActiveHit();
    }

    private static bool IsPlain(Key key, char ch) => !key.IsCtrl && !key.IsAlt && key.AsRune.Value == ch;
}
