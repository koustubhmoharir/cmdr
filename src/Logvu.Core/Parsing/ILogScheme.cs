namespace Logvu.Core.Parsing;

/// <summary>A log format: recognizes event headers and turns the text of one event into a <see cref="LogEvent"/>.</summary>
public interface ILogScheme
{
    /// <summary>The scheme name, used in configuration and on the command line.</summary>
    string Name { get; }

    /// <summary>True for schemes where every non-blank line is one event (JSON Lines).</summary>
    bool IsJson { get; }

    /// <summary>
    /// Tells whether a line starts a new event. For text schemes this is an anchored header match. For JSON schemes it is
    /// true for every line that is not whitespace-only.
    /// </summary>
    /// <param name="line">One line, without its line terminator.</param>
    bool IsHeader(string line);

    /// <summary>Parses one event.</summary>
    /// <param name="eventText">The event's lines joined by <c>\n</c>, without a trailing newline.</param>
    /// <param name="context">The parse settings.</param>
    LogEvent Parse(string eventText, ParseContext context);
}
