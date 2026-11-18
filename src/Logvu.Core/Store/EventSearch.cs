using Logvu.Core.Parsing;
using Shared.Core;

namespace Logvu.Core.Store;

/// <summary>The match of a search pattern against one event.</summary>
public static class EventSearch
{
    /// <summary>
    /// Tells whether a pattern matches an event. The pattern matches when it matches the event's full text, or, for a JSON event, its
    /// parsed message: a Serilog message is rendered from its template and properties, so the rendered text is not in the raw text
    /// verbatim. A pattern that has timed out (see <see cref="SearchPattern.TimedOut"/>) matches nothing.
    /// </summary>
    /// <param name="pattern">The pattern to search for.</param>
    /// <param name="ev">The parsed event.</param>
    /// <param name="text">The event's full text, as <see cref="RowBuilder"/> read it.</param>
    /// <returns><c>true</c> when the pattern matches the text or the message of a JSON event.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    public static bool Matches(SearchPattern pattern, LogEvent ev, string text)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(text);

        if (pattern.Count(text) > 0)
        {
            return true;
        }

        return ev.Json is not null && pattern.Count(ev.Message) > 0;
    }
}
