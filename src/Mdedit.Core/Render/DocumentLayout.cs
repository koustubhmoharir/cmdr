using System.Collections.Immutable;

namespace Mdedit.Core.Render;

/// <summary>
/// The display lines of a whole document at a width, with the source map that joins them to the source. Lines are the
/// blocks' lines in order, with one blank separator line between consecutive blocks (none between two tight blocks). A
/// separator belongs to the block before it, so the base offset of every line is the start of its owning block.
/// </summary>
/// <remarks>
/// Columns are screen columns: a cell occupies its width from where it starts, and a zero-width cell has the column of the
/// cell before it. Offsets are absolute in the document. Each block's layout carries its columns and its index of mapped
/// cells, so a lookup by offset is a binary search per block. Immutable.
/// </remarks>
public sealed class DocumentLayout
{
    private readonly ImmutableArray<DisplayLine> _lines;
    private readonly ImmutableArray<ImmutableArray<int>> _columns;
    private readonly ImmutableArray<int> _blockStarts;
    private readonly ImmutableArray<int> _searchStarts;
    private readonly ImmutableArray<int> _firstLines;
    private readonly ImmutableArray<int> _maxEnds;
    private readonly ImmutableArray<BlockLayout> _blocks;

    private DocumentLayout(
        int width,
        ImmutableArray<DisplayLine> lines,
        ImmutableArray<ImmutableArray<int>> columns,
        ImmutableArray<int> blockStarts,
        ImmutableArray<int> searchStarts,
        ImmutableArray<int> firstLines,
        ImmutableArray<int> maxEnds,
        ImmutableArray<BlockLayout> blocks)
    {
        Width = width;
        _lines = lines;
        _columns = columns;
        _blockStarts = blockStarts;
        _searchStarts = searchStarts;
        _firstLines = firstLines;
        _maxEnds = maxEnds;
        _blocks = blocks;
    }

    /// <summary>The screen width the layout was built for.</summary>
    public int Width { get; }

    /// <summary>The number of display lines, separators included.</summary>
    public int LineCount => _lines.Length;

    /// <summary>The layouts of the blocks, in order, as the cache returned them. Not counting the separators.</summary>
    internal ImmutableArray<BlockLayout> Blocks => _blocks;

