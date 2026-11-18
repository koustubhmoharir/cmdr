using Logvu.Core.Config;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;

namespace Logvu.Core.Store;

/// <summary>
/// The read side of <see cref="LogStore"/>. Each read works on one <see cref="StoreState"/> and on files opened for that read; it never
/// touches the worker's catalog or ring, so any number of reads may run at once on the thread pool.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ring and history.</b> The ring holds the newest rows, contiguously. Older rows come from <see cref="HistoryReader"/>. The ring's rows
/// that history does not hold are the <b>tail</b>: rows whose offset is at or past their member's read cursor. Only the live member's
/// provisional event is there, and it is the newest row of the timeline. History never returns the tail.
/// </para>
/// <para>
/// <b>Unfiltered reads</b> are served from the ring and, past its oldest row, from history. <b>Filtered reads</b> cannot use the ring,
/// because it holds no event text: history is read with the filter, and each tail row is matched by re-reading it from its file. The
/// two candidate lists are merged in timeline order, so the page is the page a single walk over all rows would give.
/// </para>
/// <para>
/// <b>Identity.</b> Every file is opened through <see cref="MemberFile.TryOpen"/>, which verifies the identity against the snapshot's
/// member, so content is never taken from a file that a path has since come to name.
/// </para>
/// </remarks>
internal sealed class StoreReader
{
    /// <summary>The reason given for an event whose file is no longer on disk.</summary>
    public const string ExpiredReason = "Source file expired";

    private readonly HistoryReader _history;
    private readonly ParseContext _context;

    /// <summary>Creates a reader.</summary>
    /// <param name="config">The configuration. Its time zone parses the tail rows that a filter re-reads.</param>
    /// <param name="history">The history reader. It holds no state between calls, so the store may share it.</param>
    public StoreReader(LogvuConfig config, HistoryReader history)
    {
        _context = new ParseContext(config.TimeZone);
        _history = history;
    }

    /// <summary>Reads up to <paramref name="count"/> rows of the newest history, oldest first.</summary>
    /// <param name="state">The state to read.</param>
    /// <param name="count">The most rows to return. It is at least 1.</param>
    /// <param name="filter">The predicate a row must match, or <c>null</c>.</param>
    /// <param name="progress">Receives the event bytes scanned in history. May be <c>null</c>.</param>
    /// <param name="ct">Checked at least once per batch and per event.</param>
    public RowPage Newest(StoreState state, int count, IRowFilter? filter, IProgress<long>? progress, CancellationToken ct)
    {
        CatalogSnapshot catalog = state.Catalog;
        IReadOnlyList<EventRow> ring = state.Ring;
        if (filter is null)
        {
            if (!state.RingCoversNewest || ring.Count == 0)
            {
                return _history.ReadNewest(catalog, count, null, progress, ct);
            }

            int take = Math.Min(count, ring.Count);
            var rows = new List<EventRow>(take);
            for (int i = ring.Count - take; i < ring.Count; i++)
            {
                rows.Add(ring[i]);
            }

            if (take == count)
            {
                return new RowPage(rows, ReachedBoundary: false, Errors: []);
            }

            RowPage older = _history.ReadOlder(catalog, ring[0].Key, count - take, null, progress, ct);
            return new RowPage(older.Rows.Concat(rows).ToList(), older.ReachedBoundary, older.Errors);
        }

        var errors = new List<MemberReadError>();
        List<EventRow> tail = MatchedTail(state, filter, anchor: null, after: false, errors, ct);
        RowPage history = _history.ReadNewest(catalog, count, filter, progress, ct);
        List<EventRow> page = Merge(history.Rows, tail, catalog);
        page = page.GetRange(Math.Max(0, page.Count - count), Math.Min(count, page.Count));
        return new RowPage(page, page.Count < count, history.Errors.Concat(errors).ToList());
    }

