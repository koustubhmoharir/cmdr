using System.Collections.Immutable;
using Mdedit.Core.Render;

namespace Mdedit.Core.Editing;

/// <summary>
/// Enter and the new blocks it makes: a split inside a block, a pending block below the caret's block at its end, a pending block above it at its
/// start, and a line break in a code block. Typing a pending block writes it (see <see cref="InsertBlock"/>).
/// </summary>
public static partial class ViewEdit
{
    /// <summary>
    /// Enter at the caret. In a paragraph, heading or list item it splits the block at the caret: two blocks of the same kind and containers,
    /// two list items (the new one numbered after the old, a task item unchecked) or two headings of the same level. At the block's end it holds
    /// a pending block below it (a paragraph, or a new item after a list item's text); at its start, a pending block above it (a paragraph, or a
    /// new item before a list item). In a code, HTML or raw block it writes a line break with the containers' prefixes. Enter on a pending
    /// item at the block's end ends the list; on a pending paragraph in a quote, it ends the quote.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="caret">The caret's source offset: a cell's offset, or a block's end position.</param>
    /// <param name="pending">The pending block the view holds at <paramref name="caret"/>, or null. An inline pending space is dropped.</param>
    /// <returns>A replacement, a pending block, or a refusal (a table, or a place inside a paragraph's line that cannot split).</returns>
    public static ViewEditResult Enter(RenderDocument doc, int caret, ViewEditResult.Pending? pending = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (EditSite.Find(doc, caret) is not { } site)
        {
            return Refuse(EditMessages.NoPlace);
        }

        RenderBlock block = doc.Blocks[site.BlockIndex];
        if (block.Kind == BlockKind.Table)
        {
            return Refuse(EditMessages.Table);
        }

        if (block.Runs[site.Run].Cells.Length > site.Cell && block.Runs[site.Run].Cells[site.Cell].Style.Role == StyleRole.TaskBox)
        {
            return Refuse(EditMessages.CantEdit);
        }

        if (block.Kind is BlockKind.Code or BlockKind.Html or BlockKind.Raw)
        {
            return BreakLine(doc, site.BlockIndex, caret);
        }

        if (IsBlockEnd(block, site))
        {
            if (pending is { Placement: PendingPlacement.Below } held && held.Offset == caret && EndPending(doc, site.BlockIndex, held) is { } ended)
            {
                return ended;
            }

            return BelowPending(doc, site.BlockIndex, caret);
        }

        if (IsBlockStart(block, site))
        {
            return AbovePending(doc, site.BlockIndex, caret);
        }

        return site.Cell == 0 || site.Cell >= block.Runs[site.Run].Cells.Length
            ? Refuse(EditMessages.CantEdit)
            : Split(doc, site, caret);
    }

    /// <summary>
    /// Enter over a selection inside one block: the selection is removed, then Enter acts at the place it started. The result is one replacement;
    /// a pending block that Enter holds rides on it as <see cref="ViewEditResult.Replacement.Then"/>.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="start">The selection's start (either end may be given first).</param>
    /// <param name="end">The selection's end.</param>
    public static ViewEditResult EnterOver(RenderDocument doc, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(doc);
        (start, end) = start <= end ? (start, end) : (end, start);
        if (start == end)
        {
            return Enter(doc, start);
        }

        ViewEditResult removal = ReplaceSelection(doc, start, end, string.Empty);
        if (removal is not ViewEditResult.Replacement edit)
        {
            return removal;
        }

        if (edit.Then is not null)
        {
            return edit;
        }

        RenderDocument after = RenderDocument.Parse(string.Concat(doc.Text.AsSpan(0, edit.Start), edit.Text, doc.Text.AsSpan(edit.Start + edit.Length)));
        ViewEditResult next = Enter(after, edit.Caret);
        return next switch
        {
            ViewEditResult.Replacement split => Combine(doc, edit, split),
            ViewEditResult.Pending held => new ViewEditResult.Replacement(edit.Start, edit.Length, edit.Text, held.Offset, held),
            _ => next,
        };
    }

    /// <summary>The result of two edits made one after the other, as one replacement of the original text.</summary>
    private static ViewEditResult Combine(RenderDocument doc, ViewEditResult.Replacement first, ViewEditResult.Replacement second)
    {
        string middle = string.Concat(doc.Text.AsSpan(0, first.Start), first.Text);
        string after = string.Concat(middle, doc.Text.AsSpan(first.Start + first.Length));
        string final = string.Concat(after.AsSpan(0, second.Start), second.Text, after.AsSpan(second.Start + second.Length));
        (int start, int length, string text) = TextChange.Of(doc.Text, final);
        return new ViewEditResult.Replacement(start, length, text, second.Caret);
    }

