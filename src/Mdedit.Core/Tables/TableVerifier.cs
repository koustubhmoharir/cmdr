using System.Text;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tables;

/// <summary>
/// Checks an edited table by reparsing the edited document, as cfgedit checks its edits: the table that starts where the original
/// did must have the intended rows, columns and alignments, and every cell must show the intended plain text.
/// </summary>
internal static class TableVerifier
{
    /// <summary>Whether the edited document holds the table the grid describes.</summary>
    /// <param name="text">The edited document's source text.</param>
    /// <param name="original">The table block before the edit; its start is where the edited table must start.</param>
    /// <param name="expected">The table after the edit.</param>
    public static bool Verify(string text, RenderBlock original, TableGrid expected)
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

        return true;
    }

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
