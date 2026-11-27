using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Mdedit.Core.Render;

namespace Mdedit.Core.Editing;

/// <summary>
/// Paste: the selection is removed as Backspace would remove it, then the text is written at the insertion point as Markdown source. Its lines
/// after the first take the container prefix of the block they land in, so a paste in a quote or a list item stays in it.
/// </summary>
public static partial class ViewEdit
{
    /// <summary>
    /// Pastes text at a caret, or over a selection. The text is written as source, unescaped; its line breaks of any kind become the document's line
    /// ending; each line after the first takes the container prefix of the block it lands in. Code is pasted raw, and a table cell takes one line,
    /// with its unescaped pipes escaped. A pending block the view holds at the caret is written in front of the text (after it, for an above
    /// pending, its tail follows).
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="start">The caret, or the selection's start (either end may be given first).</param>
    /// <param name="end">The selection's end; equal to <paramref name="start"/> when nothing is selected.</param>
    /// <param name="text">The text to paste. Empty text does nothing, without a message.</param>
    /// <param name="pending">
    /// The pending insertion the view holds at <paramref name="start"/>, or null. It is ignored when a selection is pasted over: the selection's
    /// removal may make a pending block of its own (see <see cref="ReplaceSelection"/>), which the paste then writes in front.
    /// </param>
    /// <returns>
    /// A replacement, or a refusal. The edit is verified by its blocks: the blocks wholly before and after the pasted range must be unchanged, and
    /// at least one block must lie between them. What the pasted text renders is not judged, so Markdown in it may render in any way.
    /// </returns>
    public static ViewEditResult Paste(RenderDocument doc, int start, int end, string text, ViewEditResult.Pending? pending = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(text);
        (start, end) = start <= end ? (start, end) : (end, start);
        if (text.Length == 0)
        {
            return Refuse(string.Empty);
        }

        if (start == end)
        {
            return PasteAt(doc, start, text, pending);
        }

        ViewEditResult removal = ReplaceSelection(doc, start, end, string.Empty);
        if (removal is not ViewEditResult.Replacement cut)
        {
            return removal;
        }

        RenderDocument after = RenderDocument.Parse(string.Concat(doc.Text.AsSpan(0, cut.Start), cut.Text, doc.Text.AsSpan(cut.Start + cut.Length)));
        ViewEditResult paste = PasteAt(after, cut.Caret, text, cut.Then);
        return paste is ViewEditResult.Replacement pasted ? Combine(doc, cut, pasted) : paste;
    }

