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

    /// <summary>
    /// Tells whether an event that is known only by its text may match a pattern, before the event is parsed. It is exact for a text event,
    /// which matches by its text alone. A JSON event may also match by its parsed message, so the result is <c>true</c> for it, except in one
    /// case. A slog message joins several JSON string values with <c>": "</c>. Each value is in the text verbatim when the text has no backslash,
    /// so a match in the message lies in the text too, unless it spans a join. A pattern without <c>':'</c> or <c>' '</c> cannot span one, so a
    /// literal pattern that the text does not contain cannot match the message either.
    /// </summary>
    /// <param name="pattern">The pattern to search for.</param>
    /// <param name="text">The event's full text.</param>
    /// <param name="scheme">The scheme of the event's member.</param>
    /// <returns><c>false</c> only when <see cref="Matches"/> would be <c>false</c> for the event.</returns>
    public static bool MayMatch(SearchPattern pattern, string text, ILogScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(scheme);

        if (!scheme.IsJson)
        {
            return pattern.Count(text) > 0;
        }

        bool verbatimMessage = scheme is SlogJsonScheme && !pattern.Query.Regex && !pattern.Query.WholeWord && text.IndexOf('\\') < 0
            && pattern.Query.Text.IndexOfAny([':', ' ']) < 0;
        return !verbatimMessage || pattern.Count(text) > 0;
    }
}
