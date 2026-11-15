using System.Globalization;
using Cmdr.Core.Output;

namespace Cmdr.Tui;

/// <summary>
/// Cell-based wrapping of output lines, the way a terminal wraps: the cells are the grapheme clusters of the line's plain text,
/// a row takes cells until the next one would not fit, and a grapheme is never split. Zero-width graphemes take no cell: they
/// are not drawn, but they stay in the text, so copying the line includes them. Pure: no UI types.
/// </summary>
internal static class OutputWrap
{
    /// <summary>
    /// One drawable cell of a line: the grapheme's text (a grapheme that starts with a control character is the space that
    /// draws it), its display column in the line, its width in columns and the index of the span that holds its first unit.
    /// </summary>
    internal readonly record struct Cell(string Text, int Column, int Width, int Span);

    /// <summary>
    /// The cells of a line in display order: one per grapheme cluster of <see cref="OutputLine.PlainText"/> that has a width.
    /// Zero-width graphemes (such as a combining mark with no base before it) give no cell. A grapheme that starts with a control
    /// character is a space of width 1. A grapheme that spans two spans belongs to the span of its first unit.
    /// </summary>
    public static List<Cell> Cells(OutputLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        string plain = line.PlainText;
        var cells = new List<Cell>(plain.Length);
        int column = 0;
        int span = 0;
        int spanEnd = line.Runs.Length > 0 ? line.RunEnd(0) : 0;
        int offset = 0;
        while (offset < plain.Length)
        {
            while (offset >= spanEnd)
            {
                span++;
                spanEnd = line.RunEnd(span);
            }

            int length = StringInfo.GetNextTextElementLength(plain.AsSpan(offset));
            int width = TextWidth.WidthOf(plain.AsSpan(offset, length));
            if (width > 0)
            {
                string text = char.IsControl(plain[offset]) ? " " : plain.Substring(offset, length);
                cells.Add(new Cell(text, column, width, span));
                column += width;
            }

            offset += length;
        }

        return cells;
    }

    /// <summary>
    /// The number of rows the line wraps to in <paramref name="width"/> columns. Always at least 1. Same rule as
    /// <see cref="Rows(IReadOnlyList{int}, int)"/> on the widths of <see cref="Cells"/>, without building the cells.
    /// </summary>
    public static int RowCount(OutputLine line, int width)
    {
        ArgumentNullException.ThrowIfNull(line);

        string plain = line.PlainText;
        var breaker = new RowBreaker(width);
        int rows = 1;
        int offset = 0;
        while (offset < plain.Length)
        {
            int length = StringInfo.GetNextTextElementLength(plain.AsSpan(offset));
            int cellWidth = TextWidth.WidthOf(plain.AsSpan(offset, length));
            if (cellWidth > 0 && breaker.Add(cellWidth))
            {
                rows++;
            }

            offset += length;
        }

        return rows;
    }

    /// <summary>The rows of a line, as display-column ranges <c>[StartColumn, EndColumn)</c> of the line. See <see cref="Rows(IReadOnlyList{int}, int)"/>.</summary>
    public static List<(int StartColumn, int EndColumn)> Rows(OutputLine line, int width) =>
        Rows(Cells(line).Select(cell => cell.Width).ToList(), width);

    /// <summary>
    /// Splits cells of the given widths into rows of at most <paramref name="width"/> columns (a width below 1 counts as 1).
    /// A cell that does not fit in the rest of a row starts the next row, and the rest of the row is left blank; a row
    /// never splits a cell. A cell wider than the whole row gets a row of its own. Widths of 0 take no column and are skipped.
    /// The result is the column ranges of the rows, in order, and is never empty: no cells give one empty row. The
    /// columns are display columns of the line, so a cell's column is the sum of the widths before it.
    /// </summary>
    public static List<(int StartColumn, int EndColumn)> Rows(IReadOnlyList<int> widths, int width)
    {
        ArgumentNullException.ThrowIfNull(widths);

        var breaker = new RowBreaker(width);
        var rows = new List<(int StartColumn, int EndColumn)>();
        foreach (int cellWidth in widths)
        {
            if (cellWidth <= 0)
            {
                continue;
            }

            // The current row is [Start, End) until the cell is added.
            int start = breaker.Start;
            int end = breaker.End;
            if (breaker.Add(cellWidth))
            {
                rows.Add((start, end));
            }
        }

        rows.Add((breaker.Start, breaker.End));
        return rows;
    }

    /// <summary>
    /// The index of the first cell whose column is at least <paramref name="column"/>, or the cell count when there is none.
    /// <paramref name="cells"/> must be in column order, as <see cref="Cells"/> returns them.
    /// </summary>
    public static int FirstCellAt(IReadOnlyList<Cell> cells, int column)
    {
        ArgumentNullException.ThrowIfNull(cells);

        int low = 0;
        int high = cells.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (cells[middle].Column < column)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>
    /// The row-breaking rule, the one place it is written: <see cref="Rows(IReadOnlyList{int}, int)"/> and
    /// <see cref="RowCount"/> both use it, so they cannot diverge. Feed it cells of positive width, in order.
    /// </summary>
    private struct RowBreaker
    {
        private readonly int _limit;

        public RowBreaker(int width)
        {
            _limit = Math.Max(1, width);
            Start = 0;
            End = 0;
        }

        /// <summary>The first column of the current row.</summary>
        public int Start { get; private set; }

        /// <summary>The column after the last cell of the current row.</summary>
        public int End { get; private set; }

        /// <summary>
        /// Adds a cell of positive width. Returns true when the cell starts a new row; the row it closed is the range
        /// [Start, End) as it was before the call. An empty row takes whatever comes, even a cell wider than the row.
        /// </summary>
        public bool Add(int cellWidth)
        {
            bool breaks = End > Start && End - Start + cellWidth > _limit;
            if (breaks)
            {
                Start = End;
            }

            End += cellWidth;
            return breaks;
        }
    }
}
