using System.Globalization;
using System.Text;

namespace Cmdr.Tui;

/// <summary>
/// A position in the logical text of the details strip: a logical line and a UTF-16 offset into its cleaned text. The offset is
/// a caret, so the character at <see cref="Offset"/> is the first one after it.
/// </summary>
internal readonly record struct LogicalPoint(int Line, int Offset) : IComparable<LogicalPoint>
{
    public int CompareTo(LogicalPoint other) =>
        Line != other.Line ? Line.CompareTo(other.Line) : Offset.CompareTo(other.Offset);
}

/// <summary>A selection in logical text: <see cref="Start"/> is never after <see cref="End"/>. Covers the characters from Start up to End.</summary>
internal readonly record struct LogicalRange(LogicalPoint Start, LogicalPoint End)
{
    /// <summary>The range between two points, in either order.</summary>
    public static LogicalRange Between(LogicalPoint a, LogicalPoint b) =>
        a.CompareTo(b) <= 0 ? new LogicalRange(a, b) : new LogicalRange(b, a);

    public bool IsEmpty => Start.CompareTo(End) == 0;

    /// <summary>True when the character at <paramref name="offset"/> of logical line <paramref name="line"/> is selected.</summary>
    public bool Covers(int line, int offset)
    {
        var point = new LogicalPoint(line, offset);
        return point.CompareTo(Start) >= 0 && point.CompareTo(End) < 0;
    }
}

/// <summary>
/// The selection of the details strip, on its logical lines (see <see cref="DetailsRow"/>): the mapping from display cells to
/// logical positions, the selected text, and the clamping that keeps a selection valid when the text changes. Pure.
/// </summary>
internal static class DetailsSelection
{
    /// <summary>The logical text of each line: its cleaned plain text, the same as the displayed text before wrapping.</summary>
    public static IReadOnlyList<string> Texts(IReadOnlyList<DetailsLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return lines.Select(line => TextWidth.Clean(line.Text)).ToList();
    }

    /// <summary>
    /// The selection between two points, clamped to the text: a line past the end is the last line, and an offset past the end
    /// of its line is the end of the line. With no text, both points are the origin.
    /// </summary>
    public static LogicalRange Clamp(LogicalPoint anchor, LogicalPoint active, IReadOnlyList<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        return LogicalRange.Between(ClampPoint(anchor, texts), ClampPoint(active, texts));
    }

