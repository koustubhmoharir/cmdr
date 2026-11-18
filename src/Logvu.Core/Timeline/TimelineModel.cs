using Logvu.Core.Store;

namespace Logvu.Core.Timeline;

/// <summary>
/// The reader's position in the timeline: a window of loaded rows, the viewport on it, the selection, and whether the reader follows the
/// newest rows or holds its place. It has no UI dependency. The owner calls every public member on its UI thread, and load results
/// come back through the <c>post</c> action, so every state change happens on that thread.
/// </summary>
/// <remarks>
/// The window is oldest first. <see cref="Top"/> and the selection are window indices, with the invariant
/// <c>Top ≤ selected ≤ Top + Height − 1</c> whenever the window has rows. The transitions are split across the partial files:
/// this file holds the public API, the window placement and the settling; <c>TimelineModel.Moves.cs</c> holds the command transitions;
/// <c>TimelineModel.Loads.cs</c> holds the requests, the margin and the trimming.
/// </remarks>
public sealed partial class TimelineModel
{
    /// <summary>The page size of a load, in viewport heights.</summary>
    private const int PageHeights = 4;

    /// <summary>The rows kept loaded beyond each viewport edge, in viewport heights, unless the window reaches the history edge.</summary>
    private const int MarginHeights = 2;

    /// <summary>The distance beyond each viewport edge past which rows are trimmed, in viewport heights.</summary>
    private const int TrimHeights = 8;

    private readonly ITimelineSource _source;
    private readonly Action<Action> _post;
    private readonly Comparison<RowKey> _compare;
    private readonly List<EventRow> _rows = [];
    private readonly Queue<TimelineCommand> _queue = new();
    private IRowFilter? _filter;
    private int _height = 1;
    private int _top;
    private int _selected;
    private int _generation;
    private bool _atStart;
    private bool _atEnd;
    private bool _placed;
    private RowKey? _pivot;
    private int _pivotScreenRow;
    private TimelineMode _mode = TimelineMode.Follow;
    private int _newCount;

