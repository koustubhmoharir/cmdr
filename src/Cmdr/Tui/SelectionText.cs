using System.Text;

namespace Cmdr.Tui;

/// <summary>A cell in output coordinates: an absolute line number (see <c>OutputBuffer.FirstLineNumber</c>) and a 0-based display column.</summary>
internal readonly record struct SelectionPoint(long Line, int Column) : IComparable<SelectionPoint>
{
    public int CompareTo(SelectionPoint other) =>
        Line != other.Line ? Line.CompareTo(other.Line) : Column.CompareTo(other.Column);
}

/// <summary>A normalized selection: <see cref="Start"/> is never after <see cref="End"/>. In line mode the columns are ignored.</summary>
internal readonly record struct SelectionRange(SelectionPoint Start, SelectionPoint End, bool LineMode)
{
    /// <summary>The range between two points, in either order.</summary>
    public static SelectionRange Between(SelectionPoint anchor, SelectionPoint active, bool lineMode) =>
        anchor.CompareTo(active) <= 0 ? new SelectionRange(anchor, active, lineMode) : new SelectionRange(active, anchor, lineMode);

    /// <summary>
    /// True when the cells [<paramref name="column"/>, <paramref name="column"/> + <paramref name="width"/>) of the line overlap
    /// the selection. A wide character that is only partly inside counts as selected as a whole.
    /// </summary>
    public bool Covers(long line, int column, int width)
    {
        if (line < Start.Line || line > End.Line)
        {
            return false;
        }

        if (LineMode)
        {
            return true;
        }

        int last = column + Math.Max(1, width) - 1;
        if (line == Start.Line && last < Start.Column)
        {
            return false;
        }

        return line != End.Line || column <= End.Column;
    }
}

/// <summary>Extracts the text of a selection. Pure: works on plain text and display columns (see <see cref="TextWidth"/>).</summary>
internal static class SelectionText
{
    /// <summary>
    /// The selected text. <paramref name="lines"/>[i] is the plain text of absolute line <paramref name="firstLine"/> + i.
    /// Selected lines outside that list (evicted from the buffer) are absent from the result. Lines are joined with "\n"
    /// and there is no trailing newline.
    /// </summary>
    public static string Extract(IReadOnlyList<string> lines, long firstLine, SelectionRange range)
    {
        ArgumentNullException.ThrowIfNull(lines);

        long from = Math.Max(range.Start.Line, firstLine);
        long to = Math.Min(range.End.Line, firstLine + lines.Count - 1);
        if (from > to)
        {
            return string.Empty;
        }

        var parts = new List<string>((int)(to - from + 1));
        for (long line = from; line <= to; line++)
        {
            string text = lines[(int)(line - firstLine)];
            if (range.LineMode)
            {
                parts.Add(text);
                continue;
            }

            int fromColumn = line == range.Start.Line ? range.Start.Column : 0;
            int toColumn = line == range.End.Line ? range.End.Column : int.MaxValue;
            parts.Add(Slice(text, fromColumn, toColumn));
        }

        return string.Join("\n", parts);
    }

    /// <summary>The characters of <paramref name="text"/> that overlap the columns [<paramref name="fromColumn"/>, <paramref name="toColumn"/>].</summary>
    internal static string Slice(string text, int fromColumn, int toColumn)
    {
        var sb = new StringBuilder();
        int column = 0;
        bool previousIncluded = false;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int width = TextWidth.Width(rune);
            bool included;
            if (width == 0)
            {
                // A combining mark belongs to the character before it.
                included = previousIncluded;
            }
            else
            {
                included = column + width - 1 >= fromColumn && column <= toColumn;
                previousIncluded = included;
            }

            if (included)
            {
                sb.Append(rune.ToString());
            }

            column += width;
        }

        return sb.ToString();
    }
}
