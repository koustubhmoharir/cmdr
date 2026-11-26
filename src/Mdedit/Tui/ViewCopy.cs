using Mdedit.Core.Render;

namespace Mdedit.Tui;

/// <summary>
/// What the copy keys take from the document: the selection when there is one, else the block under the cursor. Pure, so the window
/// only adds the text, the clipboard and the message.
/// </summary>
internal static class ViewCopy
{
    /// <summary>
    /// The source range to copy: <paramref name="selection"/> when it is set, else the block that holds <paramref name="cursor"/> (the
    /// last block that starts at or before the cursor, when the cursor is between blocks). Null when the document has no block.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="selection">The selection, or null.</param>
    /// <param name="cursor">The absolute source offset of the cursor.</param>
    public static SourceRange? RangeOf(RenderDocument document, SourceRange? selection, int cursor)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (selection is { } range)
        {
            return range;
        }

        RenderBlock? owner = null;
        foreach (RenderBlock block in document.Blocks)
        {
            if (cursor >= block.Start && cursor < block.End)
            {
                return new SourceRange(block.Start, block.End);
            }

            if (block.Start <= cursor)
            {
                owner = block;
            }
        }

        return owner is null ? null : new SourceRange(owner.Start, owner.End);
    }
}
