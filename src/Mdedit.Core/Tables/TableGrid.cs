using System.Collections.Immutable;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tables;

/// <summary>
/// A cell of a <see cref="TableGrid"/>: its text between the pipes as written, and the plain text the document shows for it.
/// </summary>
/// <param name="Raw">The text between the pipes, padding included.</param>
/// <param name="Plain">
/// The cell's plain text as the document renders it, or null when an edit wrote the cell (its plain text is then checked
/// against the text that was written).
/// </param>
internal sealed record GridCell(string Raw, string? Plain)
{
    /// <summary>The source content: <see cref="Raw"/> without its spaces and tabs at either end.</summary>
    public string Content => Raw.Trim(' ', '\t');
}

/// <summary>
/// A line of a table being edited: the container prefix, the pipe style, the cells and the line ending. The cells are as many as
/// the line has; a row is normalised to the table's column count only when an operation touches it.
/// </summary>
/// <param name="Prefix">The text before the row's first character (quote markers, list indentation).</param>
/// <param name="LeadingPipe">Whether the row starts with a pipe.</param>
/// <param name="TrailingPipe">Whether the row ends with a pipe.</param>
/// <param name="Suffix">The spaces after the trailing pipe; empty without one.</param>
/// <param name="Newline">
/// The line ending after the line in the source. The writer does not write the last line's ending; a line an edit added has none,
/// and the table's line ending is used for it.
/// </param>
/// <param name="Cells">The cells, in column order.</param>
internal sealed record GridRow(string Prefix, bool LeadingPipe, bool TrailingPipe, string Suffix, string Newline, ImmutableArray<GridCell> Cells)
{
    /// <summary>
    /// Whether the first cell has no content. Without a leading pipe, such a row would read as having one (a pipe is the first
    /// character of the row), so the writer adds one.
    /// </summary>
    public bool FirstCellEmpty => Cells.Length > 0 && Cells[0].Content.Length == 0;

    /// <summary>
    /// Whether the last cell has no content. Without a trailing pipe, the row would lose the cell (a pipe after it is what makes
    /// it a cell), so the writer adds one.
    /// </summary>
    public bool LastCellEmpty => Cells.Length > 0 && Cells[^1].Content.Length == 0;
}

/// <summary>
/// The table being edited, as a value: the column alignments (null for none, the delimiter <c>---</c>), the rows (header first)
/// and the delimiter row. Each operation returns a new grid; the grid holds no text offsets, which the writers compute from the
/// source the grid was made from.
/// </summary>
/// <param name="Alignments">The alignment of each column, null for none.</param>
/// <param name="Rows">The header row, then the body rows.</param>
/// <param name="Delimiter">The delimiter row.</param>
internal sealed record TableGrid(ImmutableArray<ColumnAlignment?> Alignments, ImmutableArray<GridRow> Rows, GridRow Delimiter)
{
    /// <summary>The blank cell of a new row or column: two spaces, as <c>|  |</c> shows.</summary>
    private static readonly GridCell BlankCell = new("  ", string.Empty);

    /// <summary>The delimiter cell of a new column: <c>---</c>, which is no alignment.</summary>
    private static readonly GridCell NewDelimiterCell = new(" --- ", null);

    /// <summary>The number of columns.</summary>
    public int Columns => Alignments.Length;

    /// <summary>The lines in source order: the header, the delimiter, then the body rows.</summary>
    public ImmutableArray<GridRow> Lines => Rows.Take(1).Append(Delimiter).Concat(Rows.Skip(1)).ToImmutableArray();

    /// <summary>Makes the grid of a parsed table: its alignments, rows and plain cell texts.</summary>
    /// <param name="source">The table as written.</param>
    /// <param name="table">The table block the source was parsed from.</param>
    public static TableGrid From(TableSource source, RenderBlock table)
    {
        TableModel model = table.Table ?? throw new ArgumentException("The block is not a table.", nameof(table));
        int columns = model.Alignments.Length;
        ImmutableArray<ColumnAlignment?> alignments = Enumerable.Range(0, columns)
            .Select(column => AlignmentOf(column < source.Delimiter.Contents.Length ? source.Delimiter.Contents[column].Slice(source.Text) : string.Empty))
            .ToImmutableArray();

        ImmutableArray<GridRow> rows = Enumerable.Range(0, source.RowCount)
            .Select(row => RowOf(source, source.Row(row), model.Rows[row], columns))
            .ToImmutableArray();
        GridRow delimiter = Of(source, source.Delimiter, _ => null);
        return new TableGrid(alignments, rows, delimiter);
    }

