using System.Collections.Immutable;
using Mdedit.Core.Render;

namespace Mdedit.Core.Editing;

/// <summary>
/// Backspace at a block's start and Delete at its end: the heading, item and quote unwrapping, and the joins of two blocks; and the joins of two
/// lines of one block (a hard break, or a line of code).
/// </summary>
public static partial class ViewEdit
{
    /// <summary>
    /// Backspace at a block's start, in order: a heading becomes a paragraph; the first leaf of a list item joins the previous item's last text block,
    /// else becomes a paragraph before the list; the first leaf of a quote leaves it; otherwise the block joins the previous text block with the same
    /// containers, or a previous thematic break is removed. At the document's start nothing happens (an empty refusal message).
    /// </summary>
    private static ViewEditResult BackspaceAtStart(RenderDocument doc, int index)
    {
        RenderBlock block = doc.Blocks[index];
        ImmutableArray<BlockShape> shapes = BlockShape.Of(doc);
        int n = block.Containers.Length;
        int last = n - 1;
        if (block.Kind == BlockKind.Heading)
        {
            int from = block.Start;
            int to = FirstTextStart(doc, block);
            var expected = shapes.ToList();
            expected[index] = shapes[index] with { Kind = BlockKind.Paragraph, HeadingLevel = 0 };
            return Commit(doc, from, to - from, string.Empty, from, expected);
        }

        if (n > 0 && IsItem(block.Containers[last]) && block.IsFirstInItem)
        {
            int to = FirstTextStart(doc, block);
            if (index > 0 && IntoPreviousItem(doc, shapes, index) && LastTextRun(doc.Blocks[index - 1]) >= 0)
            {
                return JoinWithPrevious(doc, shapes, index);
            }

            int markerStart = ItemMarkerStart(doc, block);
            var unwrapped = shapes.ToList();
            unwrapped[index] = shapes[index] with { Containers = shapes[index].Containers[..last] };
            return Commit(doc, markerStart, to - markerStart, string.Empty, markerStart, unwrapped);
        }

        if (n > 0 && IsQuote(block.Containers[last]) && block.Containers[last].IsFirstLeaf)
        {
            return LeaveQuote(doc, shapes, index);
        }

        if (index == 0)
        {
            return Refuse(string.Empty);
        }

        RenderBlock previous = doc.Blocks[index - 1];
        if (previous.Kind == BlockKind.ThematicBreak && SameIdentities(shapes[index - 1].Containers, shapes[index].Containers))
        {
            int from = previous.Start;
            int to = LineStart(doc.Text, FirstTextStart(doc, block));
            var expected = shapes.ToList();
            expected.RemoveAt(index - 1);
            return Commit(doc, from, to - from, string.Empty, from, expected);
        }

        if (previous.Kind is BlockKind.Paragraph or BlockKind.Heading && SameIdentities(shapes[index - 1].Containers, shapes[index].Containers))
        {
            return JoinWithPrevious(doc, shapes, index);
        }

        return Refuse(EditMessages.CantJoin);
    }

    /// <summary>
    /// Delete at a block's end: the next text block's text is appended to the block (its marker, prefix and kind go) when it continues the
    /// block's containers or starts the next item of its list; a next thematic break with the same containers is removed. At the document's end
    /// nothing happens (an empty refusal message).
    /// </summary>
    private static ViewEditResult DeleteAtBlockEnd(RenderDocument doc, int index)
    {
        if (index + 1 >= doc.Blocks.Length)
        {
            return Refuse(string.Empty);
        }

        RenderBlock block = doc.Blocks[index];
        RenderBlock next = doc.Blocks[index + 1];
        ImmutableArray<BlockShape> shapes = BlockShape.Of(doc);
        int at = LastTextEnd(doc, block);
        if (next.Kind == BlockKind.ThematicBreak && SameIdentities(shapes[index].Containers, shapes[index + 1].Containers))
        {
            var expected = shapes.ToList();
            expected.RemoveAt(index + 1);
            return Commit(doc, at, next.End - at, string.Empty, at, expected);
        }

        if (next.Kind is BlockKind.Paragraph or BlockKind.Heading && JoinsNext(doc, shapes, index))
        {
            int to = FirstTextStart(doc, next);
            var expected = shapes.ToList();
            expected[index] = shapes[index] with { Text = shapes[index].Text + shapes[index + 1].Text };
            expected.RemoveAt(index + 1);
            return Commit(doc, at, to - at, string.Empty, at, expected);
        }

        return Refuse(EditMessages.CantJoin);
    }