    /// <summary>
    /// Writes pasted text at a caret, choosing the place by what the caret is on: an empty document; a pending block below the caret's block (the text
    /// follows the pending's source); a pending block above it (the text goes at the block's start, before the tail); a table cell (one line); or the
    /// insertion point of the caret's stop, after any held space.
    /// </summary>
    /// <param name="doc">The document, after any removal the paste makes.</param>
    /// <param name="caret">The caret's offset.</param>
    /// <param name="text">The text to paste, with any line breaks.</param>
    /// <param name="pending">The pending insertion the view holds at <paramref name="caret"/>, or null.</param>
    private static ViewEditResult PasteAt(RenderDocument doc, int caret, string text, ViewEditResult.Pending? pending)
    {
        string newline = LineEnding.Of(doc.Text);
        string[] lines = Regex.Split(text, "\r\n|\r|\n");
        if (doc.Blocks.Length == 0)
        {
            if (caret != 0)
            {
                return Refuse(EditMessages.NoPlace);
            }

            string edge = pending is { Placement: PendingPlacement.Inline, Offset: 0 } empty ? empty.SourceText : string.Empty;
            string body = edge + Lines(lines, newline, Continuation(edge));
            return Written(doc, 0, body, body.Length, prefix: 0, suffix: 0);
        }

        if (pending is { Placement: PendingPlacement.Below } below && below.Offset == caret && EditSite.Find(doc, caret) is { } under)
        {
            string body = below.SourceText + Lines(lines, newline, Continuation(below.SourceText));
            return Written(doc, caret, body, caret + body.Length, prefix: under.BlockIndex + 1, suffix: doc.Blocks.Length - under.BlockIndex - 1);
        }

        if (pending is { Placement: PendingPlacement.Above } above && above.Offset == caret && EditSite.Find(doc, caret) is { } over)
        {
            int at = doc.Blocks[over.BlockIndex].Start;
            string body = above.SourceText + Lines(lines, newline, Continuation(above.Tail));
            return Written(doc, at, body + above.Tail, at + body.Length, prefix: over.BlockIndex, suffix: doc.Blocks.Length - over.BlockIndex);
        }

        if (EditSite.Find(doc, caret) is not { } site)
        {
            return Refuse(EditMessages.NoPlace);
        }

        RenderBlock block = doc.Blocks[site.BlockIndex];
        string held = pending is { Placement: PendingPlacement.Inline } inline && inline.Offset == caret ? inline.SourceText : string.Empty;
        if (block.Kind == BlockKind.Table)
        {
            // A value copied from a cell or a line often ends in a line break, which is dropped; any other break makes it more than one line.
            string[] cell = lines.Length == 2 && lines[1].Length == 0 ? lines[..1] : lines;
            return cell.Length > 1 ? Refuse(EditMessages.CellOneLine) : PasteCell(doc, site, held + Escaped(cell[0]));
        }

        int insertion = InsertionAt(doc, site);
        string prefix = EditSource.Continuation(block.Containers) + (block.Kind == BlockKind.Code && IsIndentedCode(doc, block) ? "    " : string.Empty);
        string written = held + Lines(lines, newline, prefix);
        return Written(doc, insertion, written, insertion + written.Length, prefix: site.BlockIndex, suffix: doc.Blocks.Length - site.BlockIndex - 1);
    }

    /// <summary>
    /// The text written at an offset, verified by its blocks: the blocks before the first <paramref name="prefix"/> and the blocks after the last
    /// <paramref name="suffix"/> must be as they were, with at least one block between them. Returned as one replacement with the caret after the
    /// text (<paramref name="caret"/>, in the text after the edit).
    /// </summary>
    /// <param name="doc">The document before the paste.</param>
    /// <param name="at">The offset the text is written at.</param>
    /// <param name="written">The text written there.</param>
    /// <param name="caret">The caret's offset in the text after the paste.</param>
    /// <param name="prefix">The number of blocks before the pasted range, which must be unchanged.</param>
    /// <param name="suffix">The number of blocks after the pasted range, which must be unchanged.</param>
    private static ViewEditResult Written(RenderDocument doc, int at, string written, int caret, int prefix, int suffix)
    {
        string text = doc.Text.Insert(at, written);
        if (!Surrounded(doc, RenderDocument.Parse(text), prefix, suffix))
        {
            return Refuse(EditMessages.CantEdit);
        }

        (int start, int length, string replacement) = TextChange.Of(doc.Text, text);
        return new ViewEditResult.Replacement(start, length, replacement, caret);
    }

    /// <summary>
    /// The paste into a table cell: the cell's source is written with <see cref="RewriteCell"/>, so the table stays aligned or minimal as typing
    /// keeps it, and the blocks around the table must be as they were.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="site">The cell's stop.</param>
    /// <param name="inserted">The held space and the line, with its pipes escaped.</param>
    private static ViewEditResult PasteCell(RenderDocument doc, Site site, string inserted)
    {
        RenderBlock block = doc.Blocks[site.BlockIndex];
        RemovalSet none = RemovalSet.Of(doc, block, [], trimEdges: false);
        int at = InsertionAt(doc, site);
        CellWrite? written = RewriteCell(doc, site.BlockIndex, site.Run, none, doc.Text, at, inserted);
        if (written is not { } cell || !Surrounded(doc, cell.After, site.BlockIndex, doc.Blocks.Length - site.BlockIndex - 1))
        {
            return Refuse(EditMessages.CantEdit);
        }

        return new ViewEditResult.Replacement(cell.Start, cell.Length, cell.Text, cell.Caret);
    }

