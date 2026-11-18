using Logvu.Core.Config;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;

namespace Logvu.Core.Store;

/// <summary>
/// Reads pages of rows from history: the events of the timeline's members, each bounded by its read cursor. It reads only
/// <see cref="MemberInfo"/> copies from a <see cref="CatalogSnapshot"/>, never the mutable catalog, so it may run on any thread. A reader
/// holds no state between calls and may be shared; the scan state lives in each call.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bounds.</b> A member contributes the events that start before its <see cref="Member.ReadCursor"/>. Bytes at or beyond the
/// cursor belong to the live tail and are never returned. The cursor is an event boundary, so the bytes before it are framed as a
/// closed file through a <see cref="ReadWindow"/>.
/// </para>
/// <para>
/// <b>Walk.</b> A backward read starts at the anchor's member and follows <see cref="CatalogSnapshot.Previous"/>; a forward read follows
/// <see cref="CatalogSnapshot.Next"/>. Members that are not readable (expired) are passed over silently. Rows are produced in physical order
/// within a member and in timeline order across members, so consecutive pages join without skips or repeats.
/// </para>
/// <para>
/// <b>Handles.</b> A member is opened for one batch of at most <see cref="BatchEvents"/> events or <see cref="BatchBytes"/> bytes of event
/// extents, then closed; the next batch reopens it and verifies its identity against the snapshot's. No handle outlives a call. A member
/// that cannot be opened, whose identity has changed, or that fails with an I/O error is recorded in <see cref="RowPage.Errors"/>, and the
/// walk moves on to the next member. A snapshot's path may be stale: a file renamed since the snapshot is not found at that path, and
/// the read reports it so, rather than reading whatever now lives there.
/// </para>
/// </remarks>
public sealed class HistoryReader
{
    private const int DefaultBatchEvents = 256;
    private const long DefaultBatchBytes = 4L * 1024 * 1024;

    private readonly ParseContext _context;
    private int _batchEvents = DefaultBatchEvents;
    private long _batchBytes = DefaultBatchBytes;

    /// <summary>Creates a reader.</summary>
    /// <param name="config">The configuration. Its time zone interprets timestamps without an offset.</param>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is <c>null</c>.</exception>
    public HistoryReader(LogvuConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _context = new ParseContext(config.TimeZone);
    }

