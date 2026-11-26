using System.Collections.Immutable;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Mdedit.Core.Render;
using Shared.Core;

namespace Mdedit.Core.Tables;

/// <summary>
/// Builds a <see cref="TableSource"/> from the text of a table block. Markdig's table rows give where each row's first character
/// is (after the container prefix); the pipes, cells and contents are found by scanning each line, because Markdig's cell spans
/// include the padding and drop the escapes.
/// </summary>
internal static class TableSourceScanner
{
    /// <summary>Scans the table block <paramref name="table"/> of <paramref name="text"/>.</summary>
    public static TableSource Scan(string text, RenderBlock table)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(table);
        if (table.Table is null)
        {
            throw new ArgumentException("The block is not a table.", nameof(table));
        }

        List<TableRow> rows = FindTable(text, table.Start).OfType<TableRow>().ToList();
        if (rows.Count != table.Table.Rows.Length)
        {
            throw new InvalidOperationException("The table block does not match its source.");
        }

        TableSourceLine header = ScanRow(text, rows[0]);
        int delimiterStart = header.Line.End + header.Newline.Length;
        TableSourceLine delimiter = ScanLine(text, delimiterStart, DelimiterContentStart(text, delimiterStart));
        ImmutableArray<TableSourceLine> body = rows.Skip(1).Select(row => ScanRow(text, row)).ToImmutableArray();

