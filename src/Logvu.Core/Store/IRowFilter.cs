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

    /// <summary>
    /// Whether <see cref="MayMatchHead"/> can reject events, so that the reader builds the header row before it reads the rest of an
    /// event. A filter with no predicate on the header line returns <c>false</c>, the default.
    /// </summary>
    bool ChecksHead => false;

    /// <summary>
    /// Whether <see cref="MayMatchText"/> can reject events, so that the reader checks the event's text before it parses the event. A filter
    /// whose text predicate follows a predicate that needs the parsed event returns <c>false</c>, the default.
    /// </summary>
    bool ChecksText => false;

    /// <summary>
    /// Whether <see cref="MayMatchHead"/> reads the header's time. A filter with no time predicate returns <c>false</c>, and the reader then
    /// builds the header row without its time, which saves parsing it. The row's other fields are always built.
    /// </summary>
    bool ChecksHeadTime => false;

    /// <summary>
    /// A cheap test on an event's header line, which the reader makes before it parses the event. It is called only when
    /// <see cref="ChecksHead"/> is set, and only for events of a text scheme.
    /// </summary>
    /// <remarks>
    /// The row has the fields that the header line alone gives: its key, extent, level and its time unless
    /// <see cref="ChecksHeadTime"/> is <c>false</c>. Its preview is empty, since the message is not known yet. The result is <c>false</c> only
    /// when <see cref="Matches"/> would be <c>false</c> for the event, and <see cref="Matches"/> would decide it on those fields alone.
    /// </remarks>
    /// <param name="head">The event's row, as built from its header line.</param>
    bool MayMatchHead(EventRow head) => true;

    /// <summary>
    /// A cheap test on an event's text, which the reader makes before it parses the event. It is called only when
    /// <see cref="ChecksText"/> is set, and only after the header test has passed.
    /// </summary>
    /// <remarks>
    /// The result is <c>false</c> only when <see cref="Matches"/> would be <c>false</c> for the event, and that is decided by the text alone.
    /// A text event matches a pattern by its text. A JSON event may also match by its parsed message, which the text does not always show,
    /// so a JSON event is kept for the full test unless its message is known to be in the text (see <see cref="EventSearch.MayMatch"/>).
    /// </remarks>
    /// <param name="text">The event's full text, as <see cref="RowBuilder"/> reads it.</param>
    /// <param name="scheme">The scheme of the event's member.</param>
    bool MayMatchText(string text, ILogScheme scheme) => true;
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
