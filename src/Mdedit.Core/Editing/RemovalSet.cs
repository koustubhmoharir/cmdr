using System.Collections.Immutable;
using System.Text;
using Mdedit.Core.Render;

namespace Mdedit.Core.Editing;

/// <summary>
/// The source an edit removes from one block: the source of each removed cell (with the backslash of an escape), and each inline container
/// that is left with no text, with its delimiters. The ranges are merged and sorted, so they are the changed parts of the source.
/// </summary>
internal sealed class RemovalSet
{
    private readonly ImmutableArray<(int Run, int Cell)> _cells;
    private readonly ImmutableArray<SourceRange> _ranges;
    private readonly string _removedText;

    private RemovalSet(RenderDocument doc, RenderBlock block, IEnumerable<(int Run, int Cell)> cells, bool trimEdges, IEnumerable<SourceRange> breaks)
    {
        IEnumerable<(int Run, int Cell)> all = trimEdges ? WithEdgeSpaces(block, cells) : cells;
        _cells = all.OrderBy(cell => cell.Run).ThenBy(cell => cell.Cell).ToImmutableArray();
        var ranges = _cells.Select(cell => CellRange(doc, block, cell.Run, cell.Cell)).ToList();
        ranges.AddRange(breaks);
        ranges.AddRange(EmptiedSpans(block));
        _ranges = Merge(ranges);
        var text = new StringBuilder();
        foreach ((int run, int cell) in _cells)
        {
            text.Append(block.Runs[run].Cells[cell].Text);
        }

        _removedText = text.ToString();
    }

    /// <summary>The removal of the given cells of a block, with the inline containers they leave empty.</summary>
    /// <param name="doc">The document.</param>
    /// <param name="block">The block that owns the cells.</param>
    /// <param name="cells">The cells, as (run, cell index); none is synthetic.</param>
    /// <param name="trimEdges">
    /// Whether the spaces that would lead or trail the text after the removal go too (see <see cref="WithEdgeSpaces"/>). Not when text is typed
    /// in the removal's place, since that text then sits at the edge.
    /// </param>
    /// <param name="breaks">Source ranges removed as well: the line breaks between the runs of a selection across lines.</param>
    public static RemovalSet Of(RenderDocument doc, RenderBlock block, IEnumerable<(int Run, int Cell)> cells, bool trimEdges = true, IEnumerable<SourceRange>? breaks = null) =>
        new(doc, block, cells, trimEdges, breaks ?? []);

    /// <summary>
    /// The cells to remove with the given ones: in a paragraph or heading, the spaces left at either edge of the text too. Markdown strips
    /// them, so they would not show, and the plain text the edit leaves has to be the text without them.
    /// </summary>
    private static IEnumerable<(int Run, int Cell)> WithEdgeSpaces(RenderBlock block, IEnumerable<(int Run, int Cell)> cells)
    {
        var removed = new HashSet<(int Run, int Cell)>(cells);
        if (block.Kind is not (BlockKind.Paragraph or BlockKind.Heading))
        {
            return removed;
        }

        List<(int Run, int Cell)> mapped = MappedPositions(block).ToList();
        TrimEdge(block, mapped, removed, forward: true);
        TrimEdge(block, mapped, removed, forward: false);
        return removed;
    }

    /// <summary>Adds the spaces at one edge of the text, from the first (or last) mapped cell inward, until a cell that is not a space.</summary>
    private static void TrimEdge(RenderBlock block, List<(int Run, int Cell)> mapped, HashSet<(int Run, int Cell)> removed, bool forward)
    {
        for (int index = forward ? 0 : mapped.Count - 1; index >= 0 && index < mapped.Count; index += forward ? 1 : -1)
        {
            (int run, int cell) = mapped[index];
            if (removed.Contains((run, cell)))
            {
                continue;
            }

            if (block.Runs[run].Cells[cell].Text != " ")
            {
                return;
            }

            removed.Add((run, cell));
        }
    }

    /// <summary>The changed ranges of the source, merged and in order. Empty when nothing is removed.</summary>
    public ImmutableArray<SourceRange> Ranges => _ranges;

    /// <summary>Whether nothing is removed.</summary>
    public bool IsEmpty => _ranges.IsEmpty;

    /// <summary>The offset where the first removed range starts, or -1 when nothing is removed.</summary>
    public int Start => _ranges.IsEmpty ? -1 : _ranges[0].Start;

    /// <summary>The rendered text of the removed cells, in order: what the plain text of the block loses.</summary>
    public string RemovedText => _removedText;

    /// <summary>Whether a cell is removed.</summary>
    /// <param name="run">The run of the cell.</param>
    /// <param name="cell">The index of the cell in its run.</param>
    public bool Removes(int run, int cell) => _cells.Contains((run, cell));