    /// <summary>Sets a cell's written text. The row is normalised to the table's columns.</summary>
    /// <param name="row">The row, 0 for the header.</param>
    /// <param name="column">The column.</param>
    /// <param name="content">The cell's source content, with its pipes already escaped.</param>
    public TableGrid SetCell(int row, int column, string content)
    {
        GridRow target = Normalised(Rows[row], BlankCell);
        GridCell old = target.Cells[column];
        GridRow edited = target with { Cells = target.Cells.SetItem(column, new GridCell(Refill(old.Raw, content), null)) };
        return WithRows(Rows.SetItem(row, edited));
    }

    /// <summary>Inserts an empty body row before row <paramref name="at"/>, in the style of the row above it.</summary>
    /// <param name="at">The index the new row takes: 1 to the number of rows.</param>
    public TableGrid InsertRow(int at)
    {
        GridRow above = Rows[at - 1];
        GridRow blank = new(ContinuationPrefix(above.Prefix), above.LeadingPipe, above.TrailingPipe, string.Empty, string.Empty, BlankCells(Columns));
        return WithRows(Rows.Insert(at, blank));
    }

    /// <summary>Removes a body row.</summary>
    /// <param name="row">The row, 1 or more.</param>
    public TableGrid DeleteRow(int row) => WithRows(Rows.RemoveAt(row));

    /// <summary>Inserts a copy of a row after it.</summary>
    /// <param name="row">The row, 1 or more.</param>
    public TableGrid DuplicateRow(int row) => WithRows(Rows.Insert(row + 1, Rows[row] with { Newline = string.Empty }));

    /// <summary>Moves a body row to index <paramref name="target"/>, the others shifting.</summary>
    /// <param name="row">The row, 1 or more.</param>
    /// <param name="target">The index it takes, 1 or more.</param>
    public TableGrid MoveRow(int row, int target) => WithRows(Move(Rows, row, target));

    /// <summary>Inserts an empty column before <paramref name="at"/>. Every row is normalised to the table's columns.</summary>
    /// <param name="at">The index of the new column, 0 to the number of columns.</param>
    public TableGrid InsertColumn(int at) => MapColumns(
        alignments => alignments.Insert(at, null),
        cells => cells.Insert(at, BlankCell),
        cells => cells.Insert(at, NewDelimiterCell));

    /// <summary>Removes a column from every row. Every row is normalised to the table's columns first.</summary>
    /// <param name="column">The column.</param>
    public TableGrid DeleteColumn(int column) => MapColumns(
        alignments => alignments.RemoveAt(column),
        cells => cells.RemoveAt(column),
        cells => cells.RemoveAt(column));

    /// <summary>Moves a column to index <paramref name="target"/> in every row.</summary>
    /// <param name="column">The column.</param>
    /// <param name="target">The index the column takes.</param>
    public TableGrid MoveColumn(int column, int target) => MapColumns(
        alignments => Move(alignments, column, target),
        cells => Move(cells, column, target),
        cells => Move(cells, column, target));

    /// <summary>Sets the alignment of a column: its delimiter cell gets the alignment's colons, and its dashes are kept where possible.</summary>
    /// <param name="column">The column.</param>
    /// <param name="alignment">The alignment, or null for none.</param>
    public TableGrid SetAlignment(int column, ColumnAlignment? alignment)
    {
        GridRow delimiter = Normalised(Delimiter, NewDelimiterCell);
        GridCell old = delimiter.Cells[column];
        int dashes = Math.Max(1, old.Content.Count(c => c == '-'));
        GridCell cell = new(Refill(old.Raw, Delimiters.Text(alignment, dashes)), null);
        return new TableGrid(Alignments.SetItem(column, alignment), Rows, delimiter with { Cells = delimiter.Cells.SetItem(column, cell) });
    }