    /// <summary>
    /// Backspace or Delete across a line break inside one block (a hard break, or the line break of a code block): the break and the next line's
    /// prefix are removed, and the lines join with nothing between.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="index">The block.</param>
    /// <param name="next">The run after the break, whose first text is joined to the run before it.</param>
    private static ViewEditResult JoinLines(RenderDocument doc, int index, int next)
    {
        RenderBlock block = doc.Blocks[index];
        int previous = next - 1;
        int first = FirstTextCell(block.Runs[next]);
        if (first >= block.Runs[next].Cells.Length)
        {
            return Refuse(EditMessages.CantEdit);
        }

        int from = RunEnd(doc, block, previous);
        int to = block.Start + block.Runs[next].Cells[first].Offset;
        if (to <= from)
        {
            return Refuse(EditMessages.CantEdit);
        }

        var lines = BlockText.Lines(block).ToList();
        lines[previous] += lines[next];
        lines.RemoveAt(next);
        ImmutableArray<BlockShape> shapes = BlockShape.Of(doc);
        var expected = shapes.ToList();
        expected[index] = shapes[index] with { Text = string.Join('\n', lines) };
        return Commit(doc, from, to - from, string.Empty, from, expected);
    }

    /// <summary>
    /// Joins a block to the text block before it: the text of the block is appended to the previous one, and the source between them, with the
    /// block's prefix and marker, is removed.
    /// </summary>
    private static ViewEditResult JoinWithPrevious(RenderDocument doc, ImmutableArray<BlockShape> shapes, int index)
    {
        RenderBlock block = doc.Blocks[index];
        int from = LastTextEnd(doc, doc.Blocks[index - 1]);
        int to = FirstTextStart(doc, block);
        var expected = shapes.ToList();
        expected[index - 1] = shapes[index - 1] with { Text = shapes[index - 1].Text + shapes[index].Text };
        expected.RemoveAt(index);
        return Commit(doc, from, to - from, string.Empty, from, expected);
    }

    /// <summary>
    /// Leaves a quote: the block's lines lose their quote bar, and the block is no longer in the quote. Only a block whose only container is the quote
    /// is unwrapped; the blocks after it in the quote start a quote of their own.
    /// </summary>
    private static ViewEditResult LeaveQuote(RenderDocument doc, ImmutableArray<BlockShape> shapes, int index)
    {
        RenderBlock block = doc.Blocks[index];
        if (block.Containers.Length != 1)
        {
            return Refuse(EditMessages.CantEdit);
        }

        string text = doc.Text;
        int lineStart = LineStart(text, block.Start);
        var removals = new List<SourceRange>();
        if (block.Start - lineStart is 0 or > 2 || !text.AsSpan(lineStart, block.Start - lineStart).StartsWith(">"))
        {
            return Refuse(EditMessages.CantEdit);
        }

        removals.Add(new SourceRange(lineStart, block.Start));
        for (int at = block.Start; at < block.End; at++)
        {
            if (text[at] is '\r' or '\n')
            {
                int after = text[at] == '\r' && at + 1 < text.Length && text[at + 1] == '\n' ? at + 2 : at + 1;
                if (after < text.Length && text[after] == '>')
                {
                    removals.Add(new SourceRange(after, after + (after + 1 < text.Length && text[after + 1] == ' ' ? 2 : 1)));
                }

                at = after - 1;
            }
        }

        var result = new System.Text.StringBuilder();
        int position = 0;
        foreach (SourceRange range in removals)
        {
            result.Append(text, position, range.Start - position);
            position = range.End;
        }

        result.Append(text, position, text.Length - position);
        var expected = shapes.ToList();
        expected[index] = shapes[index] with { Containers = [] };
        return CommitText(doc, result.ToString(), lineStart, expected);
    }

