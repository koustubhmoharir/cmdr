namespace Logvu.Core.Store;

/// <summary>A page of rows read from history.</summary>
/// <param name="Rows">The matching rows, oldest first. For a backward read they are the rows just older than the anchor.</param>
/// <param name="ReachedBoundary">
/// Whether the walk ran out of history in its direction before it found <c>count</c> matching rows: the start of history for an older
/// read, the end for a newer one. It is <c>false</c> when the page was filled, even if the page happens to end at the boundary.
/// </param>
/// <param name="Errors">The members that could not be read. The walk went on past them.</param>
/// <param name="Abandoned">
/// Whether the walk stopped early because the filter was abandoned (see <see cref="IRowFilter.Abandoned"/>), such as after its pattern timed
/// out. The rows are what was found before the walk stopped. <paramref name="ReachedBoundary"/> is then <c>true</c>, since the walk did not
/// continue, and the page is not the whole of history.
/// </param>
public sealed record RowPage(IReadOnlyList<EventRow> Rows, bool ReachedBoundary, IReadOnlyList<MemberReadError> Errors, bool Abandoned = false);
