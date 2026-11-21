using System.Globalization;

namespace Cfgedit.Tui;

/// <summary>
/// The text mode's rules as plain functions, with no Terminal.Gui types. The view joins its lines with <see cref="Environment.NewLine"/>,
/// so the text read from it is normalised to the document's own line break, and the cursor's row and column (as the view counts them, in
/// grapheme clusters within a line) map to and from offsets in the normalised text.
/// </summary>
internal static class TextModeRules
{
    /// <summary>
    /// The view's text with its line breaks replaced by <paramref name="newline"/>. The result is the document's text when the view
    /// shows the document unchanged and the document uses one line break style throughout.
    /// </summary>
    public static string WithNewline(string viewText, string newline)
    {
        ArgumentNullException.ThrowIfNull(viewText);
        ArgumentNullException.ThrowIfNull(newline);
        return newline == Environment.NewLine ? viewText : viewText.Replace(Environment.NewLine, newline, StringComparison.Ordinal);
    }

    /// <summary>
    /// The offset in the normalised text (see <see cref="WithNewline"/>) of a view position. The row is clamped to the last row, and the
    /// column to the end of its row.
    /// </summary>
    /// <param name="viewText">The view's text, with its lines joined by <see cref="Environment.NewLine"/>.</param>
    /// <param name="row">The view's row, from zero.</param>
    /// <param name="column">The view's column in its row, counted in grapheme clusters from zero.</param>
    /// <param name="newline">The document's line break.</param>
    public static int OffsetOf(string viewText, int row, int column, string newline)
    {
        ArgumentNullException.ThrowIfNull(viewText);
        string[] lines = viewText.Split(Environment.NewLine);
        int clampedRow = Math.Clamp(row, 0, lines.Length - 1);
        int offset = 0;
        for (int r = 0; r < clampedRow; r++)
        {
            offset += lines[r].Length + newline.Length;
        }

        return offset + GraphemeIndex(lines[clampedRow], column);
    }

    /// <summary>
    /// The view's row and column of an offset in the normalised text: the inverse of <see cref="OffsetOf"/>. The offset is clamped to the
    /// text. Columns are counted in grapheme clusters.
    /// </summary>
    /// <param name="text">The normalised text.</param>
    /// <param name="newline">The document's line break.</param>
    /// <param name="offset">The offset.</param>
    public static (int Row, int Column) PositionOf(string text, string newline, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        int target = Math.Clamp(offset, 0, text.Length);
        string[] lines = text.Split(newline);
        int lineStart = 0;
        for (int r = 0; r < lines.Length; r++)
        {
            int lineEnd = lineStart + lines[r].Length;
            if (target <= lineEnd || r == lines.Length - 1)
            {
                int within = Math.Min(Math.Max(0, target - lineStart), lines[r].Length);
                return (r, new StringInfo(lines[r][..within]).LengthInTextElements);
            }

            lineStart = lineEnd + newline.Length;
        }

        return (0, 0);
    }

    // The UTF-16 index in a line of the start of a grapheme cluster, counting the clusters before the column. A column past the end is the end.
    private static int GraphemeIndex(string line, int column)
    {
        int index = 0;
        for (int i = 0; i < column && index < line.Length; i++)
        {
            index += StringInfo.GetNextTextElementLength(line.AsSpan(index));
        }

        return index;
    }
}
