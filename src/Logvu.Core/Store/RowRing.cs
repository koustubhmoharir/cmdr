using System.Collections.ObjectModel;

namespace Logvu.Core.Store;

/// <summary>
/// A bounded cache of the newest rows in timeline order. It holds rows only, never event text, so its memory is its capacity in rows.
/// Owned by the store's single worker: it is not thread-safe. Readers on other threads use the immutable copy from
/// <see cref="Snapshot"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Storage.</b> A circular buffer of <see cref="Capacity"/> slots. Every row has a sequence number: the oldest row of the ring is
/// <c>first</c>, the next is <c>first + 1</c>, and so on, so a row's slot is its sequence number modulo the capacity. A dictionary from
/// <see cref="RowKey"/> to sequence number finds a row in O(1).
/// </para>
/// <para>
/// <b>Order.</b> The rows are in the order of the <c>compare</c> the ring was created with, which is the catalog's order. A row is only
/// ever appended after the newest row, or replaces a row with the same key in place, so the ring stays sorted.
/// </para>
/// <para>
/// <b>Coverage.</b> <see cref="CoversNewest"/> is true once the ring has been appended to, or warmed by <see cref="PrependOlder"/>: from
/// then on it holds the newest history, and <see cref="TryNewest"/> may answer from it. Before that it holds nothing known.
/// </para>
/// </remarks>
public sealed class RowRing
{
    private readonly Comparison<RowKey> _compare;
    private readonly EventRow?[] _slots;
    private readonly Dictionary<RowKey, long> _sequences = [];
    private long _first;
    private int _count;
    private bool _coversNewest;
    private ReadOnlyCollection<EventRow>? _snapshot;