    /// <summary>Whether an offset is strictly inside a removed range, where text cannot be inserted.</summary>
    /// <param name="offset">The absolute source offset.</param>
    public bool Covers(int offset) => _ranges.Any(range => range.Start < offset && offset < range.End);

    /// <summary>The offset in the source after the removals of the text before <paramref name="offset"/>.</summary>
    /// <param name="offset">An absolute source offset that is not inside a removed range.</param>
    public int Shift(int offset)
    {
        int removed = 0;
        foreach (SourceRange range in _ranges)
        {
            removed += Math.Clamp(offset - range.Start, 0, range.End - range.Start);
        }

        return offset - removed;
    }

    /// <summary>The text with the removed ranges taken out.</summary>
    /// <param name="text">The source text the ranges are offsets in.</param>
    public string Apply(string text)
    {
        var result = new StringBuilder(text.Length);
        int position = 0;
        foreach (SourceRange range in _ranges)
        {
            result.Append(text, position, range.Start - position);
            position = range.End;
        }

        return result.Append(text, position, text.Length - position).ToString();
    }

    /// <summary>The source range of a cell: its source length, from the backslash of an escape when it has one.</summary>
    private static SourceRange CellRange(RenderDocument doc, RenderBlock block, int run, int cell)
    {
        Run owner = block.Runs[run];
        int absolute = block.Start + owner.Cells[cell].Offset;
        int start = EditSite.EscapeStart(doc, block, absolute);
        int end = absolute + SourceLength.Of(doc, block, owner, cell);
        if (doc.Text[absolute] is '\n' or '\r')
        {
            // A soft line break goes with the next line's prefix and indentation, so that the words join with nothing between.
            end = Math.Max(end, NextTextOffset(block, run, cell) ?? end);
        }

        return new SourceRange(start, end);
    }

    /// <summary>The absolute offset of the next text cell of the block after a cell, or null when there is none.</summary>
    private static int? NextTextOffset(RenderBlock block, int run, int cell)
    {
        for (int r = run; r < block.Runs.Length; r++)
        {
            for (int c = r == run ? cell + 1 : 0; c < block.Runs[r].Cells.Length; c++)
            {
                Cell next = block.Runs[r].Cells[c];
                if (next.IsMapped && next.Style.Role != StyleRole.TaskBox)
                {
                    return block.Start + next.Offset;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The outer ranges of the inline containers whose text the removal takes away and that have no text left: their delimiters go with them.
    /// </summary>
    private IEnumerable<SourceRange> EmptiedSpans(RenderBlock block)
    {
        foreach (InlineSpan span in block.InlineSpans)
        {
            int innerStart = block.Start + span.InnerStart;
            int innerEnd = block.Start + span.InnerEnd;
            if (!_cells.Any(cell => InRange(block, cell.Run, cell.Cell, innerStart, innerEnd)))
            {
                continue;
            }

            bool keepsText = MappedPositions(block).Any(position => !_cells.Contains(position) && InRange(block, position.Run, position.Cell, innerStart, innerEnd));
            if (!keepsText)
            {
                yield return new SourceRange(block.Start + span.OuterStart, block.Start + span.OuterEnd);
            }
        }
    }

    /// <summary>Whether a mapped cell's offset is in the absolute range <c>[start, end)</c>.</summary>
    private static bool InRange(RenderBlock block, int run, int cell, int start, int end)
    {
        Cell owner = block.Runs[run].Cells[cell];
        return owner.IsMapped && block.Start + owner.Offset >= start && block.Start + owner.Offset < end;
    }

    /// <summary>The positions (run, cell index) of the mapped cells of a block, in order.</summary>
    private static IEnumerable<(int Run, int Cell)> MappedPositions(RenderBlock block)
    {
        for (int run = 0; run < block.Runs.Length; run++)
        {
            for (int cell = 0; cell < block.Runs[run].Cells.Length; cell++)
            {
                if (block.Runs[run].Cells[cell].IsMapped)
                {
                    yield return (run, cell);
                }
            }
        }
    }

    /// <summary>Whether some mapped cell of the block is at an absolute offset.</summary>
    private static bool IsMappedAt(RenderBlock block, int absolute) =>
        MappedPositions(block).Any(position => block.Start + block.Runs[position.Run].Cells[position.Cell].Offset == absolute);

    /// <summary>The ranges sorted, with overlapping and touching ones merged.</summary>
    private static ImmutableArray<SourceRange> Merge(List<SourceRange> ranges)
    {
        var merged = ImmutableArray.CreateBuilder<SourceRange>();
        foreach (SourceRange range in ranges.OrderBy(range => range.Start).ThenBy(range => range.End))
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End)
            {
                merged[^1] = new SourceRange(merged[^1].Start, Math.Max(merged[^1].End, range.End));
            }
            else
            {
                merged.Add(range);
            }
        }

        return merged.ToImmutable();
    }
}
