using Logvu.Core.Parsing;
using Shared.Core;

namespace Logvu.Core.Store;

/// <summary>
/// The predicate of a search: an event must pass the active filter, when there is one, and match the pattern. A history scan of this
/// filter with a count of one finds the nearest match. The filter is abandoned when its own filter is, or when the pattern timed out.
/// </summary>
internal sealed class EventSearchFilter(IRowFilter? filter, SearchPattern pattern) : IRowFilter
{
    /// <inheritdoc/>
    public bool Abandoned => (filter?.Abandoned ?? false) || pattern.TimedOut;

    /// <inheritdoc/>
    public bool Matches(EventRow row, LogEvent ev, string text) =>
        (filter is null || filter.Matches(row, ev, text)) && EventSearch.Matches(pattern, ev, text);
}
