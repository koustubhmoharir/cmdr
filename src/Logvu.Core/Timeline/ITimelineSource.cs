using Logvu.Core.Store;
using Shared.Core;

namespace Logvu.Core.Timeline;

/// <summary>The rows a <see cref="TimelineModel"/> loads. Each read returns matching rows oldest first, the same contract as <see cref="LogStore"/>.</summary>
public interface ITimelineSource
{
    /// <summary>Reads up to <paramref name="count"/> of the newest matching rows.</summary>
    /// <param name="count">The most rows to return. It is at least 1.</param>
    /// <param name="filter">The predicate a row must match, or <c>null</c>.</param>
    /// <param name="progress">Receives the event bytes scanned in history. May be <c>null</c>.</param>
    /// <param name="ct">Cancels the read.</param>
    Task<RowPage> ReadNewestAsync(int count, IRowFilter? filter, IProgress<long>? progress, CancellationToken ct);

    /// <summary>Reads up to <paramref name="count"/> matching rows strictly older than <paramref name="anchor"/>.</summary>
    /// <param name="anchor">The row the page is older than.</param>
    /// <param name="count">The most rows to return. It is at least 1.</param>
    /// <param name="filter">The predicate a row must match, or <c>null</c>.</param>
    /// <param name="progress">Receives the event bytes scanned in history. May be <c>null</c>.</param>
    /// <param name="ct">Cancels the read.</param>
    Task<RowPage> ReadOlderAsync(RowKey anchor, int count, IRowFilter? filter, IProgress<long>? progress, CancellationToken ct);

    /// <summary>
    /// Reads up to <paramref name="count"/> matching rows strictly newer than <paramref name="anchor"/>, or, when <paramref name="inclusive"/>
    /// is set, starting with the anchor's own row if it exists and matches.
    /// </summary>
    /// <param name="anchor">The row the page is newer than, or starts at when inclusive.</param>
    /// <param name="count">The most rows to return. It is at least 1.</param>
    /// <param name="filter">The predicate a row must match, or <c>null</c>.</param>
    /// <param name="progress">Receives the event bytes scanned in history. May be <c>null</c>.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="inclusive">Whether the anchor's own row may be the page's first row.</param>
    Task<RowPage> ReadNewerAsync(RowKey anchor, int count, IRowFilter? filter, IProgress<long>? progress, CancellationToken ct, bool inclusive = false);

    /// <summary>
    /// Finds the first event strictly older, or strictly newer, than <paramref name="from"/> that matches the pattern and passes the filter.
    /// </summary>
    /// <param name="from">The row the search starts from. It is not a candidate.</param>
    /// <param name="direction">Whether the search goes toward older or newer events.</param>
    /// <param name="pattern">The pattern an event must match.</param>
    /// <param name="filter">The predicate an event must also pass, or <c>null</c>.</param>
    /// <param name="progress">Receives the event bytes scanned. May be <c>null</c>.</param>
    /// <param name="ct">Cancels the search.</param>
    Task<SearchResult> FindAsync(RowKey from, SearchDirection direction, SearchPattern pattern, IRowFilter? filter, IProgress<long>? progress, CancellationToken ct);
}
