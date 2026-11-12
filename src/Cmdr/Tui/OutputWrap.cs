using System.Text;
using Cmdr.Core.Output;

namespace Cmdr.Tui;

/// <summary>
/// Cell-based wrapping of output lines, the way a terminal wraps: a row takes cells until the next one would not fit, and
/// nothing is dropped, so a line's display column is the same wrapped or not. Pure: no UI types.
/// </summary>
internal static class OutputWrap
{
    /// <summary>
    /// One drawable cell of a line: the rune (control characters are already spaces), its display column in the line,
    /// its width in columns (1 or 2 for the usual characters) and the index of its span.
    /// </summary>
    internal readonly record struct Cell(Rune Rune, int Column, int Width, int Span);

    /// <summary>
    /// The cells of a line in display order. Runes with no columns (such as combining marks) are skipped, control
    /// characters take one column each, and wide characters take two.
    /// </summary>
    public static List<Cell> Cells(OutputLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var cells = new List<Cell>(line.PlainText.Length);
        int column = 0;
        for (int span = 0; span < line.Spans.Count; span++)
        {
            foreach (Rune original in line.Spans[span].Text.EnumerateRunes())
            {
                Rune rune = Rune.IsControl(original) ? new Rune(' ') : original;
                int width = TextWidth.Width(rune);
                if (width == 0)
                {
                    continue;
                }

                cells.Add(new Cell(rune, column, width, span));
                column += width;
            }
        }

        return cells;
    }

    /// <summary>The number of rows the line wraps to in <paramref name="width"/> columns. Always at least 1.</summary>
    public static int RowCount(OutputLine line, int width) => Rows(line, width).Count;

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

        int limit = Math.Max(1, width);
        var rows = new List<(int StartColumn, int EndColumn)>();
        int start = 0;
        int column = 0;
        foreach (int cellWidth in widths)
        {
            if (cellWidth <= 0)
            {
                continue;
            }

            // The current row holds [start, column). An empty row takes whatever comes, even a cell wider than the row.
            if (column > start && column - start + cellWidth > limit)
            {
                rows.Add((start, column));
                start = column;
            }

            column += cellWidth;
        }

        rows.Add((start, column));
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
}
