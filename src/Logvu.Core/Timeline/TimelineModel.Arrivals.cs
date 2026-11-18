using Logvu.Core.Store;

namespace Logvu.Core.Timeline;

public sealed partial class TimelineModel
{
    /// <summary>
    /// Takes in arrivals under a filter. The rows the store published were not filtered, so none of them is placed directly. Instead the
    /// window reads its own matches:
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>A loaded row that changed reloads the window, since it may no longer match. The reload is positioned as a reload is.</item>
    /// <item>At the newest end with rows loaded, a newer load from the window's newest row reads the matches, which the completion counts
    /// (paused) or follows (Follow).</item>
    /// <item>At the newest end with no rows loaded, a newest load reads the matches, since there is no row to read from.</item>
    /// <item>Away from the newest end, the arrivals cannot be counted cheaply, so only <see cref="HasUnseenArrivals"/> is set when paused.
    /// Followed, the window reloads from the newest end instead.</item>
    /// </list>
    /// </remarks>
    private void ArriveFiltered(IReadOnlyList<EventRow> added, IReadOnlyList<EventRow> updated)
    {
        RowKey? earliest = null;
        foreach (EventRow row in updated)
        {
            if (IsLoaded(row.Key) && (earliest is null || _compare(row.Key, earliest.Value) < 0))
            {
                earliest = row.Key;
            }
        }

        if (earliest is RowKey truncateAt)
        {
            TruncateAt(truncateAt);
            return;
        }

        if (added.Count == 0)
        {
            return;
        }

        if (_newer is { Truncated: true } refill)
        {
            // The window is already being refilled from the truncation point, and that read may have started before these arrivals.
            Cancel(ref _newer);
            Request(LoadKind.Newer, refill.Anchor, inclusive: true, isArrival: true, truncated: true);
            return;
        }

        if (!_atEnd || !_placed)
        {
            if (_mode == TimelineMode.Follow)
            {
                ReloadNewest();
            }
            else
            {
                MarkUnseen();
            }

            return;
        }

        if (_rows.Count == 0)
        {
            Cancel(ref _newest);
            Request(LoadKind.Newest, default, isArrival: true);
            return;
        }

        Cancel(ref _newer);
        Request(LoadKind.Newer, _rows[^1].Key, isArrival: true);
    }

    /// <summary>
    /// Removes a loaded row and every row after it, then refills the window from that row: the store's row for the key may now differ,
    /// or no longer match. The rows that come back after the removed ones are counted only when they are new, which the count horizon
    /// decides. The selection is kept when its row is before the removed rows; a removed selection is restored when the refill brings it
    /// back (Paused), and otherwise falls to the nearest row before it.
    /// </summary>
    private void TruncateAt(RowKey key)
    {
        int index = LowerBound(key);
        bool wasAtEnd = _atEnd;
        RowKey last = _rows[^1].Key;
        RowKey? selected = SelectedRow?.Key;
        bool selectionRemoved = selected is RowKey chosen && _compare(chosen, key) >= 0;

        _rows.RemoveRange(index, _rows.Count - index);
        _atEnd = false;
        _pivot = null;
        _selected = Math.Max(0, Math.Min(_selected, LastIndex));
        _restoreSelection = selectionRemoved && _mode == TimelineMode.Paused ? selected : null;

        if (wasAtEnd)
        {
            // Everything up to the old last row is already loaded, and counted if it arrived while paused.
            _countHorizon = _countHorizon is RowKey previous && _compare(previous, last) > 0 ? previous : last;
        }
        else
        {
            // The window did not reach the newest end, so the rows the refill reads may have been there before the pause: count none.
            _countSuppressed = true;
            if (_mode == TimelineMode.Paused)
            {
                MarkUnseen();
            }
        }

        Cancel(ref _newer);
        Request(LoadKind.Newer, key, inclusive: true, isArrival: true, truncated: true);
    }

    /// <summary>Whether a row with the key is loaded in the window.</summary>
    private bool IsLoaded(RowKey key)
    {
        int index = LowerBound(key);
        return index < _rows.Count && _rows[index].Key == key;
    }

    /// <summary>
    /// Counts matching arrivals that were read while paused. Rows at or before the count horizon were counted or loaded already, and a
    /// suppressed refill counts nothing. Followed, the rows are shown rather than counted.
    /// </summary>
    private void CountArrivals(IReadOnlyList<EventRow> rows)
    {
        if (_mode != TimelineMode.Paused || _countSuppressed)
        {
            return;
        }

        _newCount += _countHorizon is RowKey horizon
            ? rows.Count(row => _compare(row.Key, horizon) > 0)
            : rows.Count;
    }

    /// <summary>
    /// Sets the unseen flag. It covers the loads issued so far: only a load issued after this point clears it, since an earlier load may
    /// have missed the arrival.
    /// </summary>
    private void MarkUnseen()
    {
        _hasUnseenArrivals = true;
        _unseenMark = _requestSequence;
    }
}
