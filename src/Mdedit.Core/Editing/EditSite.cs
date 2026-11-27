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
            if (run.Cells[index].IsMapped && !removals.Removes(runIndex, index))
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