    /// <summary>Whether the caret is at the end of a block's text: after the last text cell of its last line.</summary>
    private static bool IsBlockEnd(RenderBlock block, Site site)
    {
        int last = LastTextRun(block);
        return last >= 0 && site.Run == last && site.Cell >= block.Runs[last].Cells.Length;
    }

    /// <summary>Whether the caret is before the first text cell of a block that has text.</summary>
    private static bool IsBlockStart(RenderBlock block, Site site) =>
        LastTextRun(block) >= 0 && site.Run == 0 && site.Cell == FirstTextCell(block.Runs[0]);

    /// <summary>The index of the last run that has a text cell, or -1 when the block has no text.</summary>
    private static int LastTextRun(RenderBlock block)
    {
        for (int run = block.Runs.Length - 1; run >= 0; run--)
        {
            if (block.Runs[run].Cells.Any(cell => cell.IsMapped && cell.Style.Role != StyleRole.TaskBox))
            {
                return run;
            }
        }

        return -1;
    }

    /// <summary>The index of the first text cell of a run (not a task box), or the run's cell count when it has none.</summary>
    private static int FirstTextCell(Run run)
    {
        for (int index = 0; index < run.Cells.Length; index++)
        {
            if (run.Cells[index].IsMapped && run.Cells[index].Style.Role != StyleRole.TaskBox)
            {
                return index;
            }
        }

        return run.Cells.Length;
    }

    /// <summary>A new line in a code, HTML or raw block: the line break, the containers' prefix, and the indentation of an indented code block.</summary>
    private static ViewEditResult BreakLine(RenderDocument doc, int index, int caret)
    {
        RenderBlock block = doc.Blocks[index];
        string newline = LineEnding.Of(doc.Text);
        string written = newline + EditSource.Continuation(block.Containers) + (IsIndentedCode(doc, block) ? "    " : string.Empty);
        (string before, string after) = BlockText.Split(block, caret);
        ImmutableArray<BlockShape> shapes = BlockShape.Of(doc);
        var expected = shapes.ToArray();
        expected[index] = expected[index] with { Text = before + "\n" + after };
        return Commit(doc, caret, 0, written, caret + written.Length, expected);
    }

    /// <summary>Whether a code block is indented rather than fenced: its first line does not open a fence.</summary>
    private static bool IsIndentedCode(RenderDocument doc, RenderBlock block)
    {
        if (block.Kind != BlockKind.Code)
        {
            return false;
        }

        int end = doc.Text.IndexOfAny(['\r', '\n'], block.Start);
        string first = doc.Text[block.Start..(end < 0 ? doc.Text.Length : end)].TrimStart();
        return !first.StartsWith("```", StringComparison.Ordinal) && !first.StartsWith("~~~", StringComparison.Ordinal);
    }

    /// <summary>
    /// A split of a paragraph, heading or list item at a cell in the middle of its line. The new block's first text is escaped where it would
    /// start a block (a list marker, a heading, a quote, a rule, or a number and its delimiter), as typing there would escape it.
    /// </summary>
    private static ViewEditResult Split(RenderDocument doc, Site site, int caret)
    {
        RenderBlock block = doc.Blocks[site.BlockIndex];
        int at = EditSite.EscapeStart(doc, block, block.Start + block.Runs[site.Run].Cells[site.Cell].Offset);
        (string before, string after) = BlockText.Split(block, at);
        before = before.TrimEnd(' ');
        after = after.TrimStart(' ');

        // The spaces around the split would end the first line and start the second; Markdown strips them, so they are not written.
        int cut = at;
        while (cut > block.Start && doc.Text[cut - 1] == ' ')
        {
            cut--;
        }

        int resume = at;
        while (resume < doc.Text.Length && doc.Text[resume] == ' ')
        {
            resume++;
        }

        ImmutableArray<BlockShape> shapes = BlockShape.Of(doc);
        string newline = LineEnding.Of(doc.Text);
        int last = block.Containers.Length - 1;
        bool item = last >= 0 && IsItem(block.Containers[last]) && block.IsFirstInItem;
        string separator;
        string linePrefix;
        ImmutableArray<ShapeContainer> chain;
        if (item)
        {
            Container entry = block.Containers[last];
            ImmutableArray<Container> outer = block.Containers[..last];
            string marker = NewItemMarker(doc, site.BlockIndex, entry);
            bool task = IsTaskItem(doc, site.BlockIndex);
            separator = EditSource.Separator(newline, outer, block.Tight);
            linePrefix = EditSource.Continuation(outer) + marker + " " + (task ? "[ ] " : string.Empty);
            chain = shapes[site.BlockIndex].Containers[..last].Add(NewContainer(entry, marker, site.BlockIndex + 1, last));
        }
        else
        {
            separator = EditSource.Separator(newline, block.Containers, tightItems: false);
            linePrefix = EditSource.Continuation(block.Containers);
            chain = shapes[site.BlockIndex].Containers;
        }

        if (block.Kind == BlockKind.Heading)
        {
            linePrefix += new string('#', block.HeadingLevel) + " ";
        }

        LineStart(doc, block, resume, linePrefix, out int first, out int escapeAt);
        string source = separator + linePrefix;
        int caretAt = first - (resume - cut) + source.Length + (escapeAt == first ? 1 : 0);
        string text = doc.Text[..cut] + source + doc.Text[resume..(escapeAt < 0 ? doc.Text.Length : escapeAt)]
            + (escapeAt < 0 ? string.Empty : "\\" + doc.Text[escapeAt..]);
        var expected = shapes.ToList();
        expected[site.BlockIndex] = shapes[site.BlockIndex] with { Text = before };
        expected.Insert(site.BlockIndex + 1, new BlockShape(block.Kind, block.HeadingLevel, chain, after));
        return CommitText(doc, text, caretAt, expected);
    }

