using System.Text;

namespace Mdedit.Core.Render;

/// <summary>
/// The Markdown of a source range: the source slice, with the delimiters of the inline containers it is cut from re-added and,
/// for a range that starts mid-line in a block with containers, the container prefix of that line. A range inside a table is
/// whole rows.
/// </summary>
internal static class MarkdownCopy
{
    /// <summary>The Markdown of the source range <c>[start, end)</c>, which is already clamped to the document.</summary>
    /// <param name="doc">The document.</param>
    /// <param name="start">The first offset of the range, already clamped.</param>
    /// <param name="end">The offset after the range, already clamped.</param>
    public static string Of(RenderDocument doc, int start, int end)
    {
        foreach (RenderBlock block in doc.Blocks)
        {
            if (block.Table is not null && block.Start <= start && end <= block.End)
            {
                return TableRows(doc, block, start, end);
            }
        }

        string text = doc.Text;
        var result = new StringBuilder();
        result.Append(ContainerPrefix(doc, start));

        var openers = new StringBuilder();
        var closers = new StringBuilder();
        foreach (RenderBlock block in doc.Blocks)
        {
            foreach (InlineSpan span in block.InlineSpans)
            {
                int outerStart = block.Start + span.OuterStart;
                int outerEnd = block.Start + span.OuterEnd;
                int innerStart = block.Start + span.InnerStart;
                int innerEnd = block.Start + span.InnerEnd;

                // A range that starts inside the container gets the part of its opener the range does not have, and a range that
                // ends inside it gets the rest of its closer. Openers go outermost first, closers innermost first.
                if (outerStart < start && start < innerEnd)
                {
                    int head = Math.Clamp(Math.Min(start, innerStart) - outerStart, 0, span.Opener.Length);
                    openers.Append(span.Opener.AsSpan(0, head));
                }

                if (innerStart < end && end < outerEnd)
                {
                    int tail = Math.Clamp(end - innerEnd, 0, span.Closer.Length);
                    closers.Insert(0, span.Closer.Substring(tail));
                }
            }
        }

        result.Append(openers);
        result.Append(text.AsSpan(start, end - start));
        result.Append(closers);
        return result.ToString();
    }

    /// <summary>
    /// The container prefix of the line a range starts on, when the range starts mid-line in a block with containers: the source
    /// from the line's start to the first mapped character of the block on that line (or to the start, when the range starts
    /// before that character, in the prefix itself). Empty otherwise.
    /// </summary>
    private static string ContainerPrefix(RenderDocument doc, int start)
    {
        RenderBlock? block = doc.Blocks.FirstOrDefault(candidate => candidate.Start <= start && start < candidate.End);
        if (block is null || block.Containers.IsEmpty)
        {
            return string.Empty;
        }

        int lineStart = LineStart(doc.Text, start);
        if (lineStart >= start)
        {
            return string.Empty;
        }

        int lineEnd = LineEnd(doc.Text, start);
        int contentStart = start;
        foreach (Run run in block.Runs)
        {
            foreach (Cell cell in run.Cells)
            {
                int offset = block.Start + cell.Offset;
                if (cell.IsMapped && offset >= lineStart && offset < lineEnd)
                {
                    contentStart = Math.Min(contentStart, offset);
                }
            }
        }

        return doc.Text[lineStart..contentStart];
    }

    /// <summary>
    /// The lines of a table that a range inside it covers, as written: its header, its delimiter row and every body row whose line
    /// the range touches, joined by line feeds.
    /// </summary>
    private static string TableRows(RenderDocument doc, RenderBlock block, int start, int end)
    {
        string text = doc.Text;
        int header = LineStart(text, block.Start);
        var lines = new List<string> { text[header..LineEnd(text, header)] };

        int delimiter = NextLineStart(text, LineEnd(text, header));
        lines.Add(text[delimiter..LineEnd(text, delimiter)]);

        TableModel table = block.Table!;
        for (int index = 1; index < table.Rows.Length; index++)
        {
            int lineStart = LineStart(text, block.Start + table.Rows[index].Start);
            int lineEnd = LineEnd(text, lineStart);
            if (lineStart < end && start <= lineEnd)
            {
                lines.Add(text[lineStart..lineEnd]);
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>The offset of the first character of the line that contains <paramref name="index"/>.</summary>
    private static int LineStart(string text, int index)
    {
        int line = index;
        while (line > 0 && text[line - 1] is not ('\n' or '\r'))
        {
            line--;
        }

        return line;
    }

    /// <summary>The offset of the line break that ends the line that contains <paramref name="index"/>, or the text's length.</summary>
    private static int LineEnd(string text, int index)
    {
        int line = index;
        while (line < text.Length && text[line] is not ('\n' or '\r'))
        {
            line++;
        }

        return line;
    }

    /// <summary>The offset of the line after the line break at <paramref name="lineEnd"/>, or the text's length at the end.</summary>
    private static int NextLineStart(string text, int lineEnd)
    {
        if (lineEnd >= text.Length)
        {
            return text.Length;
        }

        return text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n' ? lineEnd + 2 : lineEnd + 1;
    }
}
