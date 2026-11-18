using Logvu.Core.Store;

namespace Logvu.Core.Timeline;

/// <summary>Adapts a <see cref="LogStore"/> to an <see cref="ITimelineSource"/>. The reads report no progress.</summary>
public sealed class LogStoreTimelineSource : ITimelineSource
{
    private readonly LogStore _store;

    /// <summary>Creates a source that reads from a store.</summary>
    /// <param name="store">The store to read. It must outlive the source.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <c>null</c>.</exception>
    public LogStoreTimelineSource(LogStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc/>
    public Task<RowPage> ReadNewestAsync(int count, IRowFilter? filter, CancellationToken ct) =>
        _store.ReadNewestAsync(count, filter, progress: null, ct);

    /// <inheritdoc/>
    public Task<RowPage> ReadOlderAsync(RowKey anchor, int count, IRowFilter? filter, CancellationToken ct) =>
        _store.ReadOlderAsync(anchor, count, filter, progress: null, ct);

    /// <inheritdoc/>
    public Task<RowPage> ReadNewerAsync(RowKey anchor, int count, IRowFilter? filter, CancellationToken ct, bool inclusive = false) =>
        _store.ReadNewerAsync(anchor, count, filter, progress: null, ct, inclusive);
}
