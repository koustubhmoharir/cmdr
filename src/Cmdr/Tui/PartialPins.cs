using Cmdr.Core.Output;

namespace Cmdr.Tui;

/// <summary>
/// The identity of a partial (unfinished) line, for finding it again after its number changed. A partial line has no
/// number of its own: when another stream or a system line completes a line in front of it, it moves to the next number.
/// The line's <see cref="OutputLine.Identity"/> names it, and the text it had must still be a prefix of its text (it may
/// grow): a line rewritten by a carriage return, or evicted, no longer matches.
/// </summary>
internal readonly record struct PartialPin(long Identity, string Text)
{
    /// <summary>The pin of <paramref name="line"/> as it is now.</summary>
    public static PartialPin For(OutputLine line) => new(line.Identity, line.PlainText);

    /// <summary>True when <paramref name="line"/> is the pinned line and still starts with the pinned text.</summary>
    public bool Matches(OutputLine? line) =>
        line is not null && line.Identity == Identity && line.PlainText.StartsWith(Text, StringComparison.Ordinal);
}

/// <summary>Finds a pinned partial line again after the buffer changed. Pure: no view, no buffer.</summary>
internal static class PartialPins
{
    /// <summary>
    /// The number of the line the pin now names, or null when no retained line matches. The line at <paramref name="line"/>
    /// is checked first. Then the line just before it: a partial moves back by one when the other stream's partial, in front
    /// of it, completes (the one line it can pass). Then the lines after it, in order, up to <paramref name="lastLine"/>: a
    /// partial moves forward when a line completes in front of it. Numbers outside
    /// [<paramref name="firstLine"/>, <paramref name="lastLine"/>] are not retained, so they are not searched.
    /// <paramref name="lineAt"/> returns the line with an absolute number, or null when it is not retained.
    /// </summary>
    public static long? Find(PartialPin pin, long line, long firstLine, long lastLine, Func<long, OutputLine?> lineAt)
    {
        ArgumentNullException.ThrowIfNull(lineAt);

        if (Matches(pin, line, firstLine, lastLine, lineAt))
        {
            return line;
        }

        if (Matches(pin, line - 1, firstLine, lastLine, lineAt))
        {
            return line - 1;
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

    private static bool Matches(PartialPin pin, long number, long firstLine, long lastLine, Func<long, OutputLine?> lineAt) =>
        number >= firstLine && number <= lastLine && pin.Matches(lineAt(number));
}