    /// <summary>Reads up to <paramref name="count"/> rows strictly older than <paramref name="anchor"/>, oldest first.</summary>
    /// <param name="state">The state to read.</param>
    /// <param name="anchor">The row the page is older than. Its generation must be in the state's timeline.</param>
    /// <param name="count">The most rows to return. It is at least 1.</param>
    /// <param name="filter">The predicate a row must match, or <c>null</c>.</param>
    /// <param name="progress">Receives the event bytes scanned in history. May be <c>null</c>.</param>
    /// <param name="ct">Checked at least once per batch and per event.</param>
    /// <exception cref="ArgumentException">The anchor's generation is not in the timeline.</exception>
    public RowPage Older(StoreState state, RowKey anchor, int count, IRowFilter? filter, IProgress<long>? progress, CancellationToken ct)
    {
        RequireKnown(state, anchor);
        CatalogSnapshot catalog = state.Catalog;
        if (filter is null)
        {
            int index = Find(state, anchor);
            if (index < 0)
            {
                return _history.ReadOlder(catalog, anchor, count, null, progress, ct);
            }

            var rows = new List<EventRow>();
            for (int i = Math.Max(0, index - count); i < index; i++)
            {
                rows.Add(state.Ring[i]);
            }

            if (rows.Count == count)
            {
                return new RowPage(rows, ReachedBoundary: false, Errors: []);
            }

            RowPage older = _history.ReadOlder(catalog, state.Ring[0].Key, count - rows.Count, null, progress, ct);
            return new RowPage(older.Rows.Concat(rows).ToList(), older.ReachedBoundary, older.Errors);
        }

        var errors = new List<MemberReadError>();
        List<EventRow> tail = MatchedTail(state, filter, anchor, after: false, errors, ct);
        RowPage history = _history.ReadOlder(catalog, anchor, count, filter, progress, ct);
        List<EventRow> page = Merge(history.Rows, tail, catalog);
        page = page.GetRange(Math.Max(0, page.Count - count), Math.Min(count, page.Count));
        return new RowPage(page, page.Count < count, history.Errors.Concat(errors).ToList());
    }

    /// <summary>Reads up to <paramref name="count"/> rows strictly newer than <paramref name="anchor"/>, oldest first.</summary>
    /// <param name="state">The state to read.</param>
    /// <param name="anchor">The row the page is newer than. Its generation must be in the state's timeline.</param>
    /// <param name="count">The most rows to return. It is at least 1.</param>
    /// <param name="filter">The predicate a row must match, or <c>null</c>.</param>
    /// <param name="progress">Receives the event bytes scanned in history. May be <c>null</c>.</param>
    /// <param name="ct">Checked at least once per batch and per event.</param>
    /// <exception cref="ArgumentException">The anchor's generation is not in the timeline.</exception>
    public RowPage Newer(StoreState state, RowKey anchor, int count, IRowFilter? filter, IProgress<long>? progress, CancellationToken ct)
    {
        RequireKnown(state, anchor);
        CatalogSnapshot catalog = state.Catalog;
        if (filter is null)
        {
            int index = Find(state, anchor);
            if (index >= 0)
            {
                int first = index + 1;
                int take = Math.Min(count, state.Ring.Count - first);
                var rows = new List<EventRow>(take);
                for (int i = first; i < first + take; i++)
                {
                    rows.Add(state.Ring[i]);
                }

                return new RowPage(rows, ReachedBoundary: take < count, Errors: []);
            }

            RowPage history = _history.ReadNewer(catalog, anchor, count, null, progress, ct);
            if (history.Rows.Count == count || !history.ReachedBoundary)
            {
                return history;
            }

            // History has no more rows: the rest of the page is the ring's rows after the last one history gave.
            RowKey after = history.Rows.Count > 0 ? history.Rows[history.Rows.Count - 1].Key : anchor;
            int position = Find(state, after);
            int start = position >= 0 ? position + 1 : ~position;
            var page = history.Rows.ToList();
            for (int i = start; i < state.Ring.Count && page.Count < count; i++)
            {
                page.Add(state.Ring[i]);
            }

            return new RowPage(page, page.Count < count, history.Errors);
        }

        var errors = new List<MemberReadError>();
        List<EventRow> tail = MatchedTail(state, filter, anchor, after: true, errors, ct);
        RowPage filtered = _history.ReadNewer(catalog, anchor, count, filter, progress, ct);
        List<EventRow> merged = Merge(filtered.Rows, tail, catalog);
        merged = merged.GetRange(0, Math.Min(count, merged.Count));
        return new RowPage(merged, merged.Count < count, filtered.Errors.Concat(errors).ToList());
    }

