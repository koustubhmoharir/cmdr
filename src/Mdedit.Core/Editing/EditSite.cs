using Mdedit.Core.Render;

namespace Mdedit.Core.Editing;

/// <summary>
/// A caret stop of the document: the cell at the caret, or the end position of a block (the stop after its last mapped cell).
/// </summary>
/// <param name="BlockIndex">The index of the block in the document.</param>
/// <param name="Run">The index of the run in the block.</param>
/// <param name="Cell">The index of the cell in the run, or the run's cell count for the end position.</param>
internal sealed record Site(int BlockIndex, int Run, int Cell);

/// <summary>
/// Finds the stop a caret offset is on, and works out where typed text goes: the insertion point of a stop is right after the previous mapped
/// cell of its run (so the text joins that cell's style), or at the stop's own cell when there is no previous one, and after a link when that
/// cell is the link's last one.
/// </summary>
internal static class EditSite
{
    /// <summary>
    /// The stop at a source offset: a mapped cell whose offset is the offset, else the end position of a block whose end is the offset. Null when
    /// no stop is there.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="offset">The absolute source offset of the caret.</param>
    public static Site? Find(RenderDocument doc, int offset)
    {
        for (int b = 0; b < doc.Blocks.Length; b++)
        {
            RenderBlock block = doc.Blocks[b];
            for (int r = 0; r < block.Runs.Length; r++)
            {
                for (int c = 0; c < block.Runs[r].Cells.Length; c++)
                {
                    if (block.Runs[r].Cells[c].IsMapped && block.Start + block.Runs[r].Cells[c].Offset == offset)
                    {
                        return new Site(b, r, c);
                    }
                }
            }
        }

        for (int b = 0; b < doc.Blocks.Length; b++)
        {
            RenderBlock block = doc.Blocks[b];
            for (int r = 0; r < block.Runs.Length; r++)
            {
                if (EmptyCellOffset(block, r) == offset)
                {
                    return new Site(b, r, 0);
                }
            }
        }

        for (int b = 0; b < doc.Blocks.Length; b++)
        {
            RenderBlock block = doc.Blocks[b];
            for (int r = 0; r < block.Runs.Length; r++)
            {
                Run run = block.Runs[r];
                int k = Array.FindLastIndex(run.Cells.ToArray(), cell => cell.IsMapped && cell.Style.Role != StyleRole.TaskBox);
                if (k >= 0 && block.Start + run.Cells[k].Offset + SourceLength.Of(doc, block, run, k) == offset)
                {
                    return new Site(b, r, run.Cells.Length);
                }
            }
        }

        for (int b = 0; b < doc.Blocks.Length; b++)
        {
            RenderBlock block = doc.Blocks[b];
            if (LastMapped(block) is not { } last)
            {
                continue;
            }

            Run run = block.Runs[last.Run];
            if (block.Start + run.Cells[last.Cell].Offset + SourceLength.Of(doc, block, run, last.Cell) == offset)
            {
                return new Site(b, last.Run, run.Cells.Length);
            }
        }

        return null;
    }

    /// <summary>
    /// The index of the block a source offset lies in, both ends included, so that a selection's end that is no caret stop (just before a closing
    /// marker, say) still resolves to its block. -1 when no block holds it, as in the blank lines between blocks.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="offset">The absolute source offset.</param>
    public static int BlockAt(RenderDocument doc, int offset)
    {
        for (int b = 0; b < doc.Blocks.Length; b++)
        {
            if (doc.Blocks[b].Start <= offset && offset <= doc.Blocks[b].End)
            {
                return b;
            }
        }

        return -1;
    }

    /// <summary>
    /// The run of a table block that a source offset belongs to: the cell whose stop it is (a mapped cell or an empty cell's place), else the cell
    /// of the last mapped character before it, else the first cell. Used to tell whether a selection is inside one cell.
    /// </summary>
    /// <param name="block">A table block.</param>
    /// <param name="offset">The absolute source offset.</param>
    public static int CellAt(RenderBlock block, int offset)
    {
        for (int r = 0; r < block.Runs.Length; r++)
        {
            if (block.Runs[r].Cells.Any(cell => cell.IsMapped && block.Start + cell.Offset == offset) || EmptyCellOffset(block, r) == offset)
            {
                return r;
            }
        }

        int run = 0;
        int last = int.MinValue;
        for (int r = 0; r < block.Runs.Length; r++)
        {
            foreach (Cell cell in block.Runs[r].Cells.Where(cell => cell.IsMapped))
            {
                int at = block.Start + cell.Offset;
                if (at < offset && at > last)
                {
                    last = at;
                    run = r;
                }
            }
        }

        return run;
    }

