using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Timeline;

namespace Logvu.Core.Tests.Timeline;

/// <summary>
/// An in-memory timeline source over rows in key order. A read computes its page when it is requested; a read of a held kind waits
/// until the test completes it. Cancellation is not honoured, so a superseded read can still complete, as a racing store read can.
/// </summary>
internal sealed class FakeTimelineSource : ITimelineSource
{
    private readonly List<PendingRead> _held = [];

    public FakeTimelineSource(IEnumerable<EventRow> rows) => Rows = rows.ToList();

    /// <summary>The rows, in key order. A test appends to it to simulate arrivals.</summary>
    public List<EventRow> Rows { get; }

    /// <summary>The kinds of read that wait until the test completes them.</summary>
    public HashSet<ReadKind> Held { get; } = [];

    /// <summary>The number of held reads still waiting.</summary>
    public int Waiting(ReadKind kind) => _held.Count(read => read.Kind == kind);

    /// <summary>Compares keys by offset, which orders the test rows.</summary>
    public static int CompareKeys(RowKey a, RowKey b) => a.Offset.CompareTo(b.Offset);

    /// <summary>Completes the oldest held read of a kind with the page it computed.</summary>
    public void CompleteOldest(ReadKind kind)
    {
        PendingRead read = _held.First(candidate => candidate.Kind == kind);
        _held.Remove(read);
        read.Completion.SetResult(read.Page);
    }

    public Task<RowPage> ReadNewestAsync(int count, IRowFilter? filter, CancellationToken ct) =>
        Read(ReadKind.Newest, default, count, filter);

    public Task<RowPage> ReadOlderAsync(RowKey anchor, int count, IRowFilter? filter, CancellationToken ct) =>
        Read(ReadKind.Older, anchor, count, filter);

    public Task<RowPage> ReadNewerAsync(RowKey anchor, int count, IRowFilter? filter, CancellationToken ct, bool inclusive = false) =>
        Read(ReadKind.Newer, anchor, count, filter, inclusive);

    private Task<RowPage> Read(ReadKind kind, RowKey anchor, int count, IRowFilter? filter, bool inclusive = false)
    {
        RowPage page = Page(kind, anchor, count, filter, inclusive);
        if (!Held.Contains(kind))
        {
            return Task.FromResult(page);
        }

        var completion = new TaskCompletionSource<RowPage>();
        _held.Add(new PendingRead(kind, page, completion));
        return completion.Task;
    }

    /// <summary>Computes the page a read returns, as the store does: the matching rows nearest the anchor, oldest first.</summary>
    private RowPage Page(ReadKind kind, RowKey anchor, int count, IRowFilter? filter, bool inclusive)
    {
        List<EventRow> matching = Rows.Where(row => filter is null || filter.Matches(row, Event(row))).ToList();
        if (kind == ReadKind.Newest)
        {
            return Backward(matching, count);
        }

        if (kind == ReadKind.Older)
        {
            return Backward(matching.Where(row => CompareKeys(row.Key, anchor) < 0).ToList(), count);
        }

        List<EventRow> newer = matching.Where(row => CompareKeys(row.Key, anchor) > 0 || (inclusive && CompareKeys(row.Key, anchor) == 0)).ToList();
        return new RowPage(newer.Take(count).ToList(), newer.Count < count, []);
    }

    private static RowPage Backward(List<EventRow> candidates, int count) =>
        new(candidates.TakeLast(count).ToList(), candidates.Count < count, []);

    /// <summary>The parsed event a filter sees. The test filters read only the row.</summary>
    private static LogEvent Event(EventRow row) => new() { Message = row.Preview };

    private sealed record PendingRead(ReadKind Kind, RowPage Page, TaskCompletionSource<RowPage> Completion);
}

/// <summary>The kinds of read a fake source performs.</summary>
internal enum ReadKind
{
    Newest,
    Older,
    Newer,
}

/// <summary>A filter that keeps the rows at even indices.</summary>
internal sealed class EvenRowFilter : IRowFilter
{
    public bool Matches(EventRow row, LogEvent ev) => (row.Key.Offset / 100) % 2 == 0;
}

/// <summary>A filter that keeps the rows at odd indices, so that it rejects the even rows.</summary>
internal sealed class OddRowFilter : IRowFilter
{
    public bool Matches(EventRow row, LogEvent ev) => (row.Key.Offset / 100) % 2 == 1;
}