    /// <summary>A line of a table cell's paste, with each unescaped pipe escaped.</summary>
    private static string Escaped(string line) => Regex.Replace(line, @"(?<!\\)\|", @"\|");

    /// <summary>
    /// The insertion point of a caret stop: the place typing writes at (after the previous mapped cell of its run, or at its first cell's offset; in
    /// a table, the place of an empty cell).
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="site">The stop.</param>
    private static int InsertionAt(RenderDocument doc, Site site)
    {
        RenderBlock block = doc.Blocks[site.BlockIndex];
        Run run = block.Runs[site.Run];
        RemovalSet none = RemovalSet.Of(doc, block, [], trimEdges: false);
        int previous = EditSite.PreviousMapped(run, site.Cell, none, site.Run);
        int next = site.Cell < run.Cells.Length ? site.Cell : -1;
        int empty = EditSite.EmptyCellOffset(block, site.Run);
        return empty >= 0 ? empty : EditSite.InsertionPoint(doc, block, run, previous, next);
    }

    /// <summary>
    /// The prefix that the lines after the first of a paste take, from the source of a pending block: its quote bars are kept, and each other
    /// character (a list marker, say) becomes a space, so that the indent matches the marker's width. A task box is not part of the indent.
    /// </summary>
    /// <param name="source">The pending block's source; its last line holds its containers' prefix.</param>
    private static string Continuation(string source)
    {
        string line = LastLine(source);
        if (line.EndsWith("[ ] ", StringComparison.Ordinal))
        {
            line = line[..^4];
        }

        return new string(line.Select(c => c == '>' ? '>' : ' ').ToArray());
    }

    /// <summary>
    /// The lines joined with the document's line ending, each line after the first with <paramref name="prefix"/>. A blank line takes the prefix
    /// without its trailing spaces, so that a quote stays a quote across it.
    /// </summary>
    /// <param name="lines">The lines, the first of which is not prefixed.</param>
    /// <param name="newline">The line ending.</param>
    /// <param name="prefix">The prefix of the lines after the first.</param>
    private static string Lines(string[] lines, string newline, string prefix)
    {
        var builder = new StringBuilder(lines[0]);
        for (int index = 1; index < lines.Length; index++)
        {
            builder.Append(newline).Append(lines[index].Length == 0 ? prefix.TrimEnd(' ') : prefix + lines[index]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether the reparsed document has the blocks of <paramref name="before"/> before its first <paramref name="prefix"/> blocks and after its
    /// last <paramref name="suffix"/> blocks, unchanged, with at least one block between them. Containers are compared by their kind and details,
    /// not their identity, since a paste may join a container with the one around it.
    /// </summary>
    /// <param name="before">The document before the paste.</param>
    /// <param name="after">The document after it.</param>
    /// <param name="prefix">The number of blocks at the start that must be unchanged.</param>
    /// <param name="suffix">The number of blocks at the end that must be unchanged.</param>
    private static bool Surrounded(RenderDocument before, RenderDocument after, int prefix, int suffix)
    {
        ImmutableArray<BlockShape> old = BlockShape.Of(before);
        ImmutableArray<BlockShape> now = BlockShape.Of(after);
        if (now.Length < prefix + suffix + 1)
        {
            return false;
        }

        for (int index = 0; index < prefix; index++)
        {
            if (!SameBlock(old[index], now[index]))
            {
                return false;
            }
        }

        for (int index = 0; index < suffix; index++)
        {
            if (!SameBlock(old[old.Length - suffix + index], now[now.Length - suffix + index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether two block shapes are the same block, ignoring the identity of their containers.</summary>
    private static bool SameBlock(BlockShape x, BlockShape y) =>
        x.Kind == y.Kind
        && x.HeadingLevel == y.HeadingLevel
        && x.Text == y.Text
        && x.Containers.Select(entry => entry with { Identity = 0 }).SequenceEqual(y.Containers.Select(entry => entry with { Identity = 0 }));
}