    /// <summary>The offset after the last text cell of a run, or the run's start when it has no text.</summary>
    private static int RunEnd(RenderDocument doc, RenderBlock block, int run)
    {
        int cell = Array.FindLastIndex(block.Runs[run].Cells.ToArray(), c => c.IsMapped && c.Style.Role != StyleRole.TaskBox);
        return cell < 0 ? block.Start + block.Runs[run].Start : block.Start + block.Runs[run].Cells[cell].Offset + SourceLength.Of(doc, block, block.Runs[run], cell);
    }

    /// <summary>The offset after the last text cell of a block, or the block's start when it has no text.</summary>
    private static int LastTextEnd(RenderDocument doc, RenderBlock block)
    {
        int run = LastTextRun(block);
        return run < 0 ? block.Start : RunEnd(doc, block, run);
    }

    /// <summary>The offset of the first text cell of a block (not a task box), or the block's start when it has none.</summary>
    private static int FirstTextStart(RenderDocument doc, RenderBlock block)
    {
        foreach (Run run in block.Runs)
        {
            int cell = FirstTextCell(run);
            if (cell < run.Cells.Length)
            {
                return block.Start + run.Cells[cell].Offset;
            }
        }

        return block.Start;
    }

    /// <summary>The offset of the first character of the source line that holds <paramref name="at"/>.</summary>
    private static int LineStart(string text, int at)
    {
        int start = at;
        while (start > 0 && text[start - 1] is not ('\r' or '\n'))
        {
            start--;
        }

        return start;
    }

    /// <summary>The source offset of an item's marker: the number of an ordered item, or the bullet character before a bullet item's text.</summary>
    private static int ItemMarkerStart(RenderDocument doc, RenderBlock block)
    {
        Container entry = block.Containers[^1];
        if (entry.Kind == ContainerKind.OrderedItem)
        {
            return block.Start + entry.MarkerOffset;
        }

        int at = block.Start - 1;
        while (at > 0 && doc.Text[at] == ' ')
        {
            at--;
        }

        return at;
    }

    /// <summary>Whether the block before an item's first leaf is a text block of the previous item of the same list.</summary>
    private static bool IntoPreviousItem(RenderDocument doc, ImmutableArray<BlockShape> shapes, int index)
    {
        RenderBlock previous = doc.Blocks[index - 1];
        int depth = doc.Blocks[index].Containers.Length - 1;
        return previous.Kind is BlockKind.Paragraph or BlockKind.Heading
            && previous.Containers.Length > depth
            && IsItem(previous.Containers[depth])
            && SameIdentities(shapes[index - 1].Containers[..depth], shapes[index].Containers[..depth]);
    }

    /// <summary>
    /// Whether a text block joins the block before it at its end: it continues the block's containers, or it is the first leaf of the next item of
    /// the block's own list (its marker goes).
    /// </summary>
    private static bool JoinsNext(RenderDocument doc, ImmutableArray<BlockShape> shapes, int index)
    {
        RenderBlock block = doc.Blocks[index];
        RenderBlock next = doc.Blocks[index + 1];
        int n = block.Containers.Length;
        if (n == 0)
        {
            return true;
        }

        if (next.Containers.Length < n || !SameIdentities(shapes[index].Containers[..(n - 1)], shapes[index + 1].Containers[..(n - 1)]))
        {
            return false;
        }

        bool continues = shapes[index].Containers[n - 1].Identity == shapes[index + 1].Containers[n - 1].Identity;
        bool sibling = IsItem(block.Containers[n - 1]) && IsItem(next.Containers[n - 1]) && next.Containers[n - 1].IsFirstLeaf;
        return continues || sibling;
    }

    /// <summary>Whether two lists of containers are the same instances, level by level.</summary>
    private static bool SameIdentities(ImmutableArray<ShapeContainer> x, ImmutableArray<ShapeContainer> y) =>
        x.Length == y.Length && x.Zip(y).All(pair => pair.First.Identity == pair.Second.Identity);
}