    /// <summary>
    /// Lays out a document at a width. Each block's layout comes from <paramref name="cache"/>, so that unchanged blocks are not
    /// laid out again.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="width">The full screen width in columns, at least 1.</param>
    /// <param name="cache">The cache of block layouts; the same cache can serve every build.</param>
    public static DocumentLayout Build(RenderDocument doc, int width, LayoutCache cache)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);

        var lines = ImmutableArray.CreateBuilder<DisplayLine>();
        var columns = ImmutableArray.CreateBuilder<ImmutableArray<int>>();
        var starts = ImmutableArray.CreateBuilder<int>(doc.Blocks.Length);
        var searchStarts = ImmutableArray.CreateBuilder<int>(doc.Blocks.Length);
        var firstLines = ImmutableArray.CreateBuilder<int>(doc.Blocks.Length);
        var maxEnds = ImmutableArray.CreateBuilder<int>(doc.Blocks.Length);
        var blocks = ImmutableArray.CreateBuilder<BlockLayout>(doc.Blocks.Length);
        int maxEnd = int.MinValue;

        for (int index = 0; index < doc.Blocks.Length; index++)
        {
            RenderBlock block = doc.Blocks[index];
            BlockLayout layout = cache.Get(doc, block, width);
            firstLines.Add(lines.Count);
            starts.Add(block.Start);
            searchStarts.Add(block.Start + Math.Min(0, layout.MinOffset));
            blocks.Add(layout);
            lines.AddRange(layout.Lines);
            columns.AddRange(layout.Columns);

            maxEnd = Math.Max(maxEnd, block.End);
            maxEnds.Add(maxEnd);

            if (index + 1 < doc.Blocks.Length && !(block.Tight && doc.Blocks[index + 1].Tight))
            {
                // The separator is anchored at the end of the block before it, relative to that block's start.
                ImmutableArray<Cell> separator = LinePrefix.Separator(block, doc.Blocks[index + 1]);
                lines.Add(new DisplayLine(separator, block.End - block.Start, false));
                columns.Add(BlockLayout.ColumnsOf(separator));
            }
        }

        return new DocumentLayout(
            width,
            lines.ToImmutable(),
            columns.ToImmutable(),
            starts.ToImmutable(),
            searchStarts.ToImmutable(),
            firstLines.ToImmutable(),
            maxEnds.ToImmutable(),
            blocks.ToImmutable());
    }

    /// <summary>The display line at an index.</summary>
    /// <param name="index">The line, from 0 to <see cref="LineCount"/> minus 1.</param>
    public DisplayLine Line(int index)
    {
        CheckLine(index);
        return _lines[index];
    }

    /// <summary>
    /// The absolute offset that the relative offsets of a line add to: the start of the block the line belongs to. A separator
    /// belongs to the block before it.
    /// </summary>
    /// <param name="index">The line, from 0 to <see cref="LineCount"/> minus 1.</param>
    public int BaseOffset(int index)
    {
        CheckLine(index);
        return _blockStarts[BlockOf(index)];
    }

    /// <summary>The text of a line: its cell texts joined.</summary>
    /// <param name="line">The line, from 0 to <see cref="LineCount"/> minus 1.</param>
    public string TextOf(int line)
    {
        CheckLine(line);
        return string.Concat(_lines[line].Cells.Select(cell => cell.Text));
    }

    /// <summary>
    /// The absolute source offset of the cell at a column of a line. On a synthetic cell, or past the last cell, it is the next
    /// mapped cell to the right on the line; when there is none, the line's anchor. A column inside a wide cell gives that cell.
    /// </summary>
    /// <param name="line">The line, from 0 to <see cref="LineCount"/> minus 1.</param>
    /// <param name="column">The screen column; negative columns count as 0.</param>
    public int OffsetAt(int line, int column)
    {
        DisplayLine row = Line(line);
        int baseOffset = BaseOffset(line);
        ImmutableArray<Cell> cells = row.Cells;
        ImmutableArray<int> columns = _columns[line];
        int target = Math.Max(0, column);

        int index = -1;
        for (int cell = 0; cell < cells.Length; cell++)
        {
            if (cells[cell].Width > 0 && columns[cell] <= target && target < columns[cell] + cells[cell].Width)
            {
                index = cell;
                break;
            }
        }

        if (index >= 0)
        {
            for (int cell = index; cell < cells.Length; cell++)
            {
                if (cells[cell].IsMapped)
                {
                    return baseOffset + cells[cell].Offset;
                }
            }
        }

        return baseOffset + row.Anchor;
    }

    /// <summary>
    /// The screen position of a source offset: the cell with exactly that offset (the first in document order); otherwise the
    /// first mapped cell with a greater offset, so that a caret on markup lands on the text after it; otherwise the end of the
    /// last line. An empty document is (0, 0).
    /// </summary>
    /// <param name="offset">The absolute source offset.</param>
    public (int Line, int Column) PositionOf(int offset)
    {
        if (_lines.IsEmpty)
        {
            return (0, 0);
        }

        // Only the blocks from the first one that ends after the offset to the last one that starts at or before it can hold the
        // exact cell. The greater cell is the first in document order from the first block on.
        int last = LastBlockStartingAtOrBefore(offset);
        int first = FirstBlockEndingAfter(offset);
        for (int block = first; block <= last; block++)
        {
            if (_blocks[block].TryExact(offset - _blockStarts[block], out (int Line, int Column) exact))
            {
                return (_firstLines[block] + exact.Line, exact.Column);
            }
        }

        for (int block = first; block < _blocks.Length; block++)
        {
            if (_blocks[block].TryGreater(offset - _blockStarts[block], out (int Line, int Column) next))
            {
                return (_firstLines[block] + next.Line, next.Column);
            }
        }

        int lastLine = _lines.Length - 1;
        return (lastLine, _lines[lastLine].Cells.Sum(cell => cell.Width));
    }

    /// <summary>The block that owns a line: the last block whose first line is at or before it.</summary>
    private int BlockOf(int line)
    {
        int low = 0;
        int high = _firstLines.Length - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (_firstLines[middle] <= line)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>The last block whose cells can start at or before an offset, or -1 when none does.</summary>
    private int LastBlockStartingAtOrBefore(int offset)
    {
        int low = 0;
        int high = _searchStarts.Length - 1;
        int found = -1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (_searchStarts[middle] <= offset)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    /// <summary>The first block whose end is after an offset, or the block count when none is. The running maximum of the ends makes this a search.</summary>
    private int FirstBlockEndingAfter(int offset)
    {
        int low = 0;
        int high = _maxEnds.Length - 1;
        int found = _maxEnds.Length;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (_maxEnds[middle] > offset)
            {
                found = middle;
                high = middle - 1;
            }
            else
            {
                low = middle + 1;
            }
        }

        return found;
    }

    private void CheckLine(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _lines.Length);
    }
}
