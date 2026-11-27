using System.Collections.Immutable;
using Mdedit.Core.Render;

namespace Mdedit.Core.Editing;

/// <summary>
/// The helpers of the structure edits: the containers of a block, the identities of new containers, the marker of a new item, and the one
/// replacement that every structure edit makes and verifies.
/// </summary>
public static partial class ViewEdit
{
    /// <summary>Whether a container is a list item.</summary>
    private static bool IsItem(Container entry) => entry.Kind is ContainerKind.BulletItem or ContainerKind.OrderedItem;

    /// <summary>Whether a container is a quote or an alert.</summary>
    private static bool IsQuote(Container entry) => entry.Kind is ContainerKind.Quote or ContainerKind.Alert;

    /// <summary>The containers with none of them the first leaf, for the display of a block that is not the first line of its containers.</summary>
    private static ImmutableArray<Container> Unmarked(ImmutableArray<Container> chain)
    {
        var result = ImmutableArray.CreateBuilder<Container>(chain.Length);
        foreach (Container entry in chain)
        {
            result.Add(entry with { IsFirstLeaf = false });
        }

        return result.MoveToImmutable();
    }

    /// <summary>
    /// The index of the first leaf of the innermost item a block is in: the block whose container at the item's depth starts the item, going back
    /// from the block.
    /// </summary>
    private static int FirstLeafIndex(RenderDocument doc, int index)
    {
        int depth = doc.Blocks[index].Containers.Length - 1;
        int first = index;
        while (first > 0 && !doc.Blocks[first].Containers[depth].IsFirstLeaf)
        {
            first--;
        }

        return first;
    }

    /// <summary>Whether a block's item is a task item: its first leaf starts with a task box.</summary>
    private static bool IsTaskItem(RenderDocument doc, int index)
    {
        RenderBlock first = doc.Blocks[FirstLeafIndex(doc, index)];
        return first.Runs.Length > 0 && first.Runs[0].Cells.Any(cell => cell.IsMapped && cell.Style.Role == StyleRole.TaskBox);
    }

    /// <summary>The bullet character of a bullet item, as the source writes it: the character before its first leaf's text.</summary>
    private static char BulletChar(RenderDocument doc, int leaf)
    {
        int at = doc.Blocks[leaf].Start - 1;
        while (at > 0 && doc.Text[at] == ' ')
        {
            at--;
        }

        return doc.Text[at];
    }

    /// <summary>
    /// The marker a new item after an item's text takes, without its space: the bullet character of the list, or the number after the item's
    /// number, with its delimiter.
    /// </summary>
    private static string NewItemMarker(RenderDocument doc, int index, Container entry) =>
        entry.Kind == ContainerKind.OrderedItem
            ? EditSource.NextMarker(entry.Marker!)
            : BulletChar(doc, FirstLeafIndex(doc, index)).ToString();

    /// <summary>
    /// The shape of a container of a new item: the kind and details of <paramref name="entry"/> with the given marker and a new identity, one
    /// for each block slot and depth.
    /// </summary>
    private static ShapeContainer NewContainer(Container entry, string? marker, int slot, int depth) =>
        new(entry.Kind, entry.Alert, entry.BulletDepth, entry.Kind == ContainerKind.OrderedItem ? marker : null, -(slot * 64 + depth + 1));

    /// <summary>Whether the block after a block is in the same innermost item (its container at that depth is not the first leaf).</summary>
    private static bool ContinuesItem(RenderDocument doc, int index)
    {
        int depth = doc.Blocks[index].Containers.Length - 1;
        return index + 1 < doc.Blocks.Length && doc.Blocks[index + 1].Containers.Length > depth && !doc.Blocks[index + 1].Containers[depth].IsFirstLeaf;
    }

    /// <summary>
    /// Writes one structure edit: the text replaced at <paramref name="start"/>, reparsed and verified against the blocks the edit expects. The
    /// replacement is the changed middle of the text. A pending block rides on it as <paramref name="then"/>.
    /// </summary>
    /// <param name="doc">The document before the edit.</param>
    /// <param name="start">The offset of the replaced text.</param>
    /// <param name="length">The number of characters replaced.</param>
    /// <param name="text">The text written in their place.</param>
    /// <param name="caret">The caret's offset after the edit.</param>
    /// <param name="expected">The blocks the document must have after the edit.</param>
    /// <param name="then">A pending block the view holds at the caret after the edit, or null.</param>
    private static ViewEditResult Commit(RenderDocument doc, int start, int length, string text, int caret, IEnumerable<BlockShape> expected, ViewEditResult.Pending? then = null)
    {
        string after = string.Concat(doc.Text.AsSpan(0, start), text, doc.Text.AsSpan(start + length));
        return CommitText(doc, after, caret, expected, then);
    }

    /// <summary>
    /// Writes the whole edited text of a structure edit: it is reparsed and verified against the expected blocks, then returned as the changed
    /// middle of the text.
    /// </summary>
    /// <param name="doc">The document before the edit.</param>
    /// <param name="after">The text after the edit.</param>
    /// <param name="caret">The caret's offset in the text after the edit.</param>
    /// <param name="expected">The blocks the document must have after the edit.</param>
    /// <param name="then">A pending block the view holds at the caret after the edit, or null.</param>
    private static ViewEditResult CommitText(RenderDocument doc, string after, int caret, IEnumerable<BlockShape> expected, ViewEditResult.Pending? then = null)
    {
        if (!EditVerifier.Holds(expected, RenderDocument.Parse(after)))
        {
            return Refuse(EditMessages.CantEdit);
        }

        (int changed, int removed, string written) = TextChange.Of(doc.Text, after);
        return new ViewEditResult.Replacement(changed, removed, written, caret, then);
    }
}
