using Mdedit.Core.Render;
using Mdedit.Core.Tables;

namespace Mdedit.Core.Editing;

/// <summary>
/// Edits in a table cell: the cell's source is edited as a paragraph's text is, then written through <see cref="TableEdit"/>, so that an aligned
/// table stays aligned and a minimal one changes only the cell; and Enter, which moves to the same column of the next row or adds a row.
/// </summary>
public static partial class ViewEdit
{
    /// <summary>The result of rewriting one cell: the table's replacement, the caret in the rewritten cell, and the document after it.</summary>
    /// <param name="Start">The offset of the replaced text in the source before the edit.</param>
    /// <param name="Length">The number of characters replaced.</param>
    /// <param name="Text">The text that replaces them.</param>
    /// <param name="Caret">The caret's offset in the source after the edit: after the written text in the cell.</param>
    /// <param name="After">The document parsed from the source after the edit.</param>
    /// <param name="Content">The cell's new source content, without its padding.</param>
    private readonly record struct CellWrite(int Start, int Length, string Text, int Caret, RenderDocument After, string Content);

    /// <summary>
    /// Writes an edit of one cell: <paramref name="inserted"/> goes in at <paramref name="anchor"/> of the source <paramref name="before"/> (which has
    /// the removals of the edit made), and the cell's new source is written with <see cref="TableEdit.SetCellSource"/>. The caret goes after the
    /// inserted text, found in the rewritten table's source.
    /// </summary>
    /// <param name="doc">The document before the edit.</param>
    /// <param name="index">The index of the table block.</param>
    /// <param name="run">The cell's run.</param>
    /// <param name="removals">The removals the edit makes in the cell.</param>
    /// <param name="before">The source with the removals made.</param>
    /// <param name="anchor">The insertion point, in <paramref name="before"/>.</param>
    /// <param name="inserted">The source inserted at <paramref name="anchor"/>; empty for a deletion.</param>
    /// <returns>The rewrite, or null when the table edit is refused or the rewritten table is not the table the edit meant.</returns>
    private static CellWrite? RewriteCell(RenderDocument doc, int index, int run, RemovalSet removals, string before, int anchor, string inserted)
    {
        RenderBlock block = doc.Blocks[index];
        int columns = block.Table!.Alignments.Length;
        int row = run / columns;
        int column = run % columns;
        TableSource source = TableSource.Parse(doc.Text, block);
        if (column >= source.Row(row).Contents.Length)
        {
            return null;
        }

        SourceRange old = source.Row(row).Contents[column];
        int start = old.Start;
        string after = before.Insert(anchor, inserted);
        int end = removals.Shift(old.End) + inserted.Length;
        string content = after[start..end];
        TableEditResult edit = TableEdit.SetCellSource(doc.Text, block, row, column, content);
        if (edit.IsRefused)
        {
            return null;
        }

        string text = string.Concat(doc.Text.AsSpan(0, edit.Start), edit.Text, doc.Text.AsSpan(edit.Start + edit.Length));
        RenderDocument parsed = RenderDocument.Parse(text);
        if (parsed.Blocks.Length != doc.Blocks.Length || parsed.Blocks[index].Kind != BlockKind.Table)
        {
            return null;
        }

        // The written content has the leading spaces the table's padding takes, so the caret is counted from the first character of the content.
        int lead = content.Length - content.TrimStart(' ', '\t').Length;
        SourceRange written = TableSource.Parse(text, parsed.Blocks[index]).Row(row).Contents[column];
        int caret = written.Start + Math.Max(0, anchor + inserted.Length - start - lead);
        return new CellWrite(edit.Start, edit.Length, edit.Text, caret, parsed, content);
    }

