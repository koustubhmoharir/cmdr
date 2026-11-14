using Cmdr.Core.Output;

namespace Cmdr.Tui;

/// <summary>
/// The identity of a partial (unfinished) line, for finding it again after its number changed. A partial line has no
/// number of its own: when another stream or a system line completes a line in front of it, it moves to the next number.
/// The source and the text it had (the text may grow, so a match only needs the pinned text as a prefix) identify it.
/// </summary>
internal readonly record struct PartialPin(OutputSource Source, string Text)
{
    /// <summary>The pin of <paramref name="line"/> as it is now.</summary>
    public static PartialPin For(OutputLine line) => new(line.Source, line.PlainText);

    /// <summary>True when <paramref name="line"/> is from the same source and still starts with the pinned text.</summary>
    public bool Matches(OutputLine? line) =>
        line is not null && line.Source == Source && line.PlainText.StartsWith(Text, StringComparison.Ordinal);
}

/// <summary>Finds a pinned partial line again after the buffer changed. Pure: no view, no buffer.</summary>
internal static class PartialPins
{
    /// <summary>
    /// The number of the line the pin now names, or null when no retained line matches. The line at <paramref name="line"/>
    /// is checked first; otherwise the lines after it, in order, up to <paramref name="lastLine"/>, are searched for the first
    /// match. Numbers below <paramref name="firstLine"/> are not retained, so the search starts there when the pinned line was
    /// evicted. <paramref name="lineAt"/> returns the line with an absolute number, or null when it is not retained.
    /// </summary>
    public static long? Find(PartialPin pin, long line, long firstLine, long lastLine, Func<long, OutputLine?> lineAt)
    {
        ArgumentNullException.ThrowIfNull(lineAt);

        if (line >= firstLine && line <= lastLine && pin.Matches(lineAt(line)))
        {
            return line;
        }

        for (long number = Math.Max(line + 1, firstLine); number <= lastLine; number++)
        {
            if (pin.Matches(lineAt(number)))
            {
                return number;
            }
        }

        return null;
    }
}
