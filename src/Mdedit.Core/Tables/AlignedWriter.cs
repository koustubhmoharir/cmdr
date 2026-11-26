using System.Collections.Immutable;
using Mdedit.Core.Render;
using Shared.Core;

namespace Mdedit.Core.Tables;

/// <summary>
/// Renders a whole table aligned: each column as wide as its widest cell (at least 3, for the delimiter's dashes), one space of
/// padding on each side, the content justified by the column's alignment and the delimiter row as dashes with the colons. The
/// outer-pipe style is the header's, and each line keeps its prefix.
/// </summary>
/// <remarks>
/// When the first (or last) column is empty in some row, an empty end cell would not be read as a cell without its pipe, so every
/// line gets a leading (or trailing) pipe. This extends the header's style only when it has none, and keeps the table aligned.
/// </remarks>
internal static class AlignedWriter
{
    /// <summary>The narrowest column: the delimiter <c>:-:</c> needs three characters.</summary>
    private const int MinimumWidth = 3;

    /// <summary>The lines of the grid in source order, each with its line ending.</summary>
    /// <param name="grid">The table. Every row is written with exactly the grid's columns.</param>
    public static ImmutableArray<(string Text, string Newline)> Lines(TableGrid grid)
    {
        int[] widths = ColumnWidths(grid);
        GridRow header = grid.Rows[0];
        bool leading = header.LeadingPipe || grid.Rows.Any(row => CellContent(row, 0).Length == 0);
        bool trailing = header.TrailingPipe || grid.Rows.Any(row => CellContent(row, grid.Columns - 1).Length == 0);
        return grid.Lines
            .Select(line => (Text: Line(grid, line, widths, (leading, trailing)), line.Newline))
            .ToImmutableArray();
    }

    /// <summary>The width of each column: the widest of its cells, and at least <see cref="MinimumWidth"/>.</summary>
    private static int[] ColumnWidths(TableGrid grid)
    {
        var widths = Enumerable.Repeat(MinimumWidth, grid.Columns).ToArray();
        foreach (GridRow row in grid.Rows)
        {
            for (int column = 0; column < Math.Min(row.Cells.Length, grid.Columns); column++)
            {
                widths[column] = Math.Max(widths[column], DisplayWidth.Of(row.Cells[column].Content));
            }
        }

        return widths;
    }

    /// <summary>One line: the delimiter row when <paramref name="line"/> is the grid's delimiter, otherwise a row.</summary>
    private static string Line(TableGrid grid, GridRow line, int[] widths, (bool Leading, bool Trailing) pipes)
    {
        string[] cells = Enumerable.Range(0, grid.Columns)
            .Select(column => ReferenceEquals(line, grid.Delimiter)
                ? " " + Delimiters.Text(grid.Alignments[column], widths[column] - Delimiters.Colons(grid.Alignments[column])) + " "
                : " " + Justified(CellContent(line, column), widths[column], grid.Alignments[column]) + " ")
            .ToArray();
        return Assemble(line.Prefix, pipes, cells, line.Suffix);
    }

    /// <summary>The content of a cell, or empty for a column the row does not have.</summary>
    private static string CellContent(GridRow row, int column) => column < row.Cells.Length ? row.Cells[column].Content : string.Empty;

    /// <summary>The content padded to <paramref name="width"/> and justified by the alignment (null is left).</summary>
    private static string Justified(string content, int width, ColumnAlignment? alignment)
    {
        int space = width - DisplayWidth.Of(content);
        return alignment switch
        {
            ColumnAlignment.Right => new string(' ', space) + content,
            ColumnAlignment.Center => new string(' ', space / 2) + content + new string(' ', space - space / 2),
            _ => content + new string(' ', space),
        };
    }

    /// <summary>
    /// The line from its padded cells, with the given pipes. Without a leading pipe the first cell loses its leading space, and
    /// without a trailing pipe the last loses its trailing space, so the separators stay in their columns.
    /// </summary>
    private static string Assemble(string prefix, (bool Leading, bool Trailing) pipes, string[] cells, string suffix)
    {
        string body = string.Join('|', cells);
        if (!pipes.Leading)
        {
            body = body[1..];
        }

        if (!pipes.Trailing)
        {
            body = body[..^1].TrimEnd(' ', '\t');
        }

        return prefix + (pipes.Leading ? "|" : string.Empty) + body + (pipes.Trailing ? "|" : string.Empty) + suffix;
    }
}
