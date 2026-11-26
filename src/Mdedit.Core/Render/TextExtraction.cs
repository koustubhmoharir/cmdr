namespace Mdedit.Core.Render;

/// <summary>
/// Copies a part of a document as plain text or as Markdown. Both work on the render model and the source offsets, not on a
/// layout, so wrapping and folding do not change what is copied.
/// </summary>
public static class TextExtraction
{
    /// <summary>
    /// The rendered text of the source range <c>[start, end)</c>: the text of the cells whose source offsets are in the range,
    /// with the markup of the rendering (bullets, task boxes, table tabs and rows) and not the markup of the source (emphasis
    /// delimiters, quote bars, fences). Blocks are separated by a blank line, or by a line feed between two tight blocks; a
    /// list item's first leaf starts with its marker. The range is clamped to the text.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="start">The offset of the first character of the range.</param>
    /// <param name="end">The offset after the last character of the range.</param>
    public static string PlainText(RenderDocument doc, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(doc);

        (start, end) = Clamp(doc, start, end);
        return end <= start ? string.Empty : PlainTextCopy.Of(doc, start, end);
    }

    /// <summary>
    /// The source text of <c>[start, end)</c made valid on its own: the markup of the inline containers the range is cut from is
    /// re-added (an opener before a range that starts inside a container, a closer after one that ends inside it), the container
    /// prefix (such as <c>&gt; - </c>) is added when the range starts mid-line in a block with containers, and a range inside a
    /// table is whole rows (the header, the delimiter row and the rows it touches). Otherwise the source is copied as it is. The
    /// range is clamped to the text.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="start">The offset of the first character of the range.</param>
    /// <param name="end">The offset after the last character of the range.</param>
    public static string Markdown(RenderDocument doc, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(doc);

        (start, end) = Clamp(doc, start, end);
        return end <= start ? string.Empty : MarkdownCopy.Of(doc, start, end);
    }

    /// <summary>Clamps a range to the text of the document; an empty result when the range is inverted.</summary>
    private static (int Start, int End) Clamp(RenderDocument doc, int start, int end)
    {
        int length = doc.Text.Length;
        int clampedStart = Math.Clamp(start, 0, length);
        return (clampedStart, Math.Clamp(end, clampedStart, length));
    }
}
