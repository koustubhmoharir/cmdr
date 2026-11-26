using System.Collections.Immutable;
using System.Text;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tables;

/// <summary>
/// Turns an edited <see cref="TableGrid"/> into the one replacement of the table's source: the table's lines are rendered by
/// <see cref="AlignedWriter"/> or <see cref="MinimalWriter"/>, and the replacement is the span between the first and the last
/// changed character of the old and the new text.
/// </summary>
internal static class TableWriter
{
    /// <summary>
    /// The replacement that turns the table's source into the grid: its start, the length it replaces and the text that replaces it.
    /// The span is the table's lines, from the header line's start (so the prefix is in it) to the last line's end.
    /// </summary>
    /// <param name="text">The document's source text.</param>
    /// <param name="source">The table as written, parsed from <paramref name="text"/>.</param>
    /// <param name="grid">The table after the edit.</param>
    public static (int Start, int Length, string Text) Splice(string text, TableSource source, TableGrid grid)
    {
        ArgumentNullException.ThrowIfNull(text);
        SourceRange span = source.Span;
        string before = text.Substring(span.Start, span.End - span.Start);
        ImmutableArray<(string Text, string Newline)> lines = source.IsAligned ? AlignedWriter.Lines(grid) : MinimalWriter.Lines(grid);
        string after = Join(lines, source.NewLine);
        return Trim(before, after, span.Start);
    }

    /// <summary>
    /// Joins the lines with their line endings. The last line has none (the table's text ends there); a line with no ending of its
    /// own, such as a new row, takes the table's.
    /// </summary>
    /// <param name="lines">The lines' text and their line endings.</param>
    /// <param name="newline">The table's line ending.</param>
    public static string Join(ImmutableArray<(string Text, string Newline)> lines, string newline)
    {
        var builder = new StringBuilder();
        for (int index = 0; index < lines.Length; index++)
        {
            builder.Append(lines[index].Text);
            if (index < lines.Length - 1)
            {
                builder.Append(lines[index].Newline.Length > 0 ? lines[index].Newline : newline);
            }
        }

        return builder.ToString();
    }

    /// <summary>The common prefix and suffix of the old and the new text are kept; what lies between them is replaced.</summary>
    private static (int Start, int Length, string Text) Trim(string before, string after, int offset)
    {
        int prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < before.Length - prefix
            && suffix < after.Length - prefix
            && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
        {
            suffix++;
        }

        string replacement = after.Substring(prefix, after.Length - prefix - suffix);
        return (offset + prefix, before.Length - prefix - suffix, replacement);
    }
}
