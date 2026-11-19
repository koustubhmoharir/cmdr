using Logvu.Core.Filter;
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
    public bool ChecksHead => filter?.ChecksHead ?? false;

    /// <inheritdoc/>
    public bool ChecksHeadTime => filter?.ChecksHeadTime ?? false;

    /// <inheritdoc/>
    public bool ChecksText => filter is null || filter is EventFilter { Fields.Count: 0 };

    /// <inheritdoc/>
    public bool Matches(EventRow row, LogEvent ev, string text) =>
        (filter is null || filter.Matches(row, ev, text)) && EventSearch.Matches(pattern, ev, text);

    /// <inheritdoc/>
    public bool MayMatchHead(EventRow head) => filter is null || filter.MayMatchHead(head);

    /// <inheritdoc/>
    /// <remarks>
    /// The filter's own text test runs first, as <see cref="Matches"/> runs it first. A filter with field predicates needs the parsed event,
    /// so this filter does not check the text then (<see cref="ChecksText"/>).
    /// </remarks>
    public bool MayMatchText(string text, ILogScheme scheme) =>
        (filter is null || filter.MayMatchText(text, scheme)) && EventSearch.MayMatch(pattern, text, scheme);
}