    /// <summary>
    /// The absolute offset of the caret stop of an empty table cell: its place between its pipes, where the layout draws a blank for it. -1 when
    /// the run is not an empty cell of a table, or the row has no such column.
    /// </summary>
    /// <param name="block">The block that owns the run.</param>
    /// <param name="run">The index of the run; a table's runs are its cells, row by row.</param>
    public static int EmptyCellOffset(RenderBlock block, int run)
    {
        if (block.Table is not { } table || block.Runs[run].Cells.Any(cell => cell.IsMapped))
        {
            return -1;
        }

        int columns = table.Alignments.Length;
        TableRowModel row = table.Rows[run / columns];
        int position = row.Positions.IsDefault || run % columns >= row.Positions.Length ? Cell.Synthetic : row.Positions[run % columns];
        return position == Cell.Synthetic ? -1 : block.Start + position;
    }

    /// <summary>
    /// The index of the mapped cell before <paramref name="before"/> in the run that is not removed, or -1 when there is none.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="before">The index that the cell must precede.</param>
    /// <param name="removals">The removal the edit makes, whose cells do not count.</param>
    /// <param name="runIndex">The index of the run in its block.</param>
    public static int PreviousMapped(Run run, int before, RemovalSet removals, int runIndex)
    {
        for (int index = Math.Min(before, run.Cells.Length) - 1; index >= 0; index--)
        {
            if (run.Cells[index].IsMapped && run.Cells[index].Style.Role != StyleRole.TaskBox && !removals.Removes(runIndex, index))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// The index of the mapped cell after <paramref name="after"/> in the run that is not removed, or -1 when there is none.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="after">The index that the cell must follow.</param>
    /// <param name="removals">The removal the edit makes, whose cells do not count.</param>
    /// <param name="runIndex">The index of the run in its block.</param>
    public static int NextMapped(Run run, int after, RemovalSet removals, int runIndex)
    {
        for (int index = after + 1; index < run.Cells.Length; index++)
        {
            if (run.Cells[index].IsMapped && !removals.Removes(runIndex, index))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// The absolute source offset where text is inserted between a previous and a next mapped cell of a run (either may be -1). After the
    /// previous cell's source, unless that cell is in a link the next cell is not in: then after the link, so a link never grows at its end.
    /// With no previous cell, at the next cell's offset; with neither, at the run's start.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="block">The block that owns the run.</param>
    /// <param name="run">The run.</param>
    /// <param name="previous">The index of the mapped cell before the insertion point, or -1.</param>
    /// <param name="next">The index of the mapped cell after it, or -1.</param>
    public static int InsertionPoint(RenderDocument doc, RenderBlock block, Run run, int previous, int next)
    {
        if (previous < 0)
        {
            return block.Start + (next >= 0 ? run.Cells[next].Offset : run.Start);
        }

        Cell cell = run.Cells[previous];
        if (cell.LinkId >= 0 && (next < 0 || run.Cells[next].LinkId != cell.LinkId))
        {
            return block.Links[cell.LinkId].End;
        }

        return block.Start + cell.Offset + SourceLength.Of(doc, block, run, previous);
    }

    /// <summary>
    /// Whether text written at a source offset is raw: inside a code span, or in a block whose text is its source (code, HTML, a raw block).
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="at">The absolute source offset.</param>
    public static bool IsRaw(RenderBlock block, int at) =>
        block.Kind is BlockKind.Code or BlockKind.Html or BlockKind.Raw
        || block.InlineSpans.Any(span => span.Kind == InlineSpanKind.Code && block.Start + span.InnerStart <= at && at <= block.Start + span.InnerEnd);

    /// <summary>
    /// The offset an insertion at a cell's source must use: before the backslash of an escape, so that the escape stays whole.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="block">The block the cell is in.</param>
    /// <param name="absolute">The absolute source offset of the cell.</param>
    public static int EscapeStart(RenderDocument doc, RenderBlock block, int absolute) =>
        absolute - 1 >= block.Start && doc.Text[absolute - 1] == '\\' && !IsMappedAt(block, absolute - 1) ? absolute - 1 : absolute;

    /// <summary>Whether a mapped cell of the block is at an absolute offset.</summary>
    private static bool IsMappedAt(RenderBlock block, int absolute) =>
        block.Runs.Any(run => run.Cells.Any(cell => cell.IsMapped && block.Start + cell.Offset == absolute));

    /// <summary>The source of the line from its start to <paramref name="at"/>.</summary>
    /// <param name="text">The source text.</param>
    /// <param name="at">The offset, on the line.</param>
    public static string LinePrefix(string text, int at)
    {
        int start = at;
        while (start > 0 && text[start - 1] is not ('\n' or '\r'))
        {
            start--;
        }

        return text[start..at];
    }

    /// <summary>The last mapped cell of a block, as (run, cell), or null when the block has no mapped cell.</summary>
    private static (int Run, int Cell)? LastMapped(RenderBlock block)
    {
        for (int r = block.Runs.Length - 1; r >= 0; r--)
        {
            for (int c = block.Runs[r].Cells.Length - 1; c >= 0; c--)
            {
                if (block.Runs[r].Cells[c].IsMapped)
                {
                    return (r, c);
                }
            }
        }

        return null;
    }
}
