using System.Collections.Immutable;
using Mdedit.Core.Render;

namespace Mdedit.Core.Editing;

/// <summary>
/// The removal of a block's last character: the block goes with its separator, and a pending block of the same shape (its heading marker, its
/// item marker, its task box) is held where it was, so that typing writes the block back.
/// </summary>
public static partial class ViewEdit
{
    /// <summary>
    /// Removes a block whose last text was removed. Its separator goes with it: the separator after it when there is a block after it (the view
    /// holds a pending block above the next one), else the one before it (a pending block below the previous one). A document's only block goes with
    /// its own line, and an inline pending holds its prefix and marker at the line's start.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="index">The block whose text is gone.</param>
    private static ViewEditResult RemoveBlock(RenderDocument doc, int index)
    {
        RenderBlock block = doc.Blocks[index];
        ImmutableArray<BlockShape> shapes = BlockShape.Of(doc);
        var expected = shapes.ToList();
        expected.RemoveAt(index);
        string text = doc.Text;
        string newline = LineEnding.Of(text);
        bool item = block.Containers.Length > 0 && IsItem(block.Containers[^1]);
        string shape = (block.Kind == BlockKind.Heading ? new string('#', block.HeadingLevel) + " " : string.Empty)
            + (block.Runs.Length > 0 && block.Runs[0].Cells.Any(cell => cell.IsMapped && cell.Style.Role == StyleRole.TaskBox) ? "[ ] " : string.Empty);
        ImmutableArray<Container> chain = item ? block.Containers[..^1] : block.Containers;

        if (index + 1 < doc.Blocks.Length)
        {
            int from = LineStart(text, block.Start);
            int to = LineStart(text, doc.Blocks[index + 1].Start);
            string separator = EditSource.Separator(newline, chain, tightItems: item && SharesList(doc, shapes, index, index + 1) && block.Tight);
            string tail = separator + EditSite.LinePrefix(text, doc.Blocks[index + 1].Start);
            var pending = new ViewEditResult.Pending(from + doc.Blocks[index + 1].Start - to, shape, PendingPlacement.Above, ShapeDisplay(doc, index), tail);
            return CommitText(doc, text.Remove(from, to - from), pending.Offset, expected, pending);
        }

        if (index == 0 && doc.Blocks.Length == 1)
        {
            // The only block: its source goes, and what is left (blank lines, say) has no block. The pending is inline, at the line's start,
            // with the block's prefix and marker, so that typing writes the block back into the empty document.
            int start = LineStart(text, block.Start);
            string held = EditSite.LinePrefix(text, block.Start) + shape;
            string display = ShapeDisplay(doc, index) + (block.Kind == BlockKind.Heading ? new string('#', block.HeadingLevel) + " " : string.Empty);
            var pending = new ViewEditResult.Pending(start, held, PendingPlacement.Inline, display);
            return CommitText(doc, text.Remove(start, block.End - start), start, expected, pending);
        }

        if (index > 0)
        {
            int at = LastTextEnd(doc, doc.Blocks[index - 1]);
            string separator = EditSource.Separator(newline, chain, tightItems: item && SharesList(doc, shapes, index - 1, index) && block.Tight);
            string source = separator + EditSite.LinePrefix(text, block.Start) + shape;
            var pending = new ViewEditResult.Pending(at, source, PendingPlacement.Below, ShapeDisplay(doc, index));
            return CommitText(doc, text.Remove(at, block.End - at), at, expected, pending);
        }

        return Refuse(EditMessages.CantEdit);
    }

    /// <summary>Whether two blocks are items of the same list: both are items, in the same containers outside their own items.</summary>
    private static bool SharesList(RenderDocument doc, ImmutableArray<BlockShape> shapes, int first, int second)
    {
        RenderBlock a = doc.Blocks[first];
        RenderBlock b = doc.Blocks[second];
        return a.Containers.Length > 0 && a.Containers.Length == b.Containers.Length
            && IsItem(a.Containers[^1]) && IsItem(b.Containers[^1])
            && SameIdentities(shapes[first].Containers[..^1], shapes[second].Containers[..^1]);
    }

    /// <summary>The text a pending block of a block's shape shows before the caret: the layout's prefix for its containers, with the item's marker.</summary>
    private static string ShapeDisplay(RenderDocument doc, int index)
    {
        RenderBlock block = doc.Blocks[index];
        bool task = block.Runs.Length > 0 && block.Runs[0].Cells.Any(cell => cell.IsMapped && cell.Style.Role == StyleRole.TaskBox);
        if (block.Containers.Length > 0 && IsItem(block.Containers[^1]) && block.IsFirstInItem)
        {
            return EditSource.Display(Unmarked(block.Containers[..^1]).Add(block.Containers[^1] with { IsFirstLeaf = true }), showMarker: true, task);
        }

        return EditSource.Display(Unmarked(block.Containers), showMarker: false, task: false);
    }

    /// <summary>
    /// A typed character that writes a block whose form is not known from the pending's source (a block of the shape of a removed one): the
    /// text is written and parsed, and every other block must be as it was, with the new block showing exactly the typed text.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="written">The text the edit writes at <paramref name="at"/>.</param>
    /// <param name="at">The offset the text is written at.</param>
    /// <param name="insertAt">The index of the new block in the document after the edit.</param>
    /// <param name="typed">The typed character, which the new block must show.</param>
    /// <param name="expected">The blocks before the edit, to which the new block is added.</param>
    /// <param name="caret">The caret's offset after the edit.</param>
    private static ViewEditResult LenientInsert(RenderDocument doc, int at, string written, int insertAt, string typed, List<BlockShape> expected, int caret)
    {
        string after = doc.Text.Insert(at, written);
        ImmutableArray<BlockShape> parsed = BlockShape.Of(RenderDocument.Parse(after));
        if (parsed.Length != expected.Count + 1 || parsed[insertAt].Text != typed)
        {
            return Refuse(EditMessages.CantEdit);
        }

        expected.Insert(insertAt, parsed[insertAt]);
        return CommitText(doc, after, caret, expected);
    }
}
