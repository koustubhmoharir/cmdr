using Mdedit.Core.Render;

namespace Mdedit.Core.Tables;

/// <summary>
/// The table operations of the view, as pure functions of the source text and the table block. Each returns the one replacement
/// that performs the operation (with the cell the caret goes to), or a refusal with its message. Rows are numbered as the table
/// model numbers them: 0 is the header, and the delimiter row is not a row.
/// </summary>
/// <remarks>
/// An aligned table (<see cref="TableSource.IsAligned"/>) is rewritten whole, aligned again. Any other table is edited minimally:
/// only the cells and lines that change. Either way the edited document is reparsed and checked (see <see cref="TableVerifier"/>);
/// a check that fails is refused with <see cref="CouldNotEdit"/>.
/// </remarks>
public static class TableEdit
{
    /// <summary>The refusal of a cell text with a line break.</summary>
    public const string OneLine = "A table cell is one line";

    /// <summary>The refusal of deleting the header row.</summary>
    public const string HeaderCannotBeDeleted = "The header row cannot be deleted";

    /// <summary>The refusal of duplicating the header row, which would give the table two headers.</summary>
    public const string HeaderCannotBeDuplicated = "The header row cannot be duplicated";

    /// <summary>The refusal of moving the header row, or a row onto it, and of inserting a row before it.</summary>
    public const string HeaderStaysFirst = "The header row stays first";

    /// <summary>The refusal of moving a column or a row past the first.</summary>
    public const string AlreadyFirst = "Already first";

    /// <summary>The refusal of moving a column or a row past the last.</summary>
    public const string AlreadyLast = "Already last";

    /// <summary>The refusal of deleting the only column.</summary>
    public const string LastColumnCannotBeDeleted = "The last column cannot be deleted";

    /// <summary>The refusal of an edit whose result does not reparse as the intended table.</summary>
    public const string CouldNotEdit = "Could not edit the table safely";

    /// <summary>Sets a cell's text. Leading and trailing spaces are dropped; each <c>|</c> is written <c>\|</c>.</summary>
    /// <param name="text">The document's source text.</param>
    /// <param name="table">The table block.</param>
    /// <param name="row">The row, 0 for the header.</param>
    /// <param name="column">The column.</param>
    /// <param name="value">The text the cell shows, as the inline field has it.</param>
    public static TableEditResult SetCell(string text, RenderBlock table, int row, int column, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        CheckRow(table, row);
        CheckColumn(table, column);
        if (value.Contains('\n') || value.Contains('\r'))
        {
            return TableEditResult.Refuse(OneLine);
        }

        string written = value.Trim(' ', '\t').Replace("|", "\\|", StringComparison.Ordinal);
        return Commit(text, table, grid => grid.SetCell(row, column, written), row, column);
    }

    /// <summary>Inserts an empty body row, so that it takes row index <paramref name="at"/>.</summary>
    /// <param name="text">The document's source text.</param>
    /// <param name="table">The table block.</param>
    /// <param name="at">The index the new row takes: 1 (before the first body row) to the number of rows (after the last).</param>
    public static TableEditResult InsertRow(string text, RenderBlock table, int at)
    {
        int rows = ModelOf(table).Rows.Length;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(at, rows);
        ArgumentOutOfRangeException.ThrowIfNegative(at);
        if (at == 0)
        {
            return TableEditResult.Refuse(HeaderStaysFirst);
        }

        return Commit(text, table, grid => grid.InsertRow(at), at, 0);
    }

    /// <summary>Deletes a body row.</summary>
    /// <param name="text">The document's source text.</param>
    /// <param name="table">The table block.</param>
    /// <param name="row">The row.</param>
    public static TableEditResult DeleteRow(string text, RenderBlock table, int row)
    {
        CheckRow(table, row);
        if (row == 0)
        {
            return TableEditResult.Refuse(HeaderCannotBeDeleted);
        }

        int target = Math.Min(row, ModelOf(table).Rows.Length - 2);
        return Commit(text, table, grid => grid.DeleteRow(row), target, 0);
    }

    /// <summary>Inserts a copy of a body row after it.</summary>
    /// <param name="text">The document's source text.</param>
    /// <param name="table">The table block.</param>
    /// <param name="row">The row.</param>
    public static TableEditResult DuplicateRow(string text, RenderBlock table, int row)
    {
        CheckRow(table, row);
        if (row == 0)
        {
            return TableEditResult.Refuse(HeaderCannotBeDuplicated);
        }

        return Commit(text, table, grid => grid.DuplicateRow(row), row + 1, 0);
    }