    /// <summary>Applies a change to one column of every row, the delimiter included, after normalising the rows.</summary>
    private TableGrid MapColumns(
        Func<ImmutableArray<ColumnAlignment?>, ImmutableArray<ColumnAlignment?>> alignments,
        Func<ImmutableArray<GridCell>, ImmutableArray<GridCell>> cells,
        Func<ImmutableArray<GridCell>, ImmutableArray<GridCell>> delimiterCells)
    {
        ImmutableArray<GridRow> rows = Rows.Select(row => WithCells(Normalised(row, BlankCell), cells)).ToImmutableArray();
        GridRow delimiter = WithCells(Normalised(Delimiter, NewDelimiterCell), delimiterCells);
        return new TableGrid(alignments(Alignments), rows, delimiter);
    }

    /// <summary>The row with its cells changed by <paramref name="change"/>.</summary>
    private static GridRow WithCells(GridRow row, Func<ImmutableArray<GridCell>, ImmutableArray<GridCell>> change) =>
        row with { Cells = change(row.Cells) };

    /// <summary>A row with exactly <see cref="Columns"/> cells: extra cells are dropped and missing ones are <paramref name="pad"/>.</summary>
    private GridRow Normalised(GridRow row, GridCell pad)
    {
        ImmutableArray<GridCell> cells = row.Cells.Length >= Columns
            ? row.Cells.Take(Columns).ToImmutableArray()
            : row.Cells.AddRange(Enumerable.Repeat(pad, Columns - row.Cells.Length));
        return row with { Cells = cells };
    }

    /// <summary>The grid with its rows replaced.</summary>
    private TableGrid WithRows(ImmutableArray<GridRow> rows) => this with { Rows = rows };

    /// <summary>A source row as a grid row; <paramref name="model"/> gives the plain text of its cells.</summary>
    private static GridRow RowOf(TableSource source, TableSourceLine line, TableRowModel model, int columns) =>
        Of(source, line, column => column < columns ? TableText.PlainOf(model.Cells[column]) : null);

    /// <summary>A source line as a grid row, with the plain text of each cell from <paramref name="plainOf"/>.</summary>
    private static GridRow Of(TableSource source, TableSourceLine line, Func<int, string?> plainOf)
    {
        ImmutableArray<GridCell> cells = line.Cells
            .Select((cell, column) => new GridCell(cell.Slice(source.Text), plainOf(column)))
            .ToImmutableArray();
        return new GridRow(line.Prefix.Slice(source.Text), line.LeadingPipe, line.TrailingPipe, line.Suffix.Slice(source.Text), line.Newline, cells);
    }

    /// <summary>The alignment a delimiter cell's content gives: colons at its ends, or none.</summary>
    private static ColumnAlignment? AlignmentOf(string delimiter)
    {
        bool left = delimiter.Length > 0 && delimiter[0] == ':';
        bool right = delimiter.Length > 1 && delimiter[^1] == ':';
        return (left, right) switch
        {
            (true, true) => ColumnAlignment.Center,
            (false, true) => ColumnAlignment.Right,
            (true, false) => ColumnAlignment.Left,
            _ => null,
        };
    }

    /// <summary>
    /// The raw text for a cell written with <paramref name="content"/>: the spaces it had at either end are kept, and a cell with
    /// no content gets one space each side.
    /// </summary>
    private static string Refill(string raw, string content)
    {
        string trimmed = raw.Trim(' ', '\t');
        if (trimmed.Length == 0)
        {
            return content.Length == 0 ? raw : " " + content + " ";
        }

        int lead = raw.Length - raw.TrimStart(' ', '\t').Length;
        int trail = raw.Length - raw.TrimEnd(' ', '\t').Length;
        return raw[..lead] + content + raw[(raw.Length - trail)..];
    }

    /// <summary>Moves the item at <paramref name="from"/> to <paramref name="to"/>, the items between them shifting.</summary>
    private static ImmutableArray<T> Move<T>(ImmutableArray<T> items, int from, int to)
    {
        var list = items.ToList();
        T item = list[from];
        list.RemoveAt(from);
        list.Insert(to, item);
        return list.ToImmutableArray();
    }

    /// <summary>
    /// The prefix of a row that continues a block, from the prefix of the row above: its quote markers are kept, and its other
    /// characters (a list marker, say) become spaces, so that the new row is not a new list item.
    /// </summary>
    private static string ContinuationPrefix(string prefix) => new(prefix.Select(c => c == '>' ? '>' : ' ').ToArray());

    /// <summary>The blank cells of a new row of <paramref name="count"/> columns.</summary>
    private static ImmutableArray<GridCell> BlankCells(int count) => Enumerable.Repeat(BlankCell, count).ToImmutableArray();
}
