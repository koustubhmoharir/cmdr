using Mdedit.Core.Render;

namespace Mdedit.Core.Editing;

/// <summary>
/// The rendered-view edits of M6a: typing, Backspace and Delete inside a block, and replacing a selection inside one block. Each is a pure function
/// of the document and the caret or selection, given as source offsets: the offsets the view holds, which are a cell's offset or a block's end
/// position (see <c>RenderedView.CursorOffset</c>). Each returns one replacement of the source, or a pending insertion, or a refusal; it is
/// verified by reparsing (see <see cref="EditVerifier"/>) before it is returned.
/// </summary>
/// <remarks>
/// Typed text goes in front of the text of the stop the caret is on: after the previous mapped cell of its run, so it takes that cell's style,
/// except that a link is not grown at its end. Text typed in a code span, or in a code, HTML or raw block, is written as it is; inline text is
/// escaped (see <see cref="Escaper"/>). Typing in a table, a selection across blocks and the block edges belong to later milestones and are refused.
/// </remarks>
public static class ViewEdit
{
    /// <summary>
    /// Types text at the caret.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="caret">The caret's source offset: a cell's offset, or a block's end position.</param>
    /// <param name="typed">The typed text, one character (or grapheme) from the keyboard. A line break is refused.</param>
    /// <param name="pending">
    /// The pending insertion the view holds at <paramref name="caret"/>, from an earlier result, or null. Its text is written in front of
    /// <paramref name="typed"/> as one edit. A pending insertion at another offset is ignored.
    /// </param>
    /// <returns>
    /// A replacement, or a <see cref="ViewEditResult.Pending"/> when <paramref name="typed"/> would not show (a space at a block's start or end,
    /// or after one), or a refusal.
    /// </returns>
    public static ViewEditResult Insert(RenderDocument doc, int caret, string typed, ViewEditResult.Pending? pending = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(typed);
        if (typed.Contains('\n') || typed.Contains('\r'))
        {
            return Refuse(EditMessages.LineBreak);
        }

        if (EditSite.Find(doc, caret) is not { } site)
        {
            return Refuse(EditMessages.NoPlace);
        }

        RenderBlock block = doc.Blocks[site.BlockIndex];
        if (block.Kind == BlockKind.Table)
        {
            return Refuse(EditMessages.Table);
        }

        Run run = block.Runs[site.Run];
        RemovalSet none = RemovalSet.Of(doc, block, [], trimEdges: false);
        int previous = EditSite.PreviousMapped(run, site.Cell, none, site.Run);
        int next = site.Cell < run.Cells.Length ? site.Cell : -1;
        int at = EditSite.InsertionPoint(doc, block, run, previous, next);
        string held = pending is not null && pending.Offset == caret ? pending.SourceText : string.Empty;
        return Write(new EditRequest(doc, site.BlockIndex, caret, none, at, typed, held));
    }

    /// <summary>
    /// Backspace: removes the mapped cell before the caret, with its escape and its entity whole.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="caret">The caret's source offset.</param>
    /// <returns>A replacement or a refusal; at the start of a block's line (no mapped cell before the caret in its run) the refusal is M6b.</returns>
    public static ViewEditResult DeleteBackward(RenderDocument doc, int caret)
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

