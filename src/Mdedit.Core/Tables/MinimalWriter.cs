using System.Collections.Immutable;

namespace Mdedit.Core.Tables;

/// <summary>
/// Renders the lines of a table that is not aligned in the source: each line is its prefix, its pipes and its cells' text as
/// written, so a line no edit touched comes back byte for byte. A new row or column adds its cells at their place.
/// </summary>
/// <remarks>
/// A row without a leading pipe whose first cell is empty gets one, and a row without a trailing pipe whose last cell is empty
/// gets one: the pipe is written directly after the cell's text, so the cell keeps its padding (<c>1 | </c> becomes <c>1 | |</c>,
/// and a blank cell <c>  </c> becomes <c>  |</c>). Without it, the empty cell would not be read as a cell.
/// </remarks>
internal static class MinimalWriter
{
    /// <summary>The lines of the grid in source order, each with its line ending.</summary>
    /// <param name="grid">The table.</param>
    public static ImmutableArray<(string Text, string Newline)> Lines(TableGrid grid) =>
        grid.Lines.Select(line => (Text(line), line.Newline)).ToImmutableArray();

    /// <summary>A line: its prefix, its pipes and its cells, each cell's raw text between them.</summary>
    private static string Text(GridRow row)
    {
        string cells = string.Join('|', row.Cells.Select(cell => cell.Raw));
        bool leading = row.LeadingPipe || row.FirstCellEmpty;
        bool trailing = row.TrailingPipe || row.LastCellEmpty;
        return row.Prefix + (leading ? "|" : string.Empty) + cells + (trailing ? "|" : string.Empty) + row.Suffix;
    }
}
