using System.Text;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tables;

/// <summary>
/// Checks an edited table by reparsing the edited document, as cfgedit checks its edits: the table that starts where the original
/// did must have the intended rows, columns and alignments, and every cell must show the intended plain text.
/// </summary>
internal static class TableVerifier
{
    /// <summary>
    /// Whether the edited document holds the table the grid describes: its rows, columns and alignments, the plain text of every cell, and
    /// on each row the edit wrote, the header's number of cells as written (see <see cref="WrittenRowsHaveColumns"/>).
    /// </summary>
    /// <param name="text">The edited document's source text.</param>
    /// <param name="original">The table block before the edit; its start is where the edited table must start.</param>
    /// <param name="expected">The table after the edit.</param>
    /// <param name="before">The table's grid before the edit, which tells the rows the edit wrote. Null treats every row as written.</param>
    public static bool Verify(string text, RenderBlock original, TableGrid expected, TableGrid? before = null)
    {
        RenderDocument document = RenderDocument.Parse(text);
        RenderBlock? block = document.Blocks.FirstOrDefault(candidate => candidate.Kind == BlockKind.Table && candidate.Start == original.Start);
        if (block?.Table is not { } table || table.Rows.Length != expected.Rows.Length)
        {
            return false;
        }

        ColumnAlignment[] alignments = expected.Alignments.Select(alignment => alignment ?? ColumnAlignment.Left).ToArray();
        if (!table.Alignments.SequenceEqual(alignments))
        {
            return false;
        }

        for (int row = 0; row < table.Rows.Length; row++)
        {
            for (int column = 0; column < expected.Columns; column++)
            {
                string? intended = IntendedPlain(expected.Rows[row], column, document.ReferenceKey);
                if (intended is null || intended != TableText.PlainOf(table.Rows[row].Cells[column]))
                {
                    return false;
                }
            }
        }

        return WrittenRowsHaveColumns(text, block, expected, before);
    }

    /// <summary>
    /// Whether every row the edit wrote has exactly the header's number of cells in the source as written. GFM drops the cells past the
    /// header's count, and a cell whose pipe is not escaped splits a row, so a row that writes more cells loses content from view. A row
    /// is written when it differs from its row before the edit (every row counts when <paramref name="before"/> is null).
    /// </summary>
    /// <param name="text">The edited document's source text.</param>
    /// <param name="block">The edited table's block, in the reparsed document.</param>
    /// <param name="expected">The table after the edit.</param>
    /// <param name="before">The table before the edit, or null.</param>
    private static bool WrittenRowsHaveColumns(string text, RenderBlock block, TableGrid expected, TableGrid? before)
    {
        TableSource source = TableSource.Parse(text, block);
        for (int row = 0; row < expected.Rows.Length; row++)
        {
            if (before is not null && row < before.Rows.Length && SameRow(before.Rows[row], expected.Rows[row]))
            {
                continue;
            }

            if (source.Row(row).Cells.Length != expected.Columns)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether two rows of a grid are written the same: the prefix, the pipes and the cells' raw text.</summary>
    private static bool SameRow(GridRow a, GridRow b) =>
        a.Prefix == b.Prefix
        && a.LeadingPipe == b.LeadingPipe
        && a.TrailingPipe == b.TrailingPipe
        && a.Suffix == b.Suffix
        && a.Cells.Select(cell => cell.Raw).SequenceEqual(b.Cells.Select(cell => cell.Raw));

    /// <summary>
    /// The plain text a cell must show: the one the document gave it, or for a cell an edit wrote, the plain text of its content
    /// parsed on its own. Null when that content is not one cell (for example it holds an unescaped pipe).
    /// </summary>
    /// <param name="row">The row of the grid.</param>
    /// <param name="column">The column.</param>
    /// <param name="referenceKey">The document's reference labels (<see cref="RenderDocument.ReferenceKey"/>).</param>
    private static string? IntendedPlain(GridRow row, int column, string referenceKey)
    {
        if (column >= row.Cells.Length)
        {
            return string.Empty;
        }

        GridCell cell = row.Cells[column];
        return cell.Plain ?? PlainOfContent(cell.Content, referenceKey);
    }

    /// <summary>
    /// The plain text of a cell's content, parsed as the only cell of a one-column table; null when it is not one cell. The
    /// document's link reference labels are defined in the parse (with a dummy destination: the text of a link does not depend on
    /// it), so that a cell such as <c>[x]</c> reads as the document reads it.
    /// </summary>
    private static string? PlainOfContent(string content, string referenceKey)
    {
        var source = new StringBuilder("| " + content + " |\n| - |\n\n");
        foreach (string label in referenceKey.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            source.Append('[').Append(label).Append("]: #\n");
        }

        RenderDocument document = RenderDocument.Parse(source.ToString());
        RenderBlock[] tables = document.Blocks.Where(block => block.Kind == BlockKind.Table).ToArray();
        if (tables.Length != 1 || tables[0].Table is not { } table || table.Alignments.Length != 1)
        {
            return null;
        }

        return TableText.PlainOf(table.Rows[0].Cells[0]);
    }
}
