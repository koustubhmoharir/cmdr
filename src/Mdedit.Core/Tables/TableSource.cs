using System.Collections.Immutable;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tables;

/// <summary>
/// A pipe table as written in the source: its header, delimiter and body lines, with their pipes and cells. The model is
/// immutable and describes the text, not the rendering: a row with fewer or more cells than the header is kept as written.
/// </summary>
/// <param name="Text">The source text the offsets refer to.</param>
/// <param name="Header">The header row's line.</param>
/// <param name="Delimiter">The delimiter row's line (the line after the header).</param>
/// <param name="Body">The body rows' lines, in source order.</param>
/// <param name="IsAligned">
/// Whether every line (header, delimiter and body) has its pipes at the same display columns, so that the table reads as a grid
/// in the source.
/// </param>
public sealed record TableSource(string Text, TableSourceLine Header, TableSourceLine Delimiter, ImmutableArray<TableSourceLine> Body, bool IsAligned)
{
    /// <summary>The number of rows the table has, header included; the delimiter row is not one of them.</summary>
    public int RowCount => 1 + Body.Length;

    /// <summary>The line ending of the table: the header's, which is the same as the document's line endings in a well-formed file.</summary>
    public string NewLine => Header.Newline;

    /// <summary>The span of the table's lines, from the start of the header line to the end of the last line, without its line ending.</summary>
    public SourceRange Span => new(Header.Line.Start, (Body.IsEmpty ? Delimiter : Body[^1]).Line.End);

    /// <summary>The line of a row: 0 is the header, <c>n</c> the body row <c>n</c> - 1.</summary>
    /// <param name="row">The row, 0 to <see cref="RowCount"/> - 1.</param>
    public TableSourceLine Row(int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, RowCount);
        return row == 0 ? Header : Body[row - 1];
    }

    /// <summary>
    /// The text of a cell as the inline field edits it: its source content, with each escaped pipe <c>\|</c> shown as <c>|</c>.
    /// Empty for a column the row does not have.
    /// </summary>
    /// <param name="row">The row, as in <see cref="Row"/>.</param>
    /// <param name="column">The column, from 0.</param>
    public string CellText(int row, int column)
    {
        TableSourceLine line = Row(row);
        if (column < 0 || column >= line.Contents.Length)
        {
            return string.Empty;
        }

        return line.Contents[column].Slice(Text).Replace("\\|", "|", StringComparison.Ordinal);
    }

    /// <summary>Parses the table block of a document.</summary>
    /// <param name="text">The document's source text.</param>
    /// <param name="table">A table block of the document, from <c>RenderDocument.Parse(text)</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="table"/> is not a table.</exception>
    public static TableSource Parse(string text, RenderBlock table) => TableSourceScanner.Scan(text, table);
}