    /// <summary>
    /// A typed edit of a table cell: the cell's source is rewritten, and the edit is verified by the rendering (the typed text, or the held one, is
    /// what shows). A typed space that would not show at the cell's edge is held as a pending insertion instead.
    /// </summary>
    /// <param name="request">The edit.</param>
    /// <param name="before">The source with the removals made.</param>
    /// <param name="anchor">The insertion point, in <paramref name="before"/>.</param>
    /// <param name="inserted">The held source and the typed text's source.</param>
    private static ViewEditResult WriteCell(EditRequest request, string before, int anchor, string inserted)
    {
        CellWrite? written = RewriteCell(request.Doc, request.BlockIndex, request.Run, request.Removals, before, anchor, inserted);
        if (written is not { } cell)
        {
            return Refuse(EditMessages.CantEdit);
        }

        if (request.Typed == " " && request.Removals.IsEmpty && cell.Content.Trim(' ', '\t') == CellSource(request.Doc, request.BlockIndex, request.Run).Trim(' ', '\t'))
        {
            return new ViewEditResult.Pending(request.Caret, inserted);
        }

        if (!EditVerifier.Holds(request.Doc, cell.After, request.BlockIndex, request.Removals.RemovedText, Visible(request), cell.Caret))
        {
            return Refuse(EditMessages.CantEdit);
        }

        return new ViewEditResult.Replacement(cell.Start, cell.Length, cell.Text, cell.Caret);
    }

    /// <summary>The source content of a table cell as the document has it, without its padding.</summary>
    /// <param name="doc">The document.</param>
    /// <param name="index">The index of the table block.</param>
    /// <param name="run">The cell's run.</param>
    private static string CellSource(RenderDocument doc, int index, int run)
    {
        RenderBlock block = doc.Blocks[index];
        int columns = block.Table!.Alignments.Length;
        return TableSource.Parse(doc.Text, block).Row(run / columns).Contents[run % columns].Slice(doc.Text);
    }

    /// <summary>
    /// The caret stop of a table cell: its first mapped cell's offset, or for an empty cell its place between its pipes. -1 when the cell has no
    /// stop (its row has no such column).
    /// </summary>
    /// <param name="block">The table block.</param>
    /// <param name="run">The cell's run.</param>
    private static int CellStop(RenderBlock block, int run)
    {
        foreach (Cell cell in block.Runs[run].Cells)
        {
            if (cell.IsMapped)
            {
                return block.Start + cell.Offset;
            }
        }

        return EditSite.EmptyCellOffset(block, run);
    }

    /// <summary>
    /// Enter in a table cell: on a row above the last one, the caret moves to the same column of the next row (<see cref="ViewEditResult.Move"/>).
    /// On the last row, a row is added below it, and the caret goes to the same column of that row, as one replacement.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="site">The cell's stop.</param>
    private static ViewEditResult EnterInCell(RenderDocument doc, Site site)
    {
        RenderBlock block = doc.Blocks[site.BlockIndex];
        int columns = block.Table!.Alignments.Length;
        int row = site.Run / columns;
        int column = site.Run % columns;
        int rows = block.Table.Rows.Length;
        if (row + 1 < rows)
        {
            int next = CellStop(block, site.Run + columns);
            return next < 0 ? Refuse(EditMessages.CantEdit) : new ViewEditResult.Move(next);
        }

        TableEditResult added = TableEdit.InsertRow(doc.Text, block, rows);
        if (added.IsRefused)
        {
            return Refuse(added.Refusal!);
        }

        string text = string.Concat(doc.Text.AsSpan(0, added.Start), added.Text, doc.Text.AsSpan(added.Start + added.Length));
        RenderDocument after = RenderDocument.Parse(text);
        if (after.Blocks.Length != doc.Blocks.Length)
        {
            return Refuse(EditMessages.CantEdit);
        }

        int caret = CellStop(after.Blocks[site.BlockIndex], rows * columns + column);
        return caret < 0 ? Refuse(EditMessages.CantEdit) : new ViewEditResult.Replacement(added.Start, added.Length, added.Text, caret);
    }
}
