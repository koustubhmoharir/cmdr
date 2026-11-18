using Logvu.Core.Parsing;
using Logvu.Core.Reading;

namespace Logvu.Core.Store;

/// <summary>
/// One publication of the store: what the run changed, the timeline it left, and the status. Raised by <see cref="LogStore.Updated"/>.
/// </summary>
/// <remarks>
/// <para>
/// A subscriber keeps its own window of rows, built from the batches. It must reload that window from the store when <see cref="Resync"/>
/// or <see cref="ChangeBatch.RingReset"/> is set: the batch alone does not describe the change then. A resync is raised after a run that
/// threw, because that run may have advanced the store without its rows reaching subscribers, and again on the first run that succeeds
/// after it. Its batch may then be empty.
/// </para>
/// <para>
/// A publication with an empty <see cref="ChangeBatch"/> carries a status change only: a failure, or a change of the directory error.
/// The batch of the initial run has <see cref="ChangeBatch.Initial"/> set, and its rows are the live provisional row only; the history
/// is read through <see cref="LogStore.ReadNewestAsync"/>.
/// </para>
/// </remarks>
/// <param name="Batch">What the run changed. The rows are in the order the run published them.</param>
/// <param name="Snapshot">The timeline after the run. It is the same snapshot <see cref="LogStore.Snapshot"/> returns from then on, until the next publication.</param>
/// <param name="Status">The store's status after the run.</param>
/// <param name="Resync">Whether the subscriber must reload its window from the store rather than apply the batch.</param>
public sealed record StoreUpdate(ChangeBatch Batch, CatalogSnapshot Snapshot, StoreStatus Status, bool Resync);

/// <summary>
/// The state of the store that a status line shows. Each list is a bounded, most-recent-last copy.
/// </summary>
/// <param name="MemberCount">The members of the set that have not expired.</param>
/// <param name="LiveMemberName">The basename of the live member, or <c>null</c> when there is none.</param>
/// <param name="DirectoryError">Why the logs directory could not be enumerated at the last rescan, or <c>null</c>.</param>
/// <param name="RecentErrors">
/// The most recent member read errors, at most 20. An error that repeats is kept once, at its latest position.
/// </param>
/// <param name="Gaps">The most recent retention gaps, at most 100: members that expired with bytes that were never read.</param>
/// <param name="RecentFailures">
/// The most recent unexpected failures of the store, at most 10: a run that threw, or an <see cref="LogStore.Updated"/> handler that threw.
/// A failure that repeats the latest one is not added again.
/// </param>
public sealed record StoreStatus(
    int MemberCount,
    string? LiveMemberName,
    string? DirectoryError,
    IReadOnlyList<MemberReadError> RecentErrors,
    IReadOnlyList<RetentionGap> Gaps,
    IReadOnlyList<string> RecentFailures);

/// <summary>
/// The result of reading one event's detail: its full text and parsed event, or why they cannot be read. A closed hierarchy: the two
/// cases are the nested records.
/// </summary>
public abstract record EventDetail
{
    private EventDetail()
    {
    }

    /// <summary>The event was read from its file, and its identity was verified against its member.</summary>
    /// <param name="Row">The event's row, built from the text.</param>
    /// <param name="Event">The parsed event, with its full message and fields.</param>
    /// <param name="Text">The event's text: its lines joined by <c>\n</c>, without the final line terminator. It is cut at <see cref="EventText.DefaultCap"/> bytes.</param>
    /// <param name="Truncated">Whether <paramref name="Text"/> was cut.</param>
    /// <param name="Path">The path the event was read from, which is the member's path in the snapshot the read used.</param>
    /// <param name="Offset">The byte offset of the event in <paramref name="Path"/>.</param>
    /// <param name="Length">The event's length in bytes.</param>
    public sealed record Loaded(
        EventRow Row,
        LogEvent Event,
        string Text,
        bool Truncated,
        string Path,
        long Offset,
        long Length) : EventDetail;

    /// <summary>The event cannot be read.</summary>
    /// <param name="Key">The key of the event.</param>
    /// <param name="Reason">
    /// <c>Source file expired</c> when the event's generation is no longer on disk (or is not in the timeline); otherwise the open or identity
    /// error of the last attempt.
    /// </param>
    public sealed record Unavailable(RowKey Key, string Reason) : EventDetail;
}
