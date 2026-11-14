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
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(texts);

        if (rows.Count == 0)
        {
            return default;
        }

        DetailsRow row = rows[Math.Clamp(rowIndex, 0, rows.Count - 1)];
        string text = texts[row.LogicalLine];
        int target = Math.Max(0, column);
        int cells = 0;
        int offset = row.Start;
        while (offset < row.End)
        {
            // The row starts and ends on grapheme boundaries, so the grapheme at offset is whole within the row.
            int length = StringInfo.GetNextTextElementLength(text.AsSpan(offset, row.End - offset));
            int width = TextWidth.WidthOf(text.AsSpan(offset, length));
            if (target < cells + width)
            {
                bool before = (target - cells) * 2 < width;
                return new LogicalPoint(row.LogicalLine, before ? offset : offset + length);
            }

            cells += width;
            offset += length;
        }

        return new LogicalPoint(row.LogicalLine, row.End);
    }

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
