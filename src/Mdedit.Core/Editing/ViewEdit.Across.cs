using System.Collections.Immutable;
using Mdedit.Core.Render;

namespace Mdedit.Core.Editing;

/// <summary>
/// A selection across blocks. The blocks wholly inside it go with their separators. The first block keeps its text before the selection and the
/// last keeps its text after it, and the two are joined into the first block's kind and containers. A block the selection covers wholly is
/// removed instead, and a code, HTML or raw block or a table is removed only when it is covered wholly.
/// </summary>
public static partial class ViewEdit
{
    /// <summary>
    /// Replaces a selection whose ends are in different blocks, or deletes it. The result is one replacement of the source, verified against the
    /// blocks the edit expects: the blocks before and after the selection are unchanged, and the first block holds the joined text (or the last
    /// block holds its remainder, when the first is removed). If every block is removed, the document's only block rule applies (see
    /// <c>OnlyBlockPending</c>).
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="a">The index of the block the selection starts in.</param>
    /// <param name="b">The index of the block the selection ends in, a later one.</param>
    /// <param name="start">The selection's start offset.</param>
    /// <param name="end">The selection's end offset.</param>
    /// <param name="typed">The text that takes the selection's place; empty deletes it.</param>
    private static ViewEditResult ReplaceAcrossBlocks(RenderDocument doc, int a, int b, int start, int end, string typed)
    {
        string text = doc.Text;
        RenderBlock head = doc.Blocks[a];
        RenderBlock tail = doc.Blocks[b];
        bool headWhole = start <= FirstStop(doc, head);
        bool tailWhole = end >= LastStop(doc, tail);
        bool headKeeps = !(headWhole && typed.Length == 0);
        bool tailKeeps = !tailWhole;
        if ((IsWholeOnly(head) && headKeeps) || (IsWholeOnly(tail) && tailKeeps))
        {
            return Refuse(EditMessages.WholeOnly);
        }

        ImmutableArray<BlockShape> shapes = BlockShape.Of(doc);
        int headLine = LineStart(text, head.Start);
        if (!headKeeps && !tailKeeps && a == 0 && b == doc.Blocks.Length - 1)
        {
            return OnlyBlockRemoved(doc, tail.End, headLine);
        }

        var expected = shapes.ToList();
        if (headKeeps)
        {
            // The text before the selection, the typed text and the text after it are one block: the first block's kind and containers.
            int at = EditSite.EscapeStart(doc, head, start);
            int to = tailKeeps ? KeptFrom(doc, tail, end) : tail.End;
            string written = Escaper.Escape(typed, EditSite.LinePrefix(text, at), EditSite.IsRaw(head, at));
            string joined = BlockText.Split(head, start).Before + typed + (tailKeeps ? BlockText.Split(tail, end).After : string.Empty);
            expected.RemoveRange(a, b - a + 1);
            expected.Insert(a, shapes[a] with { Text = Trimmed(head, joined) });
            return CommitText(doc, text[..at] + written + text[to..], at + written.Length, expected);
        }

        if (tailKeeps)
        {
            // The first block is removed. The last block keeps its containers and marker, and loses only the text the selection covers.
            int tailLine = LineStart(text, tail.Start);
            int to = KeptFrom(doc, tail, end);
            string after = text[..headLine] + text[tailLine..tail.Start] + text[to..];
            expected.RemoveRange(a, b - a + 1);
            expected.Insert(a, shapes[b] with { Text = Trimmed(tail, BlockText.Split(tail, end).After) });
            return CommitText(doc, after, headLine + (tail.Start - tailLine), expected);
        }

        // Both blocks are removed, and the blocks between them. The caret goes to the end of the block before, or to the start of the block after.
        string removed = text[..headLine] + text[tail.End..];
        int next = b + 1 < doc.Blocks.Length ? FirstStop(doc, doc.Blocks[b + 1]) - (tail.End - headLine) : 0;
        int caret = a > 0 ? LastTextEnd(doc, doc.Blocks[a - 1]) : next;
        expected.RemoveRange(a, b - a + 1);
        return CommitText(doc, removed, caret, expected);
    }

