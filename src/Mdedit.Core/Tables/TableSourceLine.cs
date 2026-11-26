using System.Collections.Immutable;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tables;

/// <summary>
/// One line of a pipe table as written: a header or body row, or the delimiter row. Offsets are absolute in the document.
/// </summary>
/// <param name="Line">The span of the line, without its line ending.</param>
/// <param name="ContentStart">
/// The offset of the row's first character, after its container prefix (<c>&gt; </c>, list indentation). For the delimiter row,
/// the first character that is neither a space, a tab nor a quote marker.
/// </param>
/// <param name="Newline">The line ending after the line (<c>\n</c>, <c>\r\n</c> or <c>\r</c>), or empty when the text ends there.</param>
/// <param name="LeadingPipe">Whether the row starts with a pipe.</param>
/// <param name="TrailingPipe">Whether the row ends with a pipe, not counting the spaces after it.</param>
/// <param name="Pipes">The offsets of the unescaped pipes, in order: the column separators.</param>
/// <param name="Cells">
/// The span of each cell: the text between two pipes, padding included. The empty segment before a leading pipe and the
/// segment after a trailing pipe are not cells.
/// </param>
/// <param name="Contents">The span of each cell's content, without its padding; empty, at the cell's end, when the cell has none.</param>
public sealed record TableSourceLine(
    SourceRange Line,
    int ContentStart,
    string Newline,
    bool LeadingPipe,
    bool TrailingPipe,
    ImmutableArray<int> Pipes,
    ImmutableArray<SourceRange> Cells,
    ImmutableArray<SourceRange> Contents)
{
    /// <summary>The container prefix: the text from the start of the line to the row's first character.</summary>
    public SourceRange Prefix => new(Line.Start, ContentStart);

    /// <summary>The text after the trailing pipe (its trailing spaces). Empty when the row has no trailing pipe.</summary>
    public SourceRange Suffix => TrailingPipe ? new SourceRange(Pipes[^1] + 1, Line.End) : new SourceRange(Line.End, Line.End);
}
