using Logvu.Core.Store;

namespace Logvu.Tui;

/// <summary>
/// Reads the detail of one event. The controller uses it for the detail view, so a test can hold, complete or fail a read; the store is
/// behind <see cref="LogStoreDetailSource"/> in the application.
/// </summary>
internal interface IDetailSource
{
    /// <summary>Reads the full text and parsed event of the row at <paramref name="key"/>. See <see cref="LogStore.ReadDetailAsync"/>.</summary>
    /// <param name="key">The row's key.</param>
    /// <param name="ct">Cancels the read.</param>
    Task<EventDetail> ReadDetailAsync(RowKey key, CancellationToken ct);
}

/// <summary>The detail source of a store: its <see cref="LogStore.ReadDetailAsync"/>.</summary>
internal sealed class LogStoreDetailSource : IDetailSource
{
    private readonly LogStore _store;

    /// <summary>Creates a source that reads from a store.</summary>
    /// <param name="store">The store to read. It must outlive the source.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <c>null</c>.</exception>
    public LogStoreDetailSource(LogStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc/>
    public Task<EventDetail> ReadDetailAsync(RowKey key, CancellationToken ct) => _store.ReadDetailAsync(key, ct);
}