    /// <summary>
    /// The first text cell of a block from an offset, and where a backslash must go before it or before a number's delimiter, so that the text
    /// that starts a new line is not read as block syntax. Escape is -1 when none is needed (or the text is escaped already).
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="block">The block the text is in.</param>
    /// <param name="from">The offset the new line's text starts at.</param>
    /// <param name="linePrefix">The source before the text on its new line: the containers' prefix and any marker.</param>
    /// <param name="first">The offset of the first text cell from <paramref name="from"/>.</param>
    /// <param name="escapeAt">The offset of the character the backslash goes before, or -1.</param>
    private static void LineStart(RenderDocument doc, RenderBlock block, int from, string linePrefix, out int first, out int escapeAt)
    {
        var cells = new List<(int Offset, string Text)>();
        foreach (Run run in block.Runs)
        {
            cells.AddRange(run.Cells.Where(cell => cell.IsMapped && cell.Style.Role != StyleRole.TaskBox && block.Start + cell.Offset >= from)
                .Select(cell => (block.Start + cell.Offset, cell.Text)));
            if (cells.Count > 0)
            {
                break;
            }
        }

        int index = cells.FindIndex(cell => cell.Text != " ");
        first = index < 0 ? from : cells[index].Offset;
        escapeAt = -1;
        if (index < 0 || cells[index].Text.Length != 1)
        {
            return;
        }

        char c = cells[index].Text[0];
        if (char.IsAsciiDigit(c))
        {
            var digits = new System.Text.StringBuilder().Append(c);
            for (int k = index + 1; k < cells.Count && char.IsAsciiDigit(cells[k].Text.Length == 1 ? cells[k].Text[0] : 'x'); k++)
            {
                digits.Append(cells[k].Text);
            }

            int delimiter = index + digits.Length;
            if (delimiter < cells.Count && cells[delimiter].Text is "." or ")" && Escaper.Escape(cells[delimiter].Text, linePrefix + digits, raw: false) != cells[delimiter].Text)
            {
                escapeAt = cells[delimiter].Offset;
            }
        }
        else if (Escaper.Escape(c.ToString(), linePrefix, raw: false) != c.ToString())
        {
            escapeAt = first;
        }

        if (escapeAt >= 0 && EditSite.EscapeStart(doc, block, escapeAt) != escapeAt)
        {
            escapeAt = -1;
        }
    }

    /// <summary>The pending block below a block's end: a new item after a list item's last text, else a paragraph with the same containers.</summary>
    private static ViewEditResult BelowPending(RenderDocument doc, int index, int caret)
    {
        RenderBlock block = doc.Blocks[index];
        int n = block.Containers.Length;
        bool item = n > 0 && IsItem(block.Containers[^1]) && !ContinuesItem(doc, index);
        Form form = BelowForm(doc, index, item, item ? n - 1 : n);
        return new ViewEditResult.Pending(caret, form.Source, PendingPlacement.Below, form.Display);
    }

    /// <summary>The pending block above a block's start: a new item before a list item's first text, else a paragraph with the same containers.</summary>
    private static ViewEditResult AbovePending(RenderDocument doc, int index, int caret)
    {
        RenderBlock block = doc.Blocks[index];
        Form form = AboveForm(doc, index, block.Containers.Length > 0 && IsItem(block.Containers[^1]) && block.IsFirstInItem);
        return new ViewEditResult.Pending(caret, string.Empty, PendingPlacement.Above, form.Display, form.Tail);
    }

