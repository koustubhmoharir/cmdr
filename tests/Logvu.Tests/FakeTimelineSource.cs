using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Timeline;
using Shared.Core;

namespace Logvu.Tests;

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

    /// <summary>Whether the searches wait until the test completes them. A search that is not held finds its result at once.</summary>
    public bool HoldFinds { get; set; }

    /// <summary>Every search asked for, in order, held or not.</summary>
    public List<FindRequest> Finds { get; } = [];

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

    /// <summary>Completes the oldest held read of a kind with its page, marked as abandoned: the filter's pattern timed out during it.</summary>
    public void CompleteOldestAbandoned(ReadKind kind)
    {
        PendingRead read = _held.First(candidate => candidate.Kind == kind);
        _held.Remove(read);
        read.Completion.SetResult(read.Page with { Abandoned = true });
    }

    public Task<RowPage> ReadNewestAsync(int count, IRowFilter? filter, IProgress<long>? progress, CancellationToken ct) =>
        Read(ReadKind.Newest, default, count, filter);

    public Task<RowPage> ReadOlderAsync(RowKey anchor, int count, IRowFilter? filter, IProgress<long>? progress, CancellationToken ct) =>
        Read(ReadKind.Older, anchor, count, filter);

    public Task<RowPage> ReadNewerAsync(RowKey anchor, int count, IRowFilter? filter, IProgress<long>? progress, CancellationToken ct, bool inclusive = false) =>
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
        List<EventRow> matching = Rows.Where(row => filter is null || filter.Matches(row, Event(row), row.Preview)).ToList();
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

    /// <summary>
    /// Finds the nearest row in the direction whose preview matches the pattern and that passes the filter. The search is recorded in
    /// <see cref="Finds"/>; while <see cref="HoldFinds"/> is set, the test completes it.
    /// </summary>
    public Task<SearchResult> FindAsync(RowKey from, SearchDirection direction, SearchPattern pattern, IRowFilter? filter, IProgress<long>? progress, CancellationToken ct)
    {
        var request = new FindRequest(from, direction, pattern, filter, progress, ct, new TaskCompletionSource<SearchResult>());
        Finds.Add(request);
        if (!HoldFinds)
        {
            request.Complete(Search(from, direction, pattern, filter));
        }

        return request.Completion.Task;
    }

    /// <summary>Finds the nearest matching row in the direction, or the boundary when there is none.</summary>
    private SearchResult Search(RowKey from, SearchDirection direction, SearchPattern pattern, IRowFilter? filter)
    {
        IEnumerable<EventRow> candidates = direction == SearchDirection.Older
            ? Rows.Where(row => CompareKeys(row.Key, from) < 0).Reverse()
            : Rows.Where(row => CompareKeys(row.Key, from) > 0);
        EventRow? found = candidates.FirstOrDefault(row =>
            pattern.Count(row.Preview) > 0 && (filter is null || filter.Matches(row, Event(row), row.Preview)));
        return found is null ? SearchResult.Boundary : SearchResult.Found(found);
    }

    private static RowPage Backward(List<EventRow> candidates, int count) =>
        new(candidates.TakeLast(count).ToList(), candidates.Count < count, []);

    /// <summary>The parsed event a filter sees. The test filters read only the row.</summary>
    private static LogEvent Event(EventRow row) => new() { Message = row.Preview };

    private sealed record PendingRead(ReadKind Kind, RowPage Page, TaskCompletionSource<RowPage> Completion);

    /// <summary>A search asked for: its arguments, the progress it reports to, its cancellation and the completion that finishes it.</summary>
    /// <param name="From">The row the search starts from.</param>
    /// <param name="Direction">The direction of the search.</param>
    /// <param name="Pattern">The pattern searched for.</param>
    /// <param name="Filter">The filter, or <c>null</c>.</param>
    /// <param name="Progress">The progress the search reports to, or <c>null</c>.</param>
    /// <param name="Token">The search's cancellation.</param>
    /// <param name="Completion">Finishes the search.</param>
    public sealed record FindRequest(
        RowKey From,
        SearchDirection Direction,
        SearchPattern Pattern,
        IRowFilter? Filter,
        IProgress<long>? Progress,
        CancellationToken Token,
        TaskCompletionSource<SearchResult> Completion)
    {
        /// <summary>Finishes the search with a result. A search that has finished already is unchanged.</summary>
        public void Complete(SearchResult result) => Completion.TrySetResult(result);
    }
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
    public bool Matches(EventRow row, LogEvent ev, string text) => (row.Key.Offset / 100) % 2 == 0;
}

/// <summary>A filter that keeps the rows at odd indices, so that it rejects the even rows.</summary>
internal sealed class OddRowFilter : IRowFilter
{
    public bool Matches(EventRow row, LogEvent ev, string text) => (row.Key.Offset / 100) % 2 == 1;
}
