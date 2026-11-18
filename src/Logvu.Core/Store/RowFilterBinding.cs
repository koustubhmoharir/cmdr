namespace Logvu.Core.Store;

/// <summary>Binds the filters that need a timeline to the snapshot a read walks.</summary>
internal static class RowFilterBinding
{
    /// <summary>Binds a filter to a snapshot when it needs one; any other filter, and <c>null</c>, is returned as it is.</summary>
    /// <param name="filter">The filter a read was given, or <c>null</c>.</param>
    /// <param name="snapshot">The timeline the read walks.</param>
    public static IRowFilter? Bind(IRowFilter? filter, CatalogSnapshot snapshot) =>
        filter is IBindableRowFilter bindable ? bindable.Bind(snapshot) : filter;
}
