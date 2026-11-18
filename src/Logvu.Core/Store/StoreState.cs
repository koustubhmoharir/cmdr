namespace Logvu.Core.Store;

/// <summary>
/// One published state of the store: the timeline, the newest rows and the status, taken together by the worker. Immutable, so a reader
/// holding one sees a consistent catalog and ring for the whole of its read.
/// </summary>
/// <param name="Catalog">The timeline. Every generation of <paramref name="Ring"/> is in it.</param>
/// <param name="Ring">The newest rows, oldest first, as the ring held them at publication.</param>
/// <param name="RingCoversNewest">
/// Whether the ring holds the newest rows of history contiguously, so that the rows older than <paramref name="Ring"/>'s oldest one are
/// the ones read from history. See <see cref="RowRing.CoversNewest"/>.
/// </param>
/// <param name="Status">The status at publication.</param>
internal sealed record StoreState(CatalogSnapshot Catalog, IReadOnlyList<EventRow> Ring, bool RingCoversNewest, StoreStatus Status);
