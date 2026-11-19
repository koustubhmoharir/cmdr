using Logvu.Core.Filter;

namespace Logvu.Core.Parsing;

/// <summary>
/// The values of an event, by path: how a field predicate, a column, or any other reader finds the value of a field of a parsed event. A text
/// event's values are its named fields, its <c>message</c> among them. A JSON event's values are the members at a dotted path, with the
/// event's message standing for <c>message</c> when the object has no such member.
/// </summary>
public static class EventFields
{
    /// <summary>The path of an event's message, which is a field of every text event and falls back to the message of a JSON event.</summary>
    public const string MessagePath = "message";

    /// <summary>
    /// Finds the value of a path in an event. A JSON event is read by its dotted path (<see cref="JsonFieldPath"/>), and when that does not
    /// resolve and the path is <c>message</c>, the event's <see cref="LogEvent.Message"/> is the value: a slog message (its <c>msg</c> with
    /// the error and cause parts) or a rendered Serilog message. So a JSON object with its own top-level <c>message</c> member gives that
    /// member. A text event's value is its first field with the path as its name. A raw event has no values.
    /// </summary>
    /// <param name="ev">The event.</param>
    /// <param name="path">The field name, or the dotted JSON path.</param>
    /// <param name="value">Receives the value's string form when the path has one: a JSON string unquoted, other JSON values as their raw text.</param>
    /// <returns><c>true</c> when the event has a value at the path; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    public static bool TryGetValue(LogEvent ev, string path, out string value)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(path);
        value = string.Empty;
        if (ev.IsRaw)
        {
            return false;
        }

        if (ev.Json is { } json)
        {
            if (JsonFieldPath.TryGetText(json, path, out value))
            {
                return true;
            }

            if (path == MessagePath)
            {
                value = ev.Message;
                return true;
            }

            return false;
        }

        foreach (LogField field in ev.Fields)
        {
            if (field.Name == path)
            {
                value = field.Value;
                return true;
            }
        }

        return false;
    }
}
