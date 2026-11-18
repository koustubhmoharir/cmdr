using Logvu.Core.Parsing;
using Shared.Core;

namespace Logvu.Core.Store;

/// <summary>
/// The predicate of a search: an event must pass the active filter, when there is one, and match the pattern. A history scan of this
/// filter with a count of one finds the nearest match.
/// </summary>
internal sealed class EventSearchFilter(IRowFilter? filter, SearchPattern pattern) : IRowFilter
{
    /// <inheritdoc/>
    public bool Matches(EventRow row, LogEvent ev, string text)
    {
        bool matches = (filter is null || filter.Matches(row, ev, text)) && EventSearch.Matches(pattern, ev, text);

        // A pattern that timed out matches nothing from then on (SearchPattern abandons it), so the rest of the scan would only
        // read files to find nothing and end at the boundary, which would read as "no more matches". Stop the scan instead.
        if (pattern.TimedOut)
        {
            throw new PatternTimedOutException();
        }

        return matches;
    }
}

/// <summary>Ends a search scan whose pattern timed out. Caught by the search, which reports <see cref="SearchResult.TimedOut"/>.</summary>
internal sealed class PatternTimedOutException : Exception
{
    public PatternTimedOutException()
        : base("The search pattern timed out.")
    {
    }
}