    /// <summary>Moves a body row by <paramref name="delta"/> rows; the others shift.</summary>
    /// <param name="text">The document's source text.</param>
    /// <param name="table">The table block.</param>
    /// <param name="row">The row.</param>
    /// <param name="delta">The number of rows to move by; negative moves up.</param>
    public static TableEditResult MoveRow(string text, RenderBlock table, int row, int delta)
    {
        CheckRow(table, row);
        if (row == 0)
        {
            return TableEditResult.Refuse(HeaderStaysFirst);
        }

        int target = row + delta;
        if (target < 1)
        {
            return TableEditResult.Refuse(HeaderStaysFirst);
        }

        if (target >= ModelOf(table).Rows.Length)
        {
            return TableEditResult.Refuse(AlreadyLast);
        }

        return Commit(text, table, grid => grid.MoveRow(row, target), target, 0);
    }

    /// <summary>Inserts an empty column, so that it takes column index <paramref name="at"/>.</summary>
    /// <param name="text">The document's source text.</param>
    /// <param name="table">The table block.</param>
    /// <param name="at">The index the new column takes: 0 (before the first) to the number of columns (after the last).</param>
    public static TableEditResult InsertColumn(string text, RenderBlock table, int at)
    {
        int columns = ModelOf(table).Alignments.Length;
        ArgumentOutOfRangeException.ThrowIfNegative(at);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(at, columns);
        return Commit(text, table, grid => grid.InsertColumn(at), 0, at);
    }

    /// <summary>Deletes a column from every row.</summary>
    /// <param name="text">The document's source text.</param>
    /// <param name="table">The table block.</param>
    /// <param name="column">The column.</param>
    public static TableEditResult DeleteColumn(string text, RenderBlock table, int column)
    {
        CheckColumn(table, column);
        int columns = ModelOf(table).Alignments.Length;
        if (columns == 1)
        {
            return TableEditResult.Refuse(LastColumnCannotBeDeleted);
        }

        return Commit(text, table, grid => grid.DeleteColumn(column), 0, Math.Min(column, columns - 2));
    }

    /// <summary>Moves a column by <paramref name="delta"/> columns in every row; the others shift.</summary>
    /// <param name="text">The document's source text.</param>
    /// <param name="table">The table block.</param>
    /// <param name="column">The column.</param>
    /// <param name="delta">The number of columns to move by; negative moves left.</param>
    public static TableEditResult MoveColumn(string text, RenderBlock table, int column, int delta)
    {
        CheckColumn(table, column);
        int target = column + delta;
        if (target < 0)
        {
            return TableEditResult.Refuse(AlreadyFirst);
        }

        if (target >= ModelOf(table).Alignments.Length)
        {
            return TableEditResult.Refuse(AlreadyLast);
        }

        return Commit(text, table, grid => grid.MoveColumn(column, target), 0, target);
    }

    /// <summary>Sets a column's alignment: its delimiter cell becomes <c>:--</c>, <c>:-:</c>, <c>--:</c>, or <c>---</c> for none.</summary>
    /// <param name="text">The document's source text.</param>
    /// <param name="table">The table block.</param>
    /// <param name="column">The column.</param>
    /// <param name="alignment">The alignment, or null for none.</param>
    public static TableEditResult SetAlignment(string text, RenderBlock table, int column, ColumnAlignment? alignment)
    {
        CheckColumn(table, column);
        return Commit(text, table, grid => grid.SetAlignment(column, alignment), 0, column);
    }

    /// <summary>
    /// Applies a change to the parsed table and returns its replacement, or a refusal when the result does not reparse as the
    /// intended table.
    /// </summary>
    private static TableEditResult Commit(string text, RenderBlock table, Func<TableGrid, TableGrid> change, int row, int column)
    {
        ArgumentNullException.ThrowIfNull(text);
        TableSource source = TableSource.Parse(text, table);
        TableGrid edited = change(TableGrid.From(source, table));
        (int start, int length, string replacement) = TableWriter.Splice(text, source, edited);

        string result = string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(start + length));
        if (!TableVerifier.Verify(result, table, edited))
        {
            return TableEditResult.Refuse(CouldNotEdit);
        }

        return new TableEditResult(start, length, replacement, row, column);
    }

    /// <summary>The table model of a block, which must be a table.</summary>
    private static TableModel ModelOf(RenderBlock table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.Table ?? throw new ArgumentException("The block is not a table.", nameof(table));
    }

    /// <summary>Checks that <paramref name="row"/> is a row of the table, the header included.</summary>
    private static void CheckRow(RenderBlock table, int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, ModelOf(table).Rows.Length);
    }

    /// <summary>Checks that <paramref name="column"/> is a column of the table.</summary>
    private static void CheckColumn(RenderBlock table, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, ModelOf(table).Alignments.Length);
    }
}
