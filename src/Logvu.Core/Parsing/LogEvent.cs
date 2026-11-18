using System.Text.Json;

namespace Logvu.Core.Parsing;

/// <summary>
/// One parsed event. Immutable once built. A raw event (<see cref="IsRaw"/>) carries its whole text as
/// <see cref="Message"/>, with <see cref="ParseError"/> set when the text was a malformed record.
/// </summary>
public sealed class LogEvent
{
    /// <summary>The timestamp, or <c>null</c> when absent or not parseable.</summary>
    public DateTimeOffset? Time { get; init; }

    /// <summary>The timestamp as written in the log, or <c>null</c> when absent.</summary>
    public string? TimeText { get; init; }

    /// <summary>The normalized level.</summary>
    public LogLevel Level { get; init; } = LogLevel.Unknown;

    /// <summary>The level as written in the log, or <c>null</c> when absent.</summary>
    public string? LevelText { get; init; }

    /// <summary>The logger name or source context, or <c>null</c> when the event has none.</summary>
    public string? Source { get; init; }

    /// <summary>The full message. For text events, continuation lines are joined with <c>\n</c>.</summary>
    public required string Message { get; init; }

    /// <summary>The named values of the event, in scheme order.</summary>
    public IReadOnlyList<LogField> Fields { get; init; } = [];

    /// <summary>The parsed JSON object of a JSON event, cloned so that it outlives the document it was read from.</summary>
    public JsonElement? Json { get; init; }

    /// <summary>Why the event is raw, or <c>null</c> when the event is not a parse failure.</summary>
    public string? ParseError { get; init; }

    /// <summary>True when the text could not be parsed as a record of its scheme.</summary>
    public bool IsRaw { get; init; }

    /// <summary>Builds a raw event whose whole text is the message.</summary>
    /// <param name="text">The event text.</param>
    /// <param name="parseError">The reason it could not be parsed, or <c>null</c> for orphan text.</param>
    public static LogEvent Raw(string text, string? parseError)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new LogEvent
        {
            Message = text,
            Level = LogLevel.Unknown,
            ParseError = parseError,
            IsRaw = true,
        };
    }
}