        var lines = new List<TableSourceLine> { header, delimiter };
        lines.AddRange(body);
        bool aligned = AllPipesAligned(text, lines);
        return new TableSource(text, header, delimiter, body, aligned);
    }

    /// <summary>The table that starts at <paramref name="start"/>, found by parsing the whole document.</summary>
    private static Table FindTable(string text, int start)
    {
        return MdPipeline.Parse(text).Descendants<Table>().FirstOrDefault(table => table.Span.Start == start)
            ?? throw new InvalidOperationException("No table starts at the block's offset.");
    }

    /// <summary>Scans the line of a Markdig table row.</summary>
    private static TableSourceLine ScanRow(string text, TableRow row)
    {
        int lineStart = LineStartOf(text, row.Span.Start);
        return ScanLine(text, lineStart, RowContentStart(text, lineStart, row.Span.Start));
    }

    /// <summary>
    /// The first character of a row, given Markdig's row start. Markdig starts a row after its leading pipe, so a pipe before the
    /// start (after its spaces) is the row's first character.
    /// </summary>
    private static int RowContentStart(string text, int lineStart, int markdigStart)
    {
        int index = markdigStart - 1;
        while (index >= lineStart && IsBlank(text[index]))
        {
            index--;
        }

        return index >= lineStart && text[index] == '|' ? index : markdigStart;
    }

    /// <summary>Scans one line: its pipes, its cells and their contents.</summary>
    /// <param name="text">The source text.</param>
    /// <param name="lineStart">The start of the line.</param>
    /// <param name="contentStart">The first character of the row, after its prefix.</param>
    private static TableSourceLine ScanLine(string text, int lineStart, int contentStart)
    {
        int lineEnd = LineEndOf(text, contentStart);
        ImmutableArray<int> pipes = UnescapedPipes(text, contentStart, lineEnd);
        int first = SkipBlanks(text, contentStart, lineEnd);
        int last = LastNonBlank(text, contentStart, lineEnd);
        bool leading = pipes.Length > 0 && pipes[0] == first;
        bool trailing = pipes.Length > (leading ? 1 : 0) && pipes[^1] == last;

        ImmutableArray<SourceRange> segments = Segments(contentStart, lineEnd, pipes);
        int from = leading ? 1 : 0;
        int to = segments.Length - (trailing ? 1 : 0);
        ImmutableArray<SourceRange> cells = segments.Skip(from).Take(to - from).ToImmutableArray();
        ImmutableArray<SourceRange> contents = cells.Select(cell => Trimmed(text, cell)).ToImmutableArray();

        return new TableSourceLine(
            new SourceRange(lineStart, lineEnd),
            contentStart,
            NewlineAt(text, lineEnd),
            leading,
            trailing,
            pipes,
            cells,
            contents);
    }

    /// <summary>The first character of the delimiter row: after the spaces, tabs and quote markers of its prefix.</summary>
    private static int DelimiterContentStart(string text, int lineStart)
    {
        int index = lineStart;
        while (index < text.Length && text[index] is ' ' or '\t' or '>')
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// The pipes of a row that are not escaped: a pipe preceded by an odd number of backslashes is escaped. The pipes split the
    /// row into cells, code spans included.
    /// </summary>
    private static ImmutableArray<int> UnescapedPipes(string text, int start, int end)
    {
        var pipes = ImmutableArray.CreateBuilder<int>();
        int backslashes = 0;
        for (int index = start; index < end; index++)
        {
            char c = text[index];
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '|' && backslashes % 2 == 0)
            {
                pipes.Add(index);
            }

            backslashes = 0;
        }

        return pipes.ToImmutable();
    }

    /// <summary>The text between the row's start and each pipe, and the text after the last pipe.</summary>
    private static ImmutableArray<SourceRange> Segments(int contentStart, int lineEnd, ImmutableArray<int> pipes)
    {
        var segments = ImmutableArray.CreateBuilder<SourceRange>(pipes.Length + 1);
        int start = contentStart;
        foreach (int pipe in pipes)
        {
            segments.Add(new SourceRange(start, pipe));
            start = pipe + 1;
        }

        segments.Add(new SourceRange(start, lineEnd));
        return segments.ToImmutable();
    }

    /// <summary>The range without its leading and trailing spaces and tabs. A range with no content is empty, at its end.</summary>
    private static SourceRange Trimmed(string text, SourceRange range)
    {
        int start = range.Start;
        int end = range.End;
        while (start < end && IsBlank(text[start]))
        {
            start++;
        }

        while (end > start && IsBlank(text[end - 1]))
        {
            end--;
        }

        return start == end ? new SourceRange(range.End, range.End) : new SourceRange(start, end);
    }

    /// <summary>
    /// Whether every line has its pipes at the same display columns, counted from the start of the line (so the prefix counts).
    /// A table with no pipes at all is aligned.
    /// </summary>
    private static bool AllPipesAligned(string text, List<TableSourceLine> lines)
    {
        int[] reference = PipeColumns(text, lines[0]);
        return lines.Skip(1).All(line => PipeColumns(text, line).SequenceEqual(reference));
    }

    /// <summary>The display column of each pipe of a line, from the start of the line.</summary>
    private static int[] PipeColumns(string text, TableSourceLine line)
    {
        var columns = new int[line.Pipes.Length];
        for (int index = 0; index < columns.Length; index++)
        {
            columns[index] = DisplayWidth.Of(text.AsSpan(line.Line.Start, line.Pipes[index] - line.Line.Start));
        }

        return columns;
    }

    /// <summary>The offset of the first character of the line that contains <paramref name="position"/>.</summary>
    private static int LineStartOf(string text, int position)
    {
        int index = position;
        while (index > 0 && text[index - 1] is not ('\n' or '\r'))
        {
            index--;
        }

        return index;
    }

    /// <summary>The offset of the line ending that closes the line that contains <paramref name="position"/>, or the text's end.</summary>
    private static int LineEndOf(string text, int position)
    {
        int index = position;
        while (index < text.Length && text[index] is not ('\n' or '\r'))
        {
            index++;
        }

        return index;
    }

    /// <summary>The line ending at <paramref name="index"/>: <c>\r\n</c>, <c>\n</c> or <c>\r</c>; empty at the text's end.</summary>
    private static string NewlineAt(string text, int index)
    {
        if (index >= text.Length)
        {
            return string.Empty;
        }

        return text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : text[index].ToString();
    }

    /// <summary>The first index in [<paramref name="start"/>, <paramref name="end"/>) that is not a space or tab, or <paramref name="end"/>.</summary>
    private static int SkipBlanks(string text, int start, int end)
    {
        int index = start;
        while (index < end && IsBlank(text[index]))
        {
            index++;
        }

        return index;
    }

    /// <summary>The index of the last character in [<paramref name="start"/>, <paramref name="end"/>) that is not a space or tab, or -1.</summary>
    private static int LastNonBlank(string text, int start, int end)
    {
        int index = end - 1;
        while (index >= start && IsBlank(text[index]))
        {
            index--;
        }

        return index;
    }

    /// <summary>Whether <paramref name="c"/> is a space or a tab.</summary>
    private static bool IsBlank(char c) => c is ' ' or '\t';
}