    /// <summary>
    /// Reads the detail of one event: opens its member's file with the identity verified, frames the one event at the key's offset,
    /// and reads and parses it. An identity or open failure is retried once with a fresh state, which may have recorded a rename.
    /// </summary>
    /// <param name="current">Takes the latest published state. Called again for the retry.</param>
    /// <param name="key">The event's key.</param>
    /// <param name="ct">Checked before each attempt.</param>
    public EventDetail Detail(Func<StoreState> current, RowKey key, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            StoreState state = current();
            MemberInfo? member = state.Catalog.Find(key.Generation);
            if (member is null || !member.IsReadable)
            {
                return new EventDetail.Unavailable(key, ExpiredReason);
            }

            if (MemberFile.TryOpen(member.Path, member.Identity, out FileStream? stream, out _, out string? error))
            {
                using FileStream file = stream!;
                return Load(member, key, file);
            }

            if (attempt == 2)
            {
                return new EventDetail.Unavailable(key, error ?? MemberFile.NotFound);
            }
        }
    }

    /// <summary>Frames the one event at the key's offset, and reads and parses it.</summary>
    private EventDetail Load(MemberInfo member, RowKey key, FileStream file)
    {
        if (file.Length <= key.Offset)
        {
            return new EventDetail.Unavailable(key, "the file is shorter than the event");
        }

        var framer = new EventFramer(file, member.Scheme!, live: member.IsLive);
        EventExtent? extent = framer.ReadForward(key.Offset).Cast<EventExtent?>().FirstOrDefault();
        if (extent is not { } found || found.Start != key.Offset)
        {
            return new EventDetail.Unavailable(key, "no event starts at its offset");
        }

        try
        {
            (string text, bool truncated) = EventText.Read(file, found, EventText.DefaultCap);
            (EventRow row, LogEvent ev) = RowBuilder.BuildFromText(text, member.Scheme, member.Generation, found, _context);
            return new EventDetail.Loaded(row, ev, text, truncated, member.Path, found.Start, found.End - found.Start);
        }
        catch (IOException ex)
        {
            return new EventDetail.Unavailable(key, ex.Message);
        }
    }

    /// <summary>
    /// The tail rows of the ring that match the filter, in timeline order. With an anchor, only the rows after it (<paramref name="after"/>)
    /// or before it. A tail row whose file cannot be read, or whose member has expired, is not matched and is recorded in
    /// <paramref name="errors"/>.
    /// </summary>
    private List<EventRow> MatchedTail(StoreState state, IRowFilter filter, RowKey? anchor, bool after, List<MemberReadError> errors, CancellationToken ct)
    {
        var matches = new List<EventRow>();
        foreach (EventRow row in state.Ring)
        {
            MemberInfo? member = state.Catalog.Find(row.Key.Generation);
            if (member is null || row.Key.Offset < member.ReadCursor)
            {
                continue;
            }

            if (anchor is RowKey key)
            {
                int order = state.Catalog.Compare(row.Key, key);
                if (after ? order <= 0 : order >= 0)
                {
                    continue;
                }
            }

            ct.ThrowIfCancellationRequested();
            if (!member.IsReadable)
            {
                errors.Add(new MemberReadError(member.Generation, member.Path, ExpiredReason));
                continue;
            }

            EventRow? matched = Match(member, row, filter, errors);
            if (matched is not null)
            {
                matches.Add(matched);
            }
        }

        return matches;
    }

    /// <summary>Re-reads one tail row from its file and tells whether the filter matches it.</summary>
    /// <returns>The row as built from the file, when it matches; otherwise <c>null</c>.</returns>
    private EventRow? Match(MemberInfo member, EventRow row, IRowFilter filter, List<MemberReadError> errors)
    {
        if (!MemberFile.TryOpen(member.Path, member.Identity, out FileStream? stream, out _, out string? error))
        {
            errors.Add(new MemberReadError(member.Generation, member.Path, error ?? MemberFile.NotFound));
            return null;
        }

        using FileStream file = stream!;
        if (file.Length < row.Extent.End)
        {
            errors.Add(new MemberReadError(member.Generation, member.Path, "the file is shorter than the event"));
            return null;
        }

        try
        {
            (EventRow built, LogEvent ev) = RowBuilder.Build(file, member.Scheme, member.Generation, row.Extent, _context);
            return filter.Matches(built, ev) ? built : null;
        }
        catch (IOException ex)
        {
            errors.Add(new MemberReadError(member.Generation, member.Path, ex.Message));
            return null;
        }
    }

    /// <summary>Both lists in timeline order, as one list.</summary>
    private static List<EventRow> Merge(IReadOnlyList<EventRow> history, List<EventRow> tail, CatalogSnapshot catalog)
    {
        var merged = new List<EventRow>(history.Count + tail.Count);
        merged.AddRange(history);
        merged.AddRange(tail);
        merged.Sort((a, b) => catalog.Compare(a.Key, b.Key));
        return merged;
    }

    /// <summary>The position of a key in the ring: its index when present, otherwise the bitwise complement of its insertion point.</summary>
    private static int Find(StoreState state, RowKey key)
    {
        IReadOnlyList<EventRow> ring = state.Ring;
        int low = 0;
        int high = ring.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int order = state.Catalog.Compare(ring[middle].Key, key);
            if (order == 0)
            {
                return middle;
            }

            if (order < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return ~low;
    }

    private static void RequireKnown(StoreState state, RowKey anchor)
    {
        if (state.Catalog.Find(anchor.Generation) is null)
        {
            throw new ArgumentException($"Generation {anchor.Generation} has no place in the timeline.", nameof(anchor));
        }
    }
}