    /// <summary>The most events read from one open handle before it is closed and reopened. Tests lower it.</summary>
    internal int BatchEvents
    {
        get => _batchEvents;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _batchEvents = value;
        }
    }

    /// <summary>The most bytes of event extents read from one open handle before it is closed and reopened. Tests lower it.</summary>
    internal long BatchBytes
    {
        get => _batchBytes;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _batchBytes = value;
        }
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> matching rows strictly older than <paramref name="anchor"/>.
    /// </summary>
    /// <remarks>
    /// The walk starts at the anchor's offset within its member and continues into earlier members. An anchor whose member has expired
    /// is read from the member before it.
    /// </remarks>
    /// <param name="snapshot">The timeline the read walks. It is taken by the caller and not changed by the read.</param>
    /// <param name="anchor">The row the page is older than: a key of an event start.</param>
    /// <param name="count">The most rows to return. It is at least 1.</param>
    /// <param name="filter">The predicate a row must match, or <c>null</c> to match every row.</param>
    /// <param name="scannedBytes">Receives the running total of event bytes scanned, at least once per batch. May be <c>null</c>.</param>
    /// <param name="ct">Checked at least once per batch and per event.</param>
    /// <returns>The matching rows, oldest first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is below 1, or the anchor's offset is negative.</exception>
    /// <exception cref="ArgumentException">The anchor's generation is not in the snapshot's timeline.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public RowPage ReadOlder(CatalogSnapshot snapshot, RowKey anchor, int count, IRowFilter? filter, IProgress<long>? scannedBytes, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ct.ThrowIfCancellationRequested();
        MemberInfo member = AnchorMember(snapshot, anchor);

        var scan = new Scan(this, snapshot, count, filter, scannedBytes, ct);
        bool reachedBoundary = scan.Backward(member, anchor.Offset);
        return scan.Finish(reachedBoundary, backward: true);
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> matching rows strictly newer than <paramref name="anchor"/>, or, when
    /// <paramref name="inclusive"/> is set, the anchor's own event followed by the rows newer than it.
    /// </summary>
    /// <remarks>
    /// The walk starts at the anchor within its member and continues into later members from their first event. An anchor whose member
    /// has expired is read from the member after it, so an inclusive read has no row for it there.
    /// </remarks>
    /// <param name="snapshot">The timeline the read walks. It is taken by the caller and not changed by the read.</param>
    /// <param name="anchor">The row the page is newer than: a key of an event start.</param>
    /// <param name="count">The most rows to return. It is at least 1.</param>
    /// <param name="filter">The predicate a row must match, or <c>null</c> to match every row.</param>
    /// <param name="scannedBytes">Receives the running total of event bytes scanned, at least once per batch. May be <c>null</c>.</param>
    /// <param name="ct">Checked at least once per batch and per event.</param>
    /// <param name="inclusive">Whether the anchor's own event may be the page's first row, when it exists and matches the filter.</param>
    /// <returns>The matching rows, oldest first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is below 1, or the anchor's offset is negative.</exception>
    /// <exception cref="ArgumentException">The anchor's generation is not in the snapshot's timeline.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public RowPage ReadNewer(CatalogSnapshot snapshot, RowKey anchor, int count, IRowFilter? filter, IProgress<long>? scannedBytes, CancellationToken ct, bool inclusive = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ct.ThrowIfCancellationRequested();
        MemberInfo member = AnchorMember(snapshot, anchor);

        var scan = new Scan(this, snapshot, count, filter, scannedBytes, ct);
        // The walk starts at the anchor's offset, so an inclusive read needs no skip; an exclusive one skips the anchor's event.
        bool reachedBoundary = scan.Forward(member, anchor.Offset, skipUpTo: inclusive ? null : anchor.Offset);
        return scan.Finish(reachedBoundary, backward: false);
    }

    /// <summary>
    /// Reads the newest <paramref name="count"/> matching rows of history: the walk starts at the last readable member, at its read cursor.
    /// </summary>
    /// <param name="snapshot">The timeline the read walks. It is taken by the caller and not changed by the read.</param>
    /// <param name="count">The most rows to return. It is at least 1.</param>
    /// <param name="filter">The predicate a row must match, or <c>null</c> to match every row.</param>
    /// <param name="scannedBytes">Receives the running total of event bytes scanned, at least once per batch. May be <c>null</c>.</param>
    /// <param name="ct">Checked at least once per batch and per event.</param>
    /// <returns>The matching rows, oldest first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is below 1.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public RowPage ReadNewest(CatalogSnapshot snapshot, int count, IRowFilter? filter, IProgress<long>? scannedBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ct.ThrowIfCancellationRequested();
        MemberInfo? newest = snapshot.Ordered.LastOrDefault(member => member.IsReadable);

        var scan = new Scan(this, snapshot, count, filter, scannedBytes, ct);
        bool reachedBoundary = scan.Backward(newest, long.MaxValue);
        return scan.Finish(reachedBoundary, backward: true);
    }

    /// <summary>The member of an anchor, which must be in the snapshot's timeline (even when it has expired).</summary>
    private static MemberInfo AnchorMember(CatalogSnapshot snapshot, RowKey anchor)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(anchor.Offset, nameof(anchor));
        return snapshot.Find(anchor.Generation)
            ?? throw new ArgumentException($"Generation {anchor.Generation} has no place in the timeline.", nameof(anchor));
    }

    /// <summary>The state of one page read: the rows collected so far, and the batch limits in force.</summary>
    private sealed class Scan
    {
        private readonly HistoryReader _reader;
        private readonly CatalogSnapshot _snapshot;
        private readonly int _count;
        private readonly IRowFilter? _filter;
        private readonly IProgress<long>? _progress;
        private readonly CancellationToken _ct;
        private readonly List<EventRow> _rows = [];
        private readonly List<MemberReadError> _errors = [];
        private long _scanned;

        public Scan(HistoryReader reader, CatalogSnapshot snapshot, int count, IRowFilter? filter, IProgress<long>? progress, CancellationToken ct)
        {
            _reader = reader;
            _snapshot = snapshot;
            _count = count;
            _filter = filter;
            _progress = progress;
            _ct = ct;
        }

        /// <summary>
        /// Walks backward from <paramref name="start"/>, whose events before <paramref name="before"/> are read; then through earlier members
        /// from their read cursors.
        /// </summary>
        /// <returns><c>true</c> when the start of history was reached before the page was filled.</returns>
        public bool Backward(MemberInfo? start, long before)
        {
            long from = before;
            for (MemberInfo? member = start; member is not null; member = _snapshot.Previous(member))
            {
                if (member.IsReadable && ScanBackward(member, from) == Step.Filled)
                {
                    return false;
                }

                from = long.MaxValue;
            }

            return true;
        }

        /// <summary>
        /// Walks forward from <paramref name="start"/>, from offset <paramref name="from"/>, skipping the event that starts at or before
        /// <paramref name="skipUpTo"/> when it is set; then through later members from their first event.
        /// </summary>
        /// <returns><c>true</c> when the end of history was reached before the page was filled.</returns>
        public bool Forward(MemberInfo? start, long from, long? skipUpTo)
        {
            for (MemberInfo? member = start; member is not null; member = _snapshot.Next(member))
            {
                if (member.IsReadable && ScanForward(member, from, skipUpTo) == Step.Filled)
                {
                    return false;
                }

                from = 0;
                skipUpTo = null;
            }

            return true;
        }

        /// <summary>Builds the page. Rows collected by a backward walk are reversed into oldest-first order.</summary>
        public RowPage Finish(bool reachedBoundary, bool backward)
        {
            if (backward)
            {
                _rows.Reverse();
            }

            return new RowPage(_rows, reachedBoundary, _errors);
        }

        /// <summary>Reads one member backward from <paramref name="before"/>, batch by batch, until it is exhausted or the page is filled.</summary>
        private Step ScanBackward(MemberInfo member, long before)
        {
            long cursor = member.ReadCursor;
            before = Math.Min(before, cursor);
            while (true)
            {
                _ct.ThrowIfCancellationRequested();
                if (before <= 0)
                {
                    return Step.Exhausted;
                }

                using FileStream? file = Open(member);
                if (file is null)
                {
                    return Step.Exhausted;
                }

                try
                {
                    var window = new ReadWindow(file, cursor);
                    if (before <= LineReader.ContentStart(window))
                    {
                        return Step.Exhausted;
                    }

                    var framer = new EventFramer(window, member.Scheme, live: false);
                    Step step = Step.Exhausted;
                    int events = 0;
                    long bytes = 0;
                    foreach (EventExtent extent in framer.ReadBackward(before))
                    {
                        _ct.ThrowIfCancellationRequested();
                        long length = extent.End - extent.Start;
                        events++;
                        bytes += length;
                        _scanned += length;
                        before = extent.Start;

                        if (Accept(member, window, extent))
                        {
                            step = Step.Filled;
                            break;
                        }

                        if (events >= _reader._batchEvents || bytes >= _reader._batchBytes)
                        {
                            step = Step.NextBatch;
                            break;
                        }
                    }

                    _progress?.Report(_scanned);
                    if (step != Step.NextBatch)
                    {
                        return step;
                    }
                }
                catch (IOException ex)
                {
                    _errors.Add(new MemberReadError(member.Generation, member.Path, ex.Message));
                    return Step.Exhausted;
                }
            }
        }

        /// <summary>
        /// Reads one member forward from <paramref name="from"/>, batch by batch, until it is exhausted or the page is filled.
        /// </summary>
        private Step ScanForward(MemberInfo member, long from, long? skipUpTo)
        {
            long cursor = member.ReadCursor;
            while (true)
            {
                _ct.ThrowIfCancellationRequested();
                if (from >= cursor)
                {
                    return Step.Exhausted;
                }

                using FileStream? file = Open(member);
                if (file is null)
                {
                    return Step.Exhausted;
                }

                try
                {
                    var window = new ReadWindow(file, cursor);
                    var framer = new EventFramer(window, member.Scheme, live: false);
                    Step step = Step.Exhausted;
                    int events = 0;
                    long bytes = 0;
                    foreach (EventExtent extent in framer.ReadForward(from))
                    {
                        _ct.ThrowIfCancellationRequested();
                        long length = extent.End - extent.Start;
                        events++;
                        bytes += length;
                        _scanned += length;
                        from = extent.End;

                        bool skipped = skipUpTo is { } skip && extent.Start <= skip;
                        if (!skipped && Accept(member, window, extent))
                        {
                            step = Step.Filled;
                            break;
                        }

                        if (events >= _reader._batchEvents || bytes >= _reader._batchBytes)
                        {
                            step = Step.NextBatch;
                            break;
                        }
                    }

                    _progress?.Report(_scanned);
                    if (step != Step.NextBatch)
                    {
                        return step;
                    }
                }
                catch (IOException ex)
                {
                    _errors.Add(new MemberReadError(member.Generation, member.Path, ex.Message));
                    return Step.Exhausted;
                }
            }
        }

        /// <summary>
        /// Opens a member's file for one batch, verifying its identity. A file that cannot be opened, or is shorter than the member's read
        /// cursor (so that it is not the file the cursor was taken from), is recorded as an error.
        /// </summary>
        /// <returns>The open stream, which the caller disposes, or <c>null</c> after an error was recorded.</returns>
        private FileStream? Open(MemberInfo member)
        {
            if (!MemberFile.TryOpen(member.Path, member.Identity, out FileStream? stream, out _, out string? error))
            {
                _errors.Add(new MemberReadError(member.Generation, member.Path, error ?? MemberFile.NotFound));
                return null;
            }

            if (stream!.Length < member.ReadCursor)
            {
                stream.Dispose();
                _errors.Add(new MemberReadError(member.Generation, member.Path, "the file is shorter than its read cursor"));
                return null;
            }

            return stream;
        }

        /// <summary>Builds the row of an event, keeps it if it matches, and tells whether the page is now full.</summary>
        private bool Accept(MemberInfo member, Stream window, EventExtent extent)
        {
            (EventRow row, LogEvent ev) = RowBuilder.Build(window, member.Scheme, member.Generation, extent, _reader._context);
            if (_filter is null || _filter.Matches(row, ev))
            {
                _rows.Add(row);
            }

            return _rows.Count >= _count;
        }

        /// <summary>How a batch or a member ended.</summary>
        private enum Step
        {
            /// <summary>The member has no more events in this direction.</summary>
            Exhausted,

            /// <summary>The page is full.</summary>
            Filled,

            /// <summary>The batch reached its limit; the member has more events.</summary>
            NextBatch,
        }
    }
}