    /// <summary>Creates a model with no rows. Call <see cref="Start"/> to load the timeline.</summary>
    /// <param name="source">The rows to load.</param>
    /// <param name="post">Runs a continuation on the owner's UI thread. Load results are applied through it.</param>
    /// <param name="compare">Orders two keys in the timeline, as <see cref="LogStore.Compare"/> does.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public TimelineModel(ITimelineSource source, Action<Action> post, Comparison<RowKey> compare)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(compare);
        _source = source;
        _post = post;
        _compare = compare;
    }

    /// <summary>Raised after each public call and each applied load changes or may have changed the state.</summary>
    public event Action? Changed;

    /// <summary>Whether the timeline follows the newest rows or is paused.</summary>
    public TimelineMode Mode => _mode;

    /// <summary>The matching rows that arrived while paused, since the last resume.</summary>
    public int NewCount => _newCount;

    /// <summary>The viewport height, in rows.</summary>
    public int Height => _height;

    /// <summary>The rows in the viewport, at most <see cref="Height"/> of them, top first.</summary>
    public IReadOnlyList<EventRow> VisibleRows => _rows.GetRange(_top, Math.Min(_height, _rows.Count - _top));

    /// <summary>The selected row's screen row within the viewport, or <c>-1</c> when there are no rows.</summary>
    public int SelectedScreenRow => _rows.Count == 0 ? -1 : _selected - _top;

    /// <summary>The selected row, or <c>null</c> when there are no rows.</summary>
    public EventRow? SelectedRow => _rows.Count == 0 ? null : _rows[_selected];

    /// <summary>Whether a load is outstanding.</summary>
    public bool IsLoading => _newest is not null || _older is not null || _newer is not null;

    /// <summary>The number of rows in the window, loaded and not yet trimmed. Read by tests.</summary>
    internal int LoadedCount => _rows.Count;

    /// <summary>Whether the window reaches the start of history under the active filter.</summary>
    public bool AtStart => _atStart;

    /// <summary>Whether the window reaches the newest end of history under the active filter.</summary>
    public bool AtEnd => _atEnd;

    /// <summary>Loads the newest rows in Follow mode, with the viewport <paramref name="height"/> rows high.</summary>
    /// <param name="height">The viewport height. It is at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="height"/> is below 1.</exception>
    public void Start(int height)
    {
        ValidateHeight(height);
        _height = height;
        _mode = TimelineMode.Follow;
        _newCount = 0;
        ReloadNewest();
        Finish();
    }

    /// <summary>
    /// Applies a key command. A manual navigation pauses Follow first. A command that needs rows that are not loaded yet waits in order
    /// and runs when they arrive.
    /// </summary>
    /// <param name="command">The command.</param>
    public void Execute(TimelineCommand command)
    {
        switch (command)
        {
            case TimelineCommand.End:
                ResumeFollow();
                break;
            case TimelineCommand.TogglePause when _mode == TimelineMode.Paused:
                ResumeFollow();
                break;
            case TimelineCommand.TogglePause:
                SetPaused();
                break;
            default:
                SetPaused();
                _queue.Enqueue(command);
                break;
        }

        Finish();
    }

    /// <summary>Pauses Follow. It does nothing else, and does nothing when the timeline is already paused.</summary>
    public void Pause()
    {
        SetPaused();
        Finish();
    }

    /// <summary>
    /// Pauses Follow and selects a visible row, as a click does. The viewport does not move. A row outside the viewport's rows is
    /// ignored, and so is any call while no rows are loaded. Queued navigation is dropped: the selection the caller made supersedes it.
    /// </summary>
    /// <param name="screenRow">The row's screen row within the viewport, counted from the top, with 0 the top visible row.</param>
    public void SelectVisibleRow(int screenRow)
    {
        if (screenRow < 0 || screenRow >= VisibleRows.Count)
        {
            return;
        }

        SetPaused();
        _queue.Clear();
        _pivot = null;
        _selected = _top + screenRow;
        Finish();
    }

    /// <summary>Changes the viewport height. The selected row keeps its screen row where it can, and the window is refilled.</summary>
    /// <param name="height">The new viewport height. It is at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="height"/> is below 1.</exception>
    public void Resize(int height)
    {
        ValidateHeight(height);
        int screenRow = SelectedScreenRow;
        _height = height;
        if (_pivot is not null)
        {
            _pivotScreenRow = Math.Min(_pivotScreenRow, height - 1);
        }
        else if (_mode == TimelineMode.Paused && screenRow >= 0)
        {
            _top = _selected - Math.Min(screenRow, height - 1);
        }

        Finish();
    }

    /// <summary>
    /// Applies a row filter and reloads. Paused, the reload is positioned around the selected row; in Follow it loads the newest rows.
    /// </summary>
    /// <param name="filter">The filter, or <c>null</c> for every row.</param>
    public void SetFilter(IRowFilter? filter)
    {
        _filter = filter;
        ReloadAroundSelection();
        Finish();
    }

    /// <summary>
    /// Reloads the window. Paused, the reload is positioned around the selected row; in Follow it loads the newest rows. The rows
    /// are reloaded because the store's order may have changed.
    /// </summary>
    public void Reset()
    {
        ReloadAroundSelection();
        Finish();
    }

    /// <summary>
    /// Pauses Follow, reloads the window around a row and selects it. The row is placed on the middle screen row of the viewport where the
    /// loaded rows allow it: the rows before it load as the viewport needs them, and at either end of history the row moves toward the
    /// edge. A later jump, a reset or a resume supersedes an outstanding jump.
    /// </summary>
    /// <param name="key">The row to show. Its generation must be in the timeline.</param>
    public void JumpTo(RowKey key)
    {
        SetPaused();
        BeginReload();
        _pivot = key;
        _pivotScreenRow = _height / 2;
        Request(LoadKind.Newer, key, inclusive: true);
        Finish();
    }

    /// <summary>
    /// Takes in the rows the store published. Follow appends the added rows and keeps the newest selected. Paused counts them, and
    /// appends them to the window when the window reaches the newest end. Updated rows replace the loaded rows with their keys.
    /// </summary>
    /// <param name="added">The matching rows that arrived, oldest first. The caller has applied the filter.</param>
    /// <param name="updated">The matching rows that changed in place. The caller has applied the filter.</param>
    public void Arrive(IReadOnlyList<EventRow> added, IReadOnlyList<EventRow> updated)
    {
        ArgumentNullException.ThrowIfNull(added);
        ArgumentNullException.ThrowIfNull(updated);
        ReplaceRows(updated);
        if (_mode == TimelineMode.Follow)
        {
            AppendRows(added);
            _selected = LastIndex;
        }
        else
        {
            _newCount += added.Count;
            if (_atEnd && _placed)
            {
                AppendRows(added);
            }
        }

        Finish();
    }

    private void ValidateHeight(int height) => ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

    private int LastIndex => Math.Max(0, _rows.Count - 1);

    private void SetPaused() => _mode = TimelineMode.Paused;

    private void ResumeFollow()
    {
        _mode = TimelineMode.Follow;
        _newCount = 0;
        ReloadNewest();
    }

    private void ReloadAroundSelection()
    {
        RowKey? anchor = _pivot ?? SelectedRow?.Key;
        int screenRow = Math.Max(0, SelectedScreenRow);
        BeginReload();
        if (_mode == TimelineMode.Paused && anchor is RowKey key)
        {
            _pivot = key;
            _pivotScreenRow = screenRow;
            Request(LoadKind.Newer, key, inclusive: true);
        }
        else
        {
            Request(LoadKind.Newest, default);
        }
    }

    private void ReloadNewest()
    {
        BeginReload();
        Request(LoadKind.Newest, default);
    }

    /// <summary>Drops the window, the queue and the outstanding loads, and starts a new generation.</summary>
    private void BeginReload()
    {
        _generation++;
        Cancel(ref _newest);
        Cancel(ref _older);
        Cancel(ref _newer);
        _queue.Clear();
        _rows.Clear();
        _top = 0;
        _selected = 0;
        _atStart = false;
        _atEnd = false;
        _placed = false;
        _pivot = null;
    }

    /// <summary>Places the newest page: the loaded rows that arrived while it ran are kept after it.</summary>
    private void PlaceNewest(RowPage page)
    {
        List<EventRow> arrivals = [.. _rows];
        _rows.Clear();
        _rows.AddRange(page.Rows);
        AppendRows(arrivals);
        _atStart = page.ReachedBoundary;
        _atEnd = true;
        _placed = true;
        _selected = LastIndex;
    }

    /// <summary>Appends the arrivals that are newer than the window's newest row.</summary>
    private void AppendRows(IEnumerable<EventRow> rows)
    {
        foreach (EventRow row in rows)
        {
            if (_rows.Count == 0 || _compare(row.Key, _rows[^1].Key) > 0)
            {
                _rows.Add(row);
            }
        }
    }

    /// <summary>Replaces each updated row that is loaded, matching by key.</summary>
    private void ReplaceRows(IEnumerable<EventRow> updated)
    {
        foreach (EventRow row in updated)
        {
            int index = LowerBound(row.Key);
            if (index < _rows.Count && _rows[index].Key == row.Key)
            {
                _rows[index] = row;
            }
        }
    }

    /// <summary>The first window index whose key is not before <paramref name="key"/>, or the row count when there is none.</summary>
    private int LowerBound(RowKey key)
    {
        int low = 0;
        int high = _rows.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (_compare(_rows[middle].Key, key) < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>
    /// Settles the state after a change: the pivot is resolved, the viewport is kept valid, the window is trimmed and the margin is
    /// requested, then the queued commands run.
    /// </summary>
    private void Settle()
    {
        SelectPivot();
        Refresh();
        Drain();
    }

    /// <summary>Keeps the viewport valid for the window, then trims it and requests the margin.</summary>
    private void Refresh()
    {
        Normalize();
        Trim();
        EnsureMargin();
    }

    /// <summary>
    /// Completes a change: settles the state and raises <see cref="Changed"/>. Every public call ends with it, and so does every
    /// applied load.
    /// </summary>
    private void Finish()
    {
        Settle();
        Changed?.Invoke();
    }

    /// <summary>
    /// Places the selection on the row a reload is positioned around: that row when it is loaded, otherwise the nearest row before it.
    /// The pivot stays until the viewport can show the selection at its screen row, or the window reaches the start of history, so that
    /// older rows loaded after a newer seed still move the viewport to keep the screen row.
    /// </summary>
    private void SelectPivot()
    {
        if (_pivot is not RowKey pivot || !_placed)
        {
            return;
        }

        int index = LowerBound(pivot);
        if (index < _rows.Count)
        {
            _selected = index;
        }
        else if (_atEnd)
        {
            _selected = LastIndex;
        }
        else
        {
            return;
        }

        _top = _selected - _pivotScreenRow;
        if (_atStart || _selected >= _pivotScreenRow)
        {
            _pivot = null;
        }
    }

    /// <summary>Keeps the viewport within the window, so that the viewport is full whenever the window has enough rows.</summary>
    private void Normalize()
    {
        if (_rows.Count == 0)
        {
            _top = 0;
            _selected = 0;
            return;
        }

        _selected = Math.Clamp(_selected, 0, LastIndex);
        int maxTop = Math.Max(0, _rows.Count - _height);
        int lowest = Math.Max(0, _selected - _height + 1);
        int highest = Math.Min(_selected, maxTop);
        _top = Math.Clamp(_top, lowest, highest);
    }
}
