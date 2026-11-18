using Logvu.Core.Parsing;

namespace Logvu.Core.Store;

/// <summary>
/// The scheme of a file whose format cannot be detected: every line is an event of its own, and none has a time. The events
/// are shown raw.
/// </summary>
internal sealed class RawLinesScheme : ILogScheme
{
    /// <summary>The one instance: the scheme has no state.</summary>
    public static RawLinesScheme Instance { get; } = new();

    /// <inheritdoc/>
    public string Name => "raw";

    /// <inheritdoc/>
    public bool IsJson => false;

    /// <inheritdoc/>
    /// <remarks>Every line is a header, so every line is its own event.</remarks>
    public bool IsHeader(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return true;
    }

    /// <inheritdoc/>
    public LogEvent Parse(string eventText, ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(eventText);
        ArgumentNullException.ThrowIfNull(context);
        return LogEvent.Raw(eventText, "format not recognized");
    }
}
