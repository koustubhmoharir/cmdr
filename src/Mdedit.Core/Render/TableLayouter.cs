using System.Collections.Immutable;
using System.Text;

namespace Mdedit.Core.Render;

/// <summary>
/// Draws a table block as a grid. The column widths come from the natural width of each column, narrowed one column at a time
/// to the minimum (the longest word, capped) when the table is too wide; each cell is word-wrapped to its column and padded,
/// and a row is as tall as its tallest cell. Borders and padding are synthetic. Offsets are relative to the block.
/// </summary>
internal static class TableLayouter
{
    /// <summary>The largest minimum width of a column: a word longer than this is broken inside its cell.</summary>
    public const int MaxMinimumWidth = 24;

    /// <summary>
    /// The content lines of a table at a content width (the width left after the container prefix), without the prefix: the
    /// top border, the header row, the header separator, the body rows and the bottom border. A row's lines anchor at the row's
    /// start; the top border and the header separator at the start of the row after them; the bottom border at the block's end.
    /// A row's second and later lines are continuations of the row's first.
    /// </summary>
    /// <param name="block">A table block, with its <see cref="RenderBlock.Table"/>.</param>
    /// <param name="contentWidth">The width the grid may take. A grid wider than this overflows, at its minimum widths.</param>
    public static List<(ImmutableArray<Cell> Content, int Anchor, bool IsContinuation)> Lines(RenderBlock block, int contentWidth)
    {
        TableModel table = block.Table ?? throw new InvalidOperationException("The block is not a table.");
        ImmutableArray<int> widths = ColumnWidths(table, contentWidth);
        int length = block.End - block.Start;
        var lines = new List<(ImmutableArray<Cell>, int, bool)>();

        lines.Add((Border(widths, '┌', '┬', '┐'), table.Rows.IsEmpty ? 0 : table.Rows[0].Start, false));
        for (int index = 0; index < table.Rows.Length; index++)
        {
            TableRowModel row = table.Rows[index];
            List<ImmutableArray<Cell>> rowLines = RowLines(table, row, widths);
            for (int line = 0; line < rowLines.Count; line++)
            {
                lines.Add((rowLines[line], row.Start, line > 0));
            }

            bool lastHeader = row.IsHeader && (index + 1 == table.Rows.Length || !table.Rows[index + 1].IsHeader);
            if (lastHeader)
            {
                int next = index + 1 < table.Rows.Length ? table.Rows[index + 1].Start : length;
                lines.Add((Border(widths, '├', '┼', '┤'), next, false));
            }
        }

        lines.Add((Border(widths, '└', '┴', '┘'), length, false));
        return lines;
    }

    /// <summary>
    /// The width of each column's content. The natural width is the widest cell of the column; the minimum is the longest word of
    /// the column, capped at <see cref="MaxMinimumWidth"/> and at least 1, and the natural width is never below it. When the
    /// natural widths and the borders do not fit, the widest column still above its minimum is narrowed by one, the leftmost on a
    /// tie, until the grid fits or every column is at its minimum; a grid that still does not fit keeps the minimums.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="contentWidth">The width the grid may take.</param>
    public static ImmutableArray<int> ColumnWidths(TableModel table, int contentWidth)
    {
        int columns = table.Alignments.Length;
        var widths = new int[columns];
        var minimums = new int[columns];
        foreach (TableRowModel row in table.Rows)
        {
            for (int column = 0; column < columns; column++)
            {
                ImmutableArray<Cell> cells = row.Cells[column].Cells;
                widths[column] = Math.Max(widths[column], Width(cells));
                minimums[column] = Math.Max(minimums[column], LongestWord(cells));
            }
        }

        for (int column = 0; column < columns; column++)
        {
            minimums[column] = Math.Clamp(minimums[column], 1, MaxMinimumWidth);
            widths[column] = Math.Max(widths[column], minimums[column]);
        }

        int total = widths.Sum() + BorderWidth(columns);
        while (total > contentWidth)
        {
            int widest = -1;
            for (int column = 0; column < columns; column++)
            {
                if (widths[column] > minimums[column] && (widest < 0 || widths[column] > widths[widest]))
                {
                    widest = column;
                }
            }

            if (widest < 0)
            {
                break;
            }

            widths[widest]--;
            total--;
        }

        return widths.ToImmutableArray();
    }

    /// <summary>The lines of one row: each cell's lines wrapped to its column, padded and bordered, the row as tall as its tallest cell.</summary>
    private static List<ImmutableArray<Cell>> RowLines(TableModel table, TableRowModel row, ImmutableArray<int> widths)
    {
        var wrapped = new List<List<WrappedLine>>(widths.Length);
        for (int column = 0; column < widths.Length; column++)
        {
            wrapped.Add(TextWrap.Words(row.Cells[column].Cells, 0, widths[column]));
        }

        int height = wrapped.Max(lines => lines.Count);
        var result = new List<ImmutableArray<Cell>>(height);
        for (int line = 0; line < height; line++)
        {
            var cells = ImmutableArray.CreateBuilder<Cell>();
            cells.Add(Bar());
            for (int column = 0; column < widths.Length; column++)
            {
                ImmutableArray<Cell> content = line < wrapped[column].Count ? wrapped[column][line].Cells : ImmutableArray<Cell>.Empty;
                int extra = Math.Max(0, widths[column] - Width(content));
                int left = table.Alignments[column] switch
                {
                    ColumnAlignment.Center => extra / 2,
                    ColumnAlignment.Right => extra,
                    _ => 0,
                };

                cells.AddRange(Blanks(1 + left));
                cells.AddRange(content);
                cells.AddRange(Blanks(extra - left + 1));
                cells.Add(Bar());
            }

            result.Add(cells.ToImmutable());
        }

        return result;
    }

    /// <summary>A horizontal border with a corner or junction at each column edge, drawn as synthetic border cells.</summary>
    private static ImmutableArray<Cell> Border(ImmutableArray<int> widths, char left, char middle, char right)
    {
        var text = new StringBuilder().Append(left);
        for (int column = 0; column < widths.Length; column++)
        {
            if (column > 0)
            {
                text.Append(middle);
            }

            text.Append('─', widths[column] + 2);
        }

        text.Append(right);
        return SyntheticCell.Text(text.ToString(), StyleRole.TableBorder);
    }

    /// <summary>The width of the vertical bars and padding of a row of <paramref name="columns"/> columns: <c>3 × columns + 1</c>.</summary>
    private static int BorderWidth(int columns) => 3 * columns + 1;

    private static Cell Bar() => SyntheticCell.Make("│", StyleRole.TableBorder);

    /// <summary>Synthetic blank cells: the padding around a cell's content.</summary>
    private static ImmutableArray<Cell> Blanks(int count) => count <= 0 ? ImmutableArray<Cell>.Empty : SyntheticCell.Text(new string(' ', count), StyleRole.Body);

    private static int Width(ImmutableArray<Cell> cells)
    {
        int width = 0;
        foreach (Cell cell in cells)
        {
            width += cell.Width;
        }

        return width;
    }

    /// <summary>The width of the longest run of cells between spaces: the narrowest a cell can be without breaking a word.</summary>
    private static int LongestWord(ImmutableArray<Cell> cells)
    {
        int longest = 0;
        int current = 0;
        foreach (Cell cell in cells)
        {
            if (cell.Text == " ")
            {
                longest = Math.Max(longest, current);
                current = 0;
            }
            else
            {
                current += cell.Width;
            }
        }

        return Math.Max(longest, current);
    }
}
