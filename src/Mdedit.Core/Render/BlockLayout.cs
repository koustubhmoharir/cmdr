using System.Collections.Immutable;

namespace Mdedit.Core.Render;

/// <summary>
/// The display lines of one block laid out for a width. The offsets in the lines are relative to the block's start. Built by
/// <see cref="Layout"/>; immutable, and shared by a <see cref="LayoutCache"/> between builds of the same block.
/// </summary>
/// <remarks>
/// Besides the lines, the layout keeps what the source map needs, computed once with it: the screen columns of every line and an
/// index of the mapped cells sorted by offset. A lookup by offset is then a binary search, instead of a scan of the cells.
/// </remarks>
public sealed class BlockLayout
{
    private readonly ImmutableArray<MappedCell> _index;
    private readonly ImmutableArray<int> _firstAfter;

    private BlockLayout(ImmutableArray<DisplayLine> lines)
    {
        Lines = lines;
        Columns = lines.Select(line => ColumnsOf(line.Cells)).ToImmutableArray();
        _index = BuildIndex(lines, Columns);
        _firstAfter = BuildFirstAfter(_index);
        MinOffset = _index.Length == 0 ? 0 : _index[0].Offset;
    }

    /// <summary>The display lines of the block, in order: the title lines of its alerts, then its content.</summary>
    public ImmutableArray<DisplayLine> Lines { get; }

    /// <summary>
    /// The lowest offset of a mapped cell in the block, relative to its start (negative for an ordered marker, which lies before
    /// the block), or 0 when no cell is mapped.
    /// </summary>
    public int MinOffset { get; }

    /// <summary>The screen columns of each line: the column of every cell, a zero-width cell taking the column of the one before.</summary>
    internal ImmutableArray<ImmutableArray<int>> Columns { get; }

    /// <summary>Lays out one block for a screen width.</summary>
    /// <param name="block">The block, from a <see cref="RenderDocument"/>.</param>
    /// <param name="width">The full screen width in columns, at least 1. Content wraps in the width left after the prefix.</param>
    public static BlockLayout Layout(RenderBlock block, int width)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);

        return new BlockLayout(BlockLayouter.Layout(block, width));
    }

    /// <summary>
    /// The mapped cell with exactly this relative offset that comes first in document order, as its line and column.
    /// </summary>
    /// <param name="offset">The offset relative to the block's start.</param>
    /// <param name="position">The line within the block and the screen column of the cell, when found.</param>
    internal bool TryExact(int offset, out (int Line, int Column) position)
    {
        int index = LowerBound(offset);
        if (index < _index.Length && _index[index].Offset == offset)
        {
            position = (_index[index].Line, _index[index].Column);
            return true;
        }

        position = default;
        return false;
    }

    /// <summary>
    /// The first mapped cell in document order whose relative offset is greater than <paramref name="offset"/>, as its line and
    /// column.
    /// </summary>
    /// <param name="offset">The offset relative to the block's start.</param>
    /// <param name="position">The line within the block and the screen column of the cell, when found.</param>
    internal bool TryGreater(int offset, out (int Line, int Column) position)
    {
        int index = LowerBound(offset + 1);
        if (index < _index.Length)
        {
            MappedCell cell = _index[_firstAfter[index]];
            position = (cell.Line, cell.Column);
            return true;
        }

        position = default;
        return false;
    }

    /// <summary>The screen columns of one line's cells: a zero-width cell takes the column of the cell before it.</summary>
    /// <param name="cells">The cells of the line.</param>
    internal static ImmutableArray<int> ColumnsOf(ImmutableArray<Cell> cells)
    {
        var columns = ImmutableArray.CreateBuilder<int>(cells.Length);
        int cursor = 0;
        for (int cell = 0; cell < cells.Length; cell++)
        {
            if (cells[cell].Width == 0)
            {
                columns.Add(cell > 0 ? columns[cell - 1] : 0);
            }
            else
            {
                columns.Add(cursor);
                cursor += cells[cell].Width;
            }
        }

        return columns.MoveToImmutable();
    }

    /// <summary>The index of the first entry whose offset is at least <paramref name="offset"/>, or the count when none is.</summary>
    private int LowerBound(int offset)
    {
        int low = 0;
        int high = _index.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (_index[middle].Offset < offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>
    /// The mapped cells sorted by offset, ties in document order. Sorting by (offset, line, cell) keeps the document order of
    /// equal offsets, such as the cells of one entity.
    /// </summary>
    private static ImmutableArray<MappedCell> BuildIndex(ImmutableArray<DisplayLine> lines, ImmutableArray<ImmutableArray<int>> columns)
    {
        var cells = new List<MappedCell>();
        for (int line = 0; line < lines.Length; line++)
        {
            ImmutableArray<Cell> row = lines[line].Cells;
            for (int cell = 0; cell < row.Length; cell++)
            {
                if (row[cell].IsMapped)
                {
                    cells.Add(new MappedCell(row[cell].Offset, line, cell, columns[line][cell]));
                }
            }
        }

        cells.Sort(static (a, b) => a.Offset != b.Offset ? a.Offset.CompareTo(b.Offset)
            : a.Line != b.Line ? a.Line.CompareTo(b.Line)
            : a.Cell.CompareTo(b.Cell));
        return cells.ToImmutableArray();
    }

    /// <summary>
    /// For each position in the index, the position of the entry that comes first in document order among that entry and the ones
    /// after it. Answers "first cell after an offset in document order" in one lookup.
    /// </summary>
    private static ImmutableArray<int> BuildFirstAfter(ImmutableArray<MappedCell> index)
    {
        var first = new int[index.Length];
        for (int position = index.Length - 1; position >= 0; position--)
        {
            first[position] = position == index.Length - 1 || Before(index[position], index[first[position + 1]])
                ? position
                : first[position + 1];
        }

        return ImmutableArray.Create(first);
    }

    private static bool Before(MappedCell a, MappedCell b) => a.Line != b.Line ? a.Line < b.Line : a.Cell < b.Cell;
}
