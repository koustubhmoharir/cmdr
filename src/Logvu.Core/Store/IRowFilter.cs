using Logvu.Core.Parsing;

namespace Logvu.Core.Store;

/// <summary>A predicate over events, applied by <see cref="HistoryReader"/> to every event it reads. A page holds only the rows that match.</summary>
public interface IRowFilter
{
    /// <summary>The time interval an event must lie in to match, for skipping members of history whose time range lies wholly outside it.</summary>
    /// <remarks>
    /// A bound that is <c>null</c> is open. A member is skipped only when its time range ends before <c>From</c> or starts after <c>To</c>,
    /// so a member that touches a bound is read. A filter without a time window returns <c>(null, null)</c>, the default.
    /// </remarks>
    (DateTimeOffset? From, DateTimeOffset? To) TimeWindow => (null, null);

    /// <summary>Tells whether an event matches.</summary>
    /// <param name="row">The event's row.</param>
    /// <param name="ev">The parsed event, with its full message.</param>
    /// <param name="text">The event's full text, as <see cref="RowBuilder"/> read it: capped at <see cref="RowBuilder.TextCap"/> bytes.</param>
    bool Matches(EventRow row, LogEvent ev, string text);

    /// <summary>
    /// Whether the filter has given up on the rest of history, such as a text pattern that timed out. A read checks it after each
    /// <see cref="Matches"/> call and stops the walk when it is set, so a filter that can no longer match is not run over every file.
    /// </summary>
    bool Abandoned => false;
}

/// <summary>
/// A filter that needs the timeline to evaluate, such as one that resolves a member's file name from its generation. The readers bind
/// every filter they are given to the snapshot they read with, so the filter sees the same members the read walks.
/// </summary>
public interface IBindableRowFilter : IRowFilter
{
    /// <summary>Returns the filter bound to a snapshot. The bound filter matches the same events, with the snapshot's members for lookups.</summary>
    /// <param name="snapshot">The timeline the read walks.</param>
    /// <returns>The bound filter. It may be this filter when nothing needs binding.</returns>
    IRowFilter Bind(CatalogSnapshot snapshot);
}