        int previous = EditSite.PreviousMapped(block.Runs[site.Run], site.Cell, RemovalSet.Of(doc, block, [], trimEdges: false), site.Run);
        return previous < 0 ? Refuse(EditMessages.BackspaceAtStart) : Delete(doc, caret, site.BlockIndex, block, [(site.Run, previous)]);
    }

    /// <summary>
    /// Delete: removes the mapped cell at the caret, with its escape and its entity whole.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="caret">The caret's source offset.</param>
    /// <returns>A replacement or a refusal; at the end position of a block the refusal is M6b.</returns>
    public static ViewEditResult DeleteForward(RenderDocument doc, int caret)
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

        return site.Cell >= block.Runs[site.Run].Cells.Length
            ? Refuse(EditMessages.DeleteAtEnd)
            : Delete(doc, caret, site.BlockIndex, block, [(site.Run, site.Cell)]);
    }

    /// <summary>
    /// Replaces the text between two source offsets with typed text, or deletes it when the text is empty. The selection must lie in one block
    /// and one line of it (no hard break between its cells). The typed text is written where the selection started, as typing there would.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="start">The selection's start (either end may be given first).</param>
    /// <param name="end">The selection's end.</param>
    /// <param name="typed">The text that replaces the selection; empty deletes it.</param>
    /// <returns>A replacement, a pending insertion (for an invisible typed space with nothing removed), or a refusal.</returns>
    public static ViewEditResult ReplaceSelection(RenderDocument doc, int start, int end, string typed)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(typed);
        (start, end) = start <= end ? (start, end) : (end, start);
        if (typed.Contains('\n') || typed.Contains('\r'))
        {
            return Refuse(EditMessages.LineBreak);
        }

        if (start == end)
        {
            return typed.Length == 0 ? Refuse(EditMessages.NothingSelected) : Insert(doc, start, typed);
        }

        if (EditSite.Find(doc, start) is not { } first || EditSite.Find(doc, end) is not { } last)
        {
            return Refuse(EditMessages.NoPlace);
        }

        if (first.BlockIndex != last.BlockIndex)
        {
            return Refuse(EditMessages.AcrossBlocks);
        }

        RenderBlock block = doc.Blocks[first.BlockIndex];
        if (block.Kind == BlockKind.Table)
        {
            return Refuse(EditMessages.Table);
        }

        List<(int Run, int Cell)> selected = SelectedCells(block, start, end);
        if (selected.Count == 0)
        {
            return typed.Length == 0 ? Refuse(EditMessages.NothingSelected) : Insert(doc, start, typed);
        }

        if (selected.Select(position => position.Run).Distinct().Count() > 1)
        {
            return Refuse(EditMessages.AcrossLines);
        }

        RemovalSet removals = RemovalSet.Of(doc, block, selected, typed.Length == 0);
        int run = selected[0].Run;
        int previous = EditSite.PreviousMapped(block.Runs[run], selected[0].Cell, removals, run);
        int next = EditSite.NextMapped(block.Runs[run], selected[^1].Cell, removals, run);
        int at = EditSite.InsertionPoint(doc, block, block.Runs[run], previous, next);
        return Write(new EditRequest(doc, first.BlockIndex, start, removals, at, typed, string.Empty));
    }

    /// <summary>A deletion of the given cells, written at the start of what it removes.</summary>
    private static ViewEditResult Delete(RenderDocument doc, int caret, int blockIndex, RenderBlock block, IEnumerable<(int Run, int Cell)> cells)
    {
        RemovalSet removals = RemovalSet.Of(doc, block, cells);
        return Write(new EditRequest(doc, blockIndex, caret, removals, removals.Start, string.Empty, string.Empty));
    }

    /// <summary>The mapped cells of a block whose offsets are in <c>[start, end)</c>, in order.</summary>
    private static List<(int Run, int Cell)> SelectedCells(RenderBlock block, int start, int end)
    {
        var selected = new List<(int Run, int Cell)>();
        for (int run = 0; run < block.Runs.Length; run++)
        {
            for (int cell = 0; cell < block.Runs[run].Cells.Length; cell++)
            {
                Cell owner = block.Runs[run].Cells[cell];
                if (owner.IsMapped && block.Start + owner.Offset >= start && block.Start + owner.Offset < end)
                {
                    selected.Add((run, cell));
                }
            }
        }

        return selected;
    }

    /// <summary>
    /// Makes one edit: removes the request's ranges, inserts the escaped typed text at its insertion point, reparses, verifies and returns the
    /// changed middle of the text.
    /// </summary>
    private static ViewEditResult Write(EditRequest request)
    {
        RemovalSet removals = request.Removals;
        if (removals.Covers(request.At))
        {
            return Refuse(EditMessages.CantEdit);
        }

        string before = removals.Apply(request.Doc.Text);
        int anchor = removals.Shift(request.At);
        RenderBlock block = request.Doc.Blocks[request.BlockIndex];
        string prefix = EditSite.LinePrefix(before, anchor) + request.Held;
        string inserted = request.Held + Escaper.Escape(request.Typed, prefix, EditSite.IsRaw(block, request.At));
        string text = before.Insert(anchor, inserted);
        RenderDocument after = RenderDocument.Parse(text);

        int end = anchor + inserted.Length;
        if (request.Typed == " " && removals.IsEmpty && EditVerifier.Holds(request.Doc, after, request.BlockIndex, string.Empty, [string.Empty], end))
        {
            return new ViewEditResult.Pending(request.Caret, inserted);
        }

        if (after.Blocks.Length != request.Doc.Blocks.Length && request.Typed.Length == 0)
        {
            return Refuse(EditMessages.BlockEmptied);
        }

        if (!EditVerifier.Holds(request.Doc, after, request.BlockIndex, removals.RemovedText, Visible(request), end))
        {
            return Refuse(EditMessages.CantEdit);
        }

        (int start, int length, string replacement) = TextChange.Of(request.Doc.Text, text);
        return new ViewEditResult.Replacement(start, length, replacement, end);
    }

    /// <summary>
    /// The plain texts an edit may show in place of the typed text: the typed text with the held text in front, or, for a replacement that
    /// types a space, the space itself or nothing (a space at the new edge of a block does not show).
    /// </summary>
    private static string[] Visible(EditRequest request)
    {
        if (request.Typed.Length == 0)
        {
            return [string.Empty];
        }

        if (request.Typed == " " && !request.Removals.IsEmpty)
        {
            return [request.Typed, string.Empty];
        }

        return [request.Held + request.Typed, request.Typed];
    }

    /// <summary>A refusal with a message.</summary>
    private static ViewEditResult Refuse(string message) => new ViewEditResult.Refusal(message);

    /// <summary>What one edit writes.</summary>
    /// <param name="Doc">The document before the edit.</param>
    /// <param name="BlockIndex">The index of the edited block.</param>
    /// <param name="Caret">The caret's offset (the offset a pending insertion is held at).</param>
    /// <param name="Removals">The source the edit removes.</param>
    /// <param name="At">The insertion point, in the source before the edit; for a deletion, where the removal starts.</param>
    /// <param name="Typed">The typed text; empty for a deletion.</param>
    /// <param name="Held">The source of a pending insertion written in front of the typed text.</param>
    private sealed record EditRequest(RenderDocument Doc, int BlockIndex, int Caret, RemovalSet Removals, int At, string Typed, string Held);
}