    /// <summary>
    /// The selected text: the characters of each logical line that the range covers, with the lines joined by "\n". The spaces
    /// dropped where a line was wrapped are part of the logical text, so they are copied back.
    /// </summary>
    public static string Extract(IReadOnlyList<string> texts, LogicalRange range)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0)
        {
            return string.Empty;
        }

        LogicalRange clamped = Clamp(range.Start, range.End, texts);
        var parts = new List<string>();
        for (int line = clamped.Start.Line; line <= clamped.End.Line; line++)
        {
            string text = texts[line];
            int from = line == clamped.Start.Line ? clamped.Start.Offset : 0;
            int to = line == clamped.End.Line ? clamped.End.Offset : text.Length;
            parts.Add(text.Substring(from, to - from));
        }

        return string.Join("\n", parts);
    }

    /// <summary>
    /// The logical position under a display cell: the row at <paramref name="rowIndex"/> (clamped to the rows) and the column.
    /// The caret goes to the nearer side of the character under the column. A column past the end of the row is the end of the
    /// row, so a click to the right of the text selects to its end.
    /// </summary>
    public static LogicalPoint PointAt(IReadOnlyList<DetailsRow> rows, IReadOnlyList<string> texts, int rowIndex, int column)
    {
        if (!Locate(rows, texts, rowIndex, column, out DetailsRow row, out string text, out int target))
        {
            return default;
        }

        if (!FindCell(row, text, target, out Grapheme grapheme, out _, out _))
        {
            return new LogicalPoint(row.LogicalLine, row.End);
        }

        bool before = (target - grapheme.Cell) * 2 < grapheme.Width;
        return new LogicalPoint(row.LogicalLine, before ? grapheme.Offset : grapheme.Offset + grapheme.Length);
    }

    /// <summary>
    /// The cell under a display cell, as the two points either side of it: <c>Before</c> is where the text before the cell ends and
    /// <c>After</c> is the end of the cell's grapheme. The cell is the grapheme under the column, as <see cref="PointAt"/> finds it,
    /// with any zero-width graphemes that follow it, so a combining mark is never split off. A cell that is wide takes both of its
    /// columns. The first cell of a row starts at the row's start. A column past the end of the row, or any column of a row with no
    /// cells, gives the end of the row for both points. A drag selects from one cell to another, both included, so a cell's two
    /// points are used as the ends of the selection.
    /// </summary>
    public static (LogicalPoint Before, LogicalPoint After) CellAt(IReadOnlyList<DetailsRow> rows, IReadOnlyList<string> texts, int rowIndex, int column)
    {
        if (!Locate(rows, texts, rowIndex, column, out DetailsRow row, out string text, out int target))
        {
            return default;
        }

        if (!FindCell(row, text, target, out _, out int before, out int after))
        {
            var end = new LogicalPoint(row.LogicalLine, row.End);
            return (end, end);
        }

        return (new LogicalPoint(row.LogicalLine, before), new LogicalPoint(row.LogicalLine, after));
    }

    /// <summary>
    /// The row a display cell is on, its text and the column within the row, at least 0. False when there are no rows.
    /// </summary>
    private static bool Locate(
        IReadOnlyList<DetailsRow> rows,
        IReadOnlyList<string> texts,
        int rowIndex,
        int column,
        out DetailsRow row,
        out string text,
        out int target)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(texts);

        if (rows.Count == 0)
        {
            (row, text, target) = (default!, string.Empty, 0);
            return false;
        }

        row = rows[Math.Clamp(rowIndex, 0, rows.Count - 1)];
        text = texts[row.LogicalLine];
        target = Math.Max(0, column);
        return true;
    }

    /// <summary>
    /// Walks the graphemes of a row from its start to the one that covers cell <paramref name="target"/>. When that grapheme is found,
    /// <paramref name="before"/> is the start of its cell and <paramref name="after"/> the end of the zero-width graphemes after it.
    /// Zero-width graphemes take no cell, so they are never the grapheme found, and a zero-width grapheme before the first cell belongs
    /// with that cell. False when the row ends before the target cell.
    /// </summary>
    private static bool FindCell(DetailsRow row, string text, int target, out Grapheme grapheme, out int before, out int after)
    {
        int cells = 0;
        int offset = row.Start;
        while (offset < row.End)
        {
            // The row starts and ends on grapheme boundaries, so the grapheme at offset is whole within the row.
            int length = StringInfo.GetNextTextElementLength(text.AsSpan(offset, row.End - offset));
            int width = TextWidth.WidthOf(text.AsSpan(offset, length));
            if (target < cells + width)
            {
                grapheme = new Grapheme(offset, length, cells, width);
                before = cells == 0 ? row.Start : offset;
                after = offset + length;
                while (after < row.End)
                {
                    int next = StringInfo.GetNextTextElementLength(text.AsSpan(after, row.End - after));
                    if (TextWidth.WidthOf(text.AsSpan(after, next)) > 0)
                    {
                        break;
                    }

                    after += next;
                }

                return true;
            }

            cells += width;
            offset += length;
        }

        (grapheme, before, after) = (default, row.End, row.End);
        return false;
    }

    /// <summary>A grapheme of a row: its UTF-16 offset and length, and the cell it starts at and its width in cells.</summary>
    private readonly record struct Grapheme(int Offset, int Length, int Cell, int Width);


    /// <summary>The rune starting at <paramref name="offset"/> and the number of UTF-16 units it takes. An invalid unit is one replacement character.</summary>
    internal static (Rune Rune, int Length) RuneAt(string text, int offset) =>
        Rune.TryGetRuneAt(text, offset, out Rune rune) ? (rune, rune.Utf16SequenceLength) : (Rune.ReplacementChar, 1);

    private static LogicalPoint ClampPoint(LogicalPoint point, IReadOnlyList<string> texts)
    {
        if (texts.Count == 0)
        {
            return default;
        }

        int line = Math.Clamp(point.Line, 0, texts.Count - 1);
        return new LogicalPoint(line, Math.Clamp(point.Offset, 0, texts[line].Length));
    }
}