    /// <summary>
    /// The result of a selection that removes every block of the document: the text left is the blank lines around the blocks, and the held pending
    /// is the first block's shape (see <c>OnlyBlockPending</c>), at the line the blocks started on.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="end">The end of the last block removed.</param>
    /// <param name="lineStart">The start of the line the first block starts on.</param>
    private static ViewEditResult OnlyBlockRemoved(RenderDocument doc, int end, int lineStart)
    {
        string after = doc.Text[..lineStart] + doc.Text[end..];
        ViewEditResult.Pending pending = OnlyBlockPending(doc, 0, lineStart);
        if (!EditVerifier.Holds(Array.Empty<BlockShape>(), RenderDocument.Parse(after)))
        {
            return Refuse(EditMessages.CantEdit);
        }

        (int changed, int removed, string written) = TextChange.Of(doc.Text, after);
        return new ViewEditResult.Replacement(changed, removed, written, lineStart, pending);
    }

    /// <summary>
    /// The trimmed text of a paragraph or heading after a join: the spaces at its edges would not show, so the verification does not count them.
    /// Other kinds keep their text.
    /// </summary>
    /// <param name="block">The block the text is for.</param>
    /// <param name="text">The joined text.</param>
    private static string Trimmed(RenderBlock block, string text) =>
        block.Kind is BlockKind.Paragraph or BlockKind.Heading ? text.Trim(' ') : text;

    /// <summary>Whether a block is removed whole only: a code, HTML or raw block, or a table, whose text cannot be joined.</summary>
    private static bool IsWholeOnly(RenderBlock block) => block.Kind is BlockKind.Code or BlockKind.Html or BlockKind.Raw or BlockKind.Table;

    /// <summary>The caret stops of a block as absolute offsets: each mapped cell's start, the end of each line with text, and each empty table cell's place.</summary>
    /// <param name="doc">The document.</param>
    /// <param name="block">The block.</param>
    private static IEnumerable<int> Stops(RenderDocument doc, RenderBlock block)
    {
        for (int run = 0; run < block.Runs.Length; run++)
        {
            if (block.Runs[run].Cells.Any(cell => cell.IsMapped))
            {
                foreach (Cell cell in block.Runs[run].Cells.Where(cell => cell.IsMapped))
                {
                    yield return block.Start + cell.Offset;
                }

                yield return RunEnd(doc, block, run);
            }
            else if (EditSite.EmptyCellOffset(block, run) is var empty and >= 0)
            {
                yield return empty;
            }
        }
    }

    /// <summary>
    /// Where the text a selection leaves in a block begins, for the selection's end in it. An end that is a caret stop keeps its text from there. An
    /// end that is no stop (just before a closing marker, say) keeps from the first mapped cell after it, so that the markup the selection covers goes
    /// with it; the block's end when no cell follows.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="block">The block the end is in.</param>
    /// <param name="end">The selection's end offset in the block.</param>
    private static int KeptFrom(RenderDocument doc, RenderBlock block, int end)
    {
        if (Stops(doc, block).Contains(end))
        {
            return EditSite.EscapeStart(doc, block, end);
        }

        List<int> following = block.Runs
            .SelectMany(run => run.Cells)
            .Where(cell => cell.IsMapped && block.Start + cell.Offset >= end)
            .Select(cell => block.Start + cell.Offset)
            .ToList();
        return following.Count == 0 ? block.End : EditSite.EscapeStart(doc, block, following.Min());
    }

    /// <summary>The first caret stop of a block: where its text starts, or its start when it has no stop.</summary>
    private static int FirstStop(RenderDocument doc, RenderBlock block) => Stops(doc, block).DefaultIfEmpty(block.Start).Min();

    /// <summary>The last caret stop of a block: where its text ends, or its start when it has no stop.</summary>
    private static int LastStop(RenderDocument doc, RenderBlock block) => Stops(doc, block).DefaultIfEmpty(block.Start).Max();
}