    /// <summary>Creates an empty ring.</summary>
    /// <param name="capacity">The most rows the ring holds. It is at least 1.</param>
    /// <param name="compare">The timeline order of the keys: a negative number, zero, or a positive number as the first key is before, the same as, or after the second. The catalog's <see cref="Catalog.Compare"/> is the usual one.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is below 1.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="compare"/> is <c>null</c>.</exception>
    public RowRing(int capacity, Comparison<RowKey> compare)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentNullException.ThrowIfNull(compare);
        _slots = new EventRow?[capacity];
        _compare = compare;
    }

    /// <summary>The most rows the ring holds.</summary>
    public int Capacity => _slots.Length;

    /// <summary>The number of rows in the ring.</summary>
    public int Count => _count;

    /// <summary>
    /// Whether the ring holds the newest rows of history: it has been appended to, or warmed by <see cref="PrependOlder"/> since it was
    /// created. A ring that covers the newest rows answers <see cref="TryNewest"/> from its own rows.
    /// </summary>
    public bool CoversNewest => _coversNewest;

    /// <summary>The key of the oldest row, or <c>null</c> when the ring is empty.</summary>
    public RowKey? Oldest => _count == 0 ? null : Get(_first).Key;

    /// <summary>The key of the newest row, or <c>null</c> when the ring is empty.</summary>
    public RowKey? Newest => _count == 0 ? null : Get(_first + _count - 1).Key;

    /// <summary>
    /// Adds a row at the newest end, or replaces a row in place. A row whose key is already in the ring replaces that row wherever it is,
    /// so appending the same event again (a provisional row that grew, or a repeated notification) is idempotent. Any other row must be
    /// newer than the newest row. When the ring is full, the oldest row is evicted to make room.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <exception cref="ArgumentNullException"><paramref name="row"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The row's key is not in the ring and is not newer than the newest row.</exception>
    public void Append(EventRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (_sequences.TryGetValue(row.Key, out long present))
        {
            _slots[Slot(present)] = row;
            _snapshot = null;
            return;
        }

        if (_count > 0 && _compare(row.Key, Newest!.Value) <= 0)
        {
            throw new ArgumentException(
                $"Row {row.Key} is not newer than the newest row {Newest}. The ring takes rows in timeline order.", nameof(row));
        }

        if (_count == _slots.Length)
        {
            _sequences.Remove(Get(_first).Key);
            _first++;
            _count--;
        }

        long sequence = _first + _count;
        _slots[Slot(sequence)] = row;
        _sequences.Add(row.Key, sequence);
        _count++;
        _coversNewest = true;
        _snapshot = null;
    }

    /// <summary>
    /// Warms the ring with rows from history, which are older than the ring's oldest row. As many rows as fit without evicting any row
    /// of the ring are added: the newest of <paramref name="olderRows"/>, which are the ones adjacent to the ring. When the ring is
    /// empty, the newest rows fill it, and it then covers the newest rows (<see cref="CoversNewest"/>). So warm-up is called with the
    /// newest rows of history, read by a newest read, and then with older pages only to extend a ring that is not full.
    /// </summary>
    /// <param name="olderRows">The rows, oldest first. They must be in order, each older than the ring's oldest row, and must be the rows
    /// immediately before it: a gap would make the ring lie about history.</param>
    /// <exception cref="ArgumentNullException"><paramref name="olderRows"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The rows are not in order, or one is not older than the ring's oldest row.</exception>
    public void PrependOlder(IReadOnlyList<EventRow> olderRows)
    {
        ArgumentNullException.ThrowIfNull(olderRows);
        RowKey? oldest = Oldest;
        for (int i = 0; i < olderRows.Count; i++)
        {
            RowKey key = olderRows[i].Key;
            if (i > 0 && _compare(olderRows[i - 1].Key, key) >= 0)
            {
                throw new ArgumentException($"Row {key} is not after the row before it. The rows must be oldest first.", nameof(olderRows));
            }

            if (oldest is { } first && _compare(key, first) >= 0)
            {
                throw new ArgumentException($"Row {key} is not older than the ring's oldest row {first}.", nameof(olderRows));
            }
        }

        bool wasEmpty = _count == 0;
        int take = Math.Min(_slots.Length - _count, olderRows.Count);
        long newFirst = _first - take;
        for (int i = 0; i < take; i++)
        {
            EventRow row = olderRows[olderRows.Count - take + i];
            long sequence = newFirst + i;
            _slots[Slot(sequence)] = row;
            _sequences.Add(row.Key, sequence);
        }

        if (take > 0)
        {
            _first = newFirst;
            _count += take;
            _snapshot = null;
        }

        if (wasEmpty)
        {
            _coversNewest = true;
        }
    }

    /// <summary>
    /// Serves up to <paramref name="count"/> rows older than <paramref name="anchor"/> from the ring, oldest first. The anchor must be in
    /// the ring. When the ring holds fewer older rows than <paramref name="count"/>, the result is short and
    /// <paramref name="atRingStart"/> is <c>true</c>: the caller continues from history, older than <see cref="Oldest"/>.
    /// </summary>
    /// <param name="anchor">The key of a row in the ring.</param>
    /// <param name="count">The most rows to return. It is at least 1.</param>
    /// <param name="rows">The rows, oldest first. Empty when the anchor is the oldest row.</param>
    /// <param name="atRingStart">Whether the result is short because the ring's oldest end was reached.</param>
    /// <returns><c>true</c> when the anchor is in the ring and the result is served from it; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is below 1.</exception>
    public bool TryOlder(RowKey anchor, int count, out IReadOnlyList<EventRow> rows, out bool atRingStart)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        rows = Array.Empty<EventRow>();
        atRingStart = false;
        if (!_sequences.TryGetValue(anchor, out long anchorSequence))
        {
            return false;
        }

        long available = anchorSequence - _first;
        int take = (int)Math.Min(count, available);
        atRingStart = take < count;
        rows = Range(anchorSequence - take, take);
        return true;
    }

    /// <summary>
    /// Serves up to <paramref name="count"/> rows newer than <paramref name="anchor"/> from the ring, oldest first. The anchor must be in
    /// the ring. When the ring holds fewer newer rows than <paramref name="count"/>, the result is short and
    /// <paramref name="atRingEnd"/> is <c>true</c>: the ring's newest row was reached.
    /// </summary>
    /// <param name="anchor">The key of a row in the ring.</param>
    /// <param name="count">The most rows to return. It is at least 1.</param>
    /// <param name="rows">The rows, oldest first. Empty when the anchor is the newest row.</param>
    /// <param name="atRingEnd">Whether the result is short because the ring's newest end was reached.</param>
    /// <returns><c>true</c> when the anchor is in the ring and the result is served from it; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is below 1.</exception>
    public bool TryNewer(RowKey anchor, int count, out IReadOnlyList<EventRow> rows, out bool atRingEnd)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        rows = Array.Empty<EventRow>();
        atRingEnd = false;
        if (!_sequences.TryGetValue(anchor, out long anchorSequence))
        {
            return false;
        }

        long available = _first + _count - 1 - anchorSequence;
        int take = (int)Math.Min(count, available);
        atRingEnd = take < count;
        rows = Range(anchorSequence + 1, take);
        return true;
    }

    /// <summary>
    /// Serves up to <paramref name="count"/> of the newest rows, oldest first. Requires <see cref="CoversNewest"/>. When the ring holds
    /// fewer rows than <paramref name="count"/>, the result is short and <paramref name="atRingStart"/> is <c>true</c>: the caller
    /// continues from history, older than <see cref="Oldest"/>.
    /// </summary>
    /// <param name="count">The most rows to return. It is at least 1.</param>
    /// <param name="rows">The rows, oldest first.</param>
    /// <param name="atRingStart">Whether the result is short because the ring's oldest end was reached.</param>
    /// <returns><c>true</c> when the ring covers the newest rows and the result is served from it; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is below 1.</exception>
    public bool TryNewest(int count, out IReadOnlyList<EventRow> rows, out bool atRingStart)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        rows = Array.Empty<EventRow>();
        atRingStart = false;
        if (!_coversNewest)
        {
            return false;
        }

        int take = Math.Min(count, _count);
        atRingStart = take < count;
        rows = Range(_first + _count - take, take);
        return true;
    }

    /// <summary>
    /// An immutable copy of the rows, oldest first. The same instance is returned until the ring changes, so a caller that polls it
    /// gets no new copy while nothing has changed. A change is an append, a replacement, an eviction or a warm-up that added rows.
    /// </summary>
    public IReadOnlyList<EventRow> Snapshot()
    {
        if (_snapshot is null)
        {
            var rows = new EventRow[_count];
            for (int i = 0; i < _count; i++)
            {
                rows[i] = Get(_first + i);
            }

            _snapshot = new ReadOnlyCollection<EventRow>(rows);
        }

        return _snapshot;
    }

    /// <summary>Whether a row with the key is in the ring.</summary>
    public bool Contains(RowKey key) => _sequences.ContainsKey(key);

    /// <summary>
    /// Removes every row. The ring then holds nothing, and covers no history until rows are appended or warmed.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_slots);
        _sequences.Clear();
        _first = 0;
        _count = 0;
        _coversNewest = false;
        _snapshot = null;
    }

    /// <summary>The slot of a sequence number. Sequence numbers may be negative after warm-up, hence the adjustment.</summary>
    private int Slot(long sequence)
    {
        long slot = sequence % _slots.Length;
        return (int)(slot < 0 ? slot + _slots.Length : slot);
    }

    /// <summary>The row with a sequence number, which must be in the ring.</summary>
    private EventRow Get(long sequence) => _slots[Slot(sequence)]!;

    private EventRow[] Range(long firstSequence, int count)
    {
        var rows = new EventRow[count];
        for (int i = 0; i < count; i++)
        {
            rows[i] = Get(firstSequence + i);
        }

        return rows;
    }
}
