using System.Collections.Immutable;
using System.Drawing;
using Mdedit.Core.Render;

namespace Mdedit.Tui;

/// <summary>
/// The table cells of the view: the cell under the caret (for the table keys and menu), the cell's place on the screen (for the inline
/// field), the reveal of a cell (the table scrolls to show it) and the placement of the caret on a cell after an edit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rows and columns.</b> The display lines of a table block are read, not its source: the first line of each row holds the cells' bars
/// (the synthetic <c>│</c> cells between and around the cells), and its wrapped lines continue it. A column is the cells between two bars,
/// so the column of a caret is the number of bars before it, less one.
/// </para>
/// <para>
/// <b>Empty cells.</b> An empty cell's blank is a mapped cell (the layout gives it its place between the pipes), so the caret rests on it
/// and <see cref="TableCellAtCaret"/> finds the cell.
/// </para>
/// </remarks>
internal sealed partial class RenderedView
{
    // The width of a cell's left padding, before its content, in columns.
    private const int CellPadding = 1;

    /// <summary>A cell of a table: the table block's index in the document, its row (0 for the header) and its column.</summary>
    /// <param name="Block">The index of the table block in <see cref="Document"/>'s blocks.</param>
    /// <param name="Row">The row, 0 for the header.</param>
    /// <param name="Column">The column, from 0.</param>
    public readonly record struct TableCell(int Block, int Row, int Column);

    /// <summary>
    /// The table cell under the caret, or null when the caret is not on a mapped cell of a table. The row and column are those of the
    /// caret's line and cell among the table's bars.
    /// </summary>
    public TableCell? TableCellAtCaret()
    {
        if (!SyncLayout() || !HasCursor)
        {
            return null;
        }

        int block = BlockOfLine(_line);
        if (block < 0 || _document.Blocks[block].Table is not { } table)
        {
            return null;
        }

        List<int> bars = BarsOf(_line);
        int column = bars.Count(bar => bar < _cell) - 1;
        int row = RowStartsOf(block).Where(start => start <= _line).Count() - 1;
        if (column < 0 || column >= table.Alignments.Length || row < 0 || row >= table.Rows.Length || bars.Count == 0)
        {
            return null;
        }

        return new TableCell(block, row, column);
    }

    /// <summary>
    /// The screen rectangle of a cell for its inline field: the first line of the cell's row, from the cell's text column (after its left
    /// padding) to the cell's right edge, in the view's viewport coordinates. Null when the cell is not in the layout. The rectangle may be
    /// outside the viewport, so <see cref="RevealTableCell"/> comes first.
    /// </summary>
    /// <param name="cell">The cell.</param>
    public Rectangle? TableCellRectangle(TableCell cell)
    {
        if (!SyncLayout() || CellSpan(cell) is not { } span)
        {
            return null;
        }

        int x = span.Start + CellPadding;
        return new Rectangle(x - ShiftFor(span.Line, x), span.Line - _top, Math.Max(1, span.End - x), 1);
    }

    /// <summary>
    /// Scrolls the view, by the least amount, so that the cell's first line is in view and the table shows the cell's text column. The caret
    /// does not move. Returns false when the cell is not in the layout.
    /// </summary>
    /// <param name="cell">The cell.</param>
    public bool RevealTableCell(TableCell cell)
    {
        if (!SyncLayout() || CellSpan(cell) is not { } span)
        {
            return false;
        }

        int before = _top;
        EnsureColumnVisible(span.Line, span.Start + CellPadding, Math.Max(1, span.End - span.Start - CellPadding));
        EnsureLineVisible(span.Line);
        SyncTracking();
        UpdateCursor();
        SetNeedsDraw();
        if (_top != before)
        {
            PositionChanged?.Invoke();
        }

        return true;
    }

    /// <summary>
    /// Places the caret on a cell of a table: on the first mapped cell of its source, or, for an empty cell, on its source position (which
    /// the caret resolves to the next caret stop, see the remarks of the class). Returns false when the cell does not exist.
    /// </summary>
    /// <param name="block">The index of the table block in the document.</param>
    /// <param name="row">The row, 0 for the header.</param>
    /// <param name="column">The column.</param>
    public bool PlaceTableCell(int block, int row, int column)
    {
        if (block < 0 || block >= _document.Blocks.Length || _document.Blocks[block] is not { Table: { } table } tableBlock)
        {
            return false;
        }

        if (row < 0 || row >= table.Rows.Length || column < 0 || column >= table.Alignments.Length)
        {
            return false;
        }

        foreach (Cell cell in table.Rows[row].Cells[column].Cells)
        {
            if (cell.IsMapped)
            {
                PlaceCaret(tableBlock.Start + cell.Offset, null);
                return true;
            }
        }

        // An empty cell has no mapped cell of its run: its position is the blank the layout maps for it.
        ImmutableArray<int> positions = table.Rows[row].Positions;
        if (positions.IsDefault || column >= positions.Length || positions[column] == Cell.Synthetic)
        {
            return false;
        }

        PlaceCaret(tableBlock.Start + positions[column], null);
        return true;
    }

    /// <summary>The display line of a row's first line, or -1 when the row is not in the layout.</summary>
    /// <param name="block">The table block's index.</param>
    /// <param name="row">The row.</param>
    private int FirstLineOfRow(int block, int row)
    {
        List<int> starts = RowStartsOf(block);
        return row >= 0 && row < starts.Count ? starts[row] : -1;
    }

    /// <summary>
    /// The display lines that begin a row of a table block, in order: the lines with bars that do not continue a row. Its count is the
    /// number of the block's rows.
    /// </summary>
    /// <param name="block">The table block's index.</param>
    private List<int> RowStartsOf(int block)
    {
        var starts = new List<int>();
        for (int line = 0; line < LineCount; line++)
        {
            if (BlockOfLine(line) == block && !Built.Line(line).IsContinuation && BarsOf(line).Count > 0)
            {
                starts.Add(line);
            }
        }

        return starts;
    }

    /// <summary>The span of a cell's text on its row's first line: the layout columns of the cell's padding and content, and its line.</summary>
    /// <param name="cell">The cell.</param>
    private (int Line, int Start, int End)? CellSpan(TableCell cell)
    {
        int line = FirstLineOfRow(cell.Block, cell.Row);
        if (line < 0)
        {
            return null;
        }

        List<int> bars = BarsOf(line);
        if (cell.Column < 0 || cell.Column + 1 >= bars.Count)
        {
            return null;
        }

        int[] columns = ColumnsOf(line);
        int start = columns[bars[cell.Column]] + Built.Line(line).Cells[bars[cell.Column]].Width;
        int end = columns[bars[cell.Column + 1]];
        return (line, start, end);
    }

    /// <summary>The indices of the bars of a line, after its prefix: the cells that separate and enclose the table's columns.</summary>
    /// <param name="line">The display line.</param>
    private List<int> BarsOf(int line)
    {
        DisplayLine display = Built.Line(line);
        var bars = new List<int>();
        for (int cell = display.PrefixCells; cell < display.Cells.Length; cell++)
        {
            if (IsBar(display.Cells[cell]))
            {
                bars.Add(cell);
            }
        }

        return bars;
    }

    /// <summary>Whether a cell is a column bar of a table: a synthetic <c>│</c> in the table border role.</summary>
    private static bool IsBar(Cell cell) => !cell.IsMapped && cell.Style.Role == StyleRole.TableBorder && cell.Text == "│";
}