    /// <summary>
    /// Enter on the pending block below a block: a pending new item ends the list (a paragraph after the list, in the parent item for a nested
    /// list), a pending paragraph in a quote ends the quote. A pending paragraph outside any container changes nothing. Null when the pending is
    /// neither, so that Enter acts at the caret.
    /// </summary>
    private static ViewEditResult? EndPending(RenderDocument doc, int index, ViewEditResult.Pending held)
    {
        RenderBlock block = doc.Blocks[index];
        int n = block.Containers.Length;
        if (n > 0 && IsItem(block.Containers[^1]) && held.SourceText == BelowForm(doc, index, true, n - 1).Source)
        {
            Form ended = BelowForm(doc, index, false, n - 1);
            return new ViewEditResult.Pending(held.Offset, ended.Source, PendingPlacement.Below, ended.Display);
        }

        if (n > 0 && IsQuote(block.Containers[^1]) && held.SourceText == BelowForm(doc, index, false, n).Source)
        {
            Form ended = BelowForm(doc, index, false, n - 1);
            return new ViewEditResult.Pending(held.Offset, ended.Source, PendingPlacement.Below, ended.Display);
        }

        return n == 0 && held.SourceText == BelowForm(doc, index, false, 0).Source ? held : null;
    }

    /// <summary>
    /// The text of a block below a block's end, as the forms of Enter write it: the separator and the prefix of the new line (for a new item, the
    /// marker too), the text the view draws for it, and the containers the new block would have (identities kept from the block's own containers).
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="index">The index of the block the new one follows.</param>
    /// <param name="newItem">Whether the new block is an item of the block's innermost list.</param>
    /// <param name="keep">The number of the block's containers the new block keeps, when it is not a new item.</param>
    private static Form BelowForm(RenderDocument doc, int index, bool newItem, int keep)
    {
        RenderBlock block = doc.Blocks[index];
        ImmutableArray<BlockShape> shapes = BlockShape.Of(doc);
        string newline = LineEnding.Of(doc.Text);
        if (!newItem)
        {
            ImmutableArray<Container> chain = block.Containers[..keep];
            return new Form(EditSource.Separator(newline, chain, tightItems: false) + EditSource.Continuation(chain), string.Empty,
                EditSource.Display(Unmarked(chain), showMarker: false, task: false), shapes[index].Containers[..keep]);
        }

        ImmutableArray<Container> outer = block.Containers[..^1];
        int first = FirstLeafIndex(doc, index);
        Container entry = block.Containers[^1];
        string marker = entry.Kind == ContainerKind.OrderedItem ? EditSource.NextMarker(entry.Marker!) : BulletChar(doc, first).ToString();
        bool task = IsTaskItem(doc, index);
        string source = EditSource.Separator(newline, outer, block.Tight) + EditSource.Continuation(outer) + marker + " " + (task ? "[ ] " : string.Empty);
        Container shown = entry.Kind == ContainerKind.OrderedItem
            ? Container.Ordered(0, EditSource.NextMarker(entry.Marker!), isFirstLeaf: true)
            : Container.Bullet(entry.BulletDepth, isFirstLeaf: true);
        string display = EditSource.Display(Unmarked(outer).Add(shown), showMarker: true, task);
        return new Form(source, string.Empty, display, shapes[index].Containers[..^1].Add(NewContainer(entry, marker, index + 1, outer.Length)));
    }

    /// <summary>
    /// The text of a pending block above a block's start: the separator and the prefix of the block's own line, which the pending writes after the
    /// typed text, and the containers and display of the new block. A new item before a list item's first text takes the item's marker.
    /// </summary>
    private static Form AboveForm(RenderDocument doc, int index, bool newItem)
    {
        RenderBlock block = doc.Blocks[index];
        ImmutableArray<BlockShape> shapes = BlockShape.Of(doc);
        string newline = LineEnding.Of(doc.Text);
        int n = block.Containers.Length;
        ImmutableArray<Container> chain = newItem ? block.Containers[..^1] : block.Containers;
        string tail = EditSource.Separator(newline, chain, tightItems: newItem && block.Tight) + EditSite.LinePrefix(doc.Text, block.Start);
        if (!newItem)
        {
            return new Form(string.Empty, tail, EditSource.Display(Unmarked(chain), showMarker: false, task: false), shapes[index].Containers);
        }

        Container entry = block.Containers[^1];
        Container shown = entry with { IsFirstLeaf = true };
        string display = EditSource.Display(Unmarked(chain).Add(shown), showMarker: true, task: IsTaskItem(doc, index));
        return new Form(string.Empty, tail, display, shapes[index].Containers[..(n - 1)].Add(NewContainer(entry, entry.Marker, index, n - 1)));
    }

    /// <summary>
    /// A form of a new block: the source written at the caret (below) or the tail written after the typed text (above), the display, and the
    /// containers the new block has.
    /// </summary>
    /// <param name="Source">The source written before the typed text.</param>
    /// <param name="Tail">The source written after the typed text.</param>
    /// <param name="Display">The text the view draws for the pending line.</param>
    /// <param name="Chain">The containers of the new block, with identities.</param>
    private readonly record struct Form(string Source, string Tail, string Display, ImmutableArray<ShapeContainer> Chain)
    {
    }
}
