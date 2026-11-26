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
    private readonly ImmutableArray<int> _blockEnds;
    private readonly ImmutableArray<int> _searchStarts;
    private readonly ImmutableArray<int> _firstLines;
    private readonly ImmutableArray<int> _maxEnds;
    private readonly ImmutableArray<BlockLayout> _blocks;
    private readonly ImmutableArray<FoldUnit> _units;
    private readonly ImmutableArray<int> _unitOfBlock;
    private readonly ImmutableArray<int> _redirect;

    private DocumentLayout(
        int width,
        ImmutableArray<DisplayLine> lines,
        ImmutableArray<ImmutableArray<int>> columns,
        FoldLayout folds,
        ImmutableArray<int> blockStarts,
        ImmutableArray<int> blockEnds,
        ImmutableArray<int> searchStarts,
        ImmutableArray<int> firstLines,
        ImmutableArray<int> maxEnds,
        ImmutableArray<BlockLayout> blocks)
    {
        Width = width;
        _lines = lines;
        _columns = columns;
        _units = folds.Units;
        _unitOfBlock = folds.UnitOfBlock;
        _redirect = folds.Redirect;
        _blockStarts = blockStarts;
        _blockEnds = blockEnds;
        _searchStarts = searchStarts;
        _firstLines = firstLines;
        _maxEnds = maxEnds;
        _blocks = blocks;
    }

    /// <summary>The screen width the layout was built for.</summary>
    public int Width { get; }

    /// <summary>The number of display lines, separators included.</summary>
    public int LineCount => _lines.Length;

    /// <summary>The layouts of the blocks, in order, as the cache returned them. Not counting the separators; a hidden block has an empty layout.</summary>
    internal ImmutableArray<BlockLayout> Blocks => _blocks;

    /// <summary>
    /// Lays out a document at a width. Each block's layout comes from <paramref name="cache"/>, so that unchanged blocks are not
    /// laid out again.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="width">The full screen width in columns, at least 1.</param>
    /// <param name="cache">The cache of block layouts; the same cache can serve every build.</param>
    /// <param name="folded">
    /// The keys (absolute start offsets) of the folded fold units. A folded unit hides the blocks after its head, and for a code
    /// unit its code lines; a key that is not the start of a unit is ignored. Null means nothing is folded.
    /// </param>
    public static DocumentLayout Build(RenderDocument doc, int width, LayoutCache cache, IReadOnlySet<int>? folded = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);

        ImmutableArray<FoldUnit> units = doc.FoldUnits;
        FoldLayout folds = FoldLayout.Of(doc.Blocks.Length, units, folded);

        var lines = ImmutableArray.CreateBuilder<DisplayLine>();
        var columns = ImmutableArray.CreateBuilder<ImmutableArray<int>>();
        var starts = ImmutableArray.CreateBuilder<int>(doc.Blocks.Length);
        var ends = ImmutableArray.CreateBuilder<int>(doc.Blocks.Length);
        var searchStarts = ImmutableArray.CreateBuilder<int>(doc.Blocks.Length);
        var firstLines = ImmutableArray.CreateBuilder<int>(doc.Blocks.Length);
        var maxEnds = ImmutableArray.CreateBuilder<int>(doc.Blocks.Length);
        var blocks = ImmutableArray.CreateBuilder<BlockLayout>(doc.Blocks.Length);
        int maxEnd = int.MinValue;

        for (int index = 0; index < doc.Blocks.Length; index++)
        {
            RenderBlock block = doc.Blocks[index];
            firstLines.Add(lines.Count);
            starts.Add(block.Start);
            ends.Add(block.End);

            if (folds.Hidden[index])
            {
                searchStarts.Add(block.Start);
                blocks.Add(BlockLayout.Hidden);
            }
            else
            {
                BlockLayout layout = cache.Get(doc, block, width, folds.FoldedLines[index]);
                searchStarts.Add(block.Start + Math.Min(0, layout.MinOffset));
                blocks.Add(layout);
                lines.AddRange(layout.Lines);
                columns.AddRange(layout.Columns);

                // The separator belongs to this block and is drawn before the next block that is not hidden, so a folded head is
                // followed by a blank line, and the hidden blocks leave none.
                int next = NextVisible(folds.Hidden, index + 1);
                if (next < doc.Blocks.Length && !(block.Tight && doc.Blocks[next].Tight))
                {
                    // The separator is anchored at the end of the block before it, relative to that block's start.
                    ImmutableArray<Cell> separator = LinePrefix.Separator(block, doc.Blocks[next]);
                    lines.Add(new DisplayLine(separator, block.End - block.Start, false, separator.Length));
                    columns.Add(BlockLayout.ColumnsOf(separator));
                }
            }

            maxEnd = Math.Max(maxEnd, block.End);
            maxEnds.Add(maxEnd);
        }

        return new DocumentLayout(
            width,
            lines.ToImmutable(),
            columns.ToImmutable(),
            folds,
            starts.ToImmutable(),
            ends.ToImmutable(),
            searchStarts.ToImmutable(),
            firstLines.ToImmutable(),
            maxEnds.ToImmutable(),
            blocks.ToImmutable());
    }

    /// <summary>The index of the first block at or after <paramref name="index"/> that is not hidden, or the block count when none is.</summary>
    private static int NextVisible(ImmutableArray<bool> hidden, int index)
    {
        while (index < hidden.Length && hidden[index])
        {
            index++;
        }

        return index;
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

    /// <summary>
    /// The <see cref="BlockLayout.ScrollWidth"/> of a line's block: the width of the block's content when it is a table wider than
    /// the screen, else 0. A separator line belongs to no block's content and gives 0.
    /// </summary>
    /// <param name="index">The line, from 0 to <see cref="LineCount"/> minus 1.</param>
    public int ScrollWidthOf(int index)
    {
        CheckLine(index);
        int block = BlockOf(index);
        bool separator = index - _firstLines[block] >= _blocks[block].Lines.Length;
        return separator ? 0 : _blocks[block].ScrollWidth;
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

        int index = CellCovering(cells, columns, target);

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

        // Inside a hidden block, or a folded code block (whose code lines are hidden), the position is the folded head's.
        int owner = LastBlockStartingAt(offset);
        if (owner >= 0 && _redirect[owner] >= 0 && offset < _blockEnds[owner])
        {
            return HeadPosition(_redirect[owner]);
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

    /// <summary>
    /// The position of a folded head: its first line, and the column of its first mapped cell (column 0 when it has none). A folded
    /// code block's first mapped cell is its header glyph.
    /// </summary>
    /// <param name="head">The index of the head block.</param>
    private (int Line, int Column) HeadPosition(int head) =>
        _blocks[head].TryFirstMapped(out (int Line, int Column) first)
            ? (_firstLines[head] + first.Line, first.Column)
            : (_firstLines[head], 0);

    /// <summary>
    /// The index of the last block that starts at or before an offset, or -1 when none does. The starts are in document order.
    /// </summary>
    /// <param name="offset">The absolute source offset.</param>
    private int LastBlockStartingAt(int offset)
    {
        int low = 0;
        int high = _blockStarts.Length - 1;
        int found = -1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (_blockStarts[middle] <= offset)
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

    /// <summary>
    /// The index of the cell that covers a screen column, or -1 when none does. The columns never decrease along a line, so a
    /// binary search finds the last cell that starts at or before the column; the cells with no width that share its column
    /// are skipped to reach the cell that has one.
    /// </summary>
    /// <param name="cells">The cells of the line.</param>
    /// <param name="columns">The screen column of each cell (see <see cref="BlockLayout.ColumnsOf"/>).</param>
    /// <param name="target">The screen column, at least 0.</param>
    private static int CellCovering(ImmutableArray<Cell> cells, ImmutableArray<int> columns, int target)
    {
        int low = 0;
        int high = columns.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (columns[middle] <= target)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        for (int cell = low - 1; cell >= 0; cell--)
        {
            if (cells[cell].Width > 0)
            {
                return target < columns[cell] + cells[cell].Width ? cell : -1;
            }
        }

        return -1;
    }

    /// <summary>
    /// The fold unit whose head is on a line: a section's heading lines, or a code unit's header line (not its code lines). Null
    /// when the line is not part of a head, including the separator after one.
    /// </summary>
    /// <param name="line">The line, from 0 to <see cref="LineCount"/> minus 1.</param>
    public FoldUnit? FoldUnitAt(int line)
    {
        CheckLine(line);
        int block = BlockOf(line);
        int unit = _unitOfBlock[block];
        if (unit < 0)
        {
            return null;
        }

        FoldUnit found = _units[unit];
        int headLines = found.Kind == FoldUnitKind.Code ? 1 : _blocks[block].Lines.Length;
        return line >= _firstLines[block] && line < _firstLines[block] + headLines ? found : null;
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
