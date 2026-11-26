namespace Mdedit.Core.Render;

/// <summary>
/// One grapheme cluster of a run, the unit the layout wraps and the cursor moves over. <see cref="Offset"/> ties it to the
/// source, which is what makes the rendered view editable: it is the source offset of the cluster's first character, or
/// <see cref="Synthetic"/> for a cell that stands for no source text (a bar, a bullet, a fold glyph).
/// </summary>
/// <param name="Text">One grapheme cluster. A mark that follows an inline boundary (an emphasis closer) joins the cell before it.</param>
/// <param name="Width">Its display width from <c>Shared.Core.DisplayWidth</c>: 0, 1 or 2 in practice.</param>
/// <param name="Style">Its look.</param>
/// <param name="Offset">
/// The source offset, relative to the start of the block that owns the run (add the block's <see cref="RenderBlock.Start"/>
/// for the document offset), or <see cref="Synthetic"/> when the cell is synthetic. May be negative for a marker that lies before the block.
/// </param>
/// <param name="LinkId">
/// The link this cell is in, unique within its block, or -1 when it is in no link. Ids do not cross blocks.
/// </param>
public readonly record struct Cell(string Text, int Width, CellStyle Style, int Offset, int LinkId)
{
    /// <summary>
    /// The offset of a cell that has no source character. A relative offset may legitimately be negative (the marker of an
    /// ordered item lies before its block), so this is not -1.
    /// </summary>
    public const int Synthetic = int.MinValue;

    /// <summary>Whether the cell stands for source text, so that the source map reaches it.</summary>
    public bool IsMapped => Offset != Synthetic;
}
