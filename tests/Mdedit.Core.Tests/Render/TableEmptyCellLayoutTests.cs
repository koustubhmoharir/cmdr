using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>
/// The empty cells of a table are caret stops: the layout maps one blank in an empty cell at its place between its pipes, which is unique
/// in the source map, round-trips through the position API, and leaves the grid and the runs (which copy and search read) as they were.
/// </summary>
public sealed class TableEmptyCellLayoutTests
{
    // The header's middle cell is empty: its place is its closing pipe, at 7, after the padding that follows the pipe at 4.
    private const string Empty = "| a |  | c |\n|---|---|---|\n| 1 | 2 | 3 |\n";

    [Fact]
    public void The_blank_of_an_empty_cell_is_mapped_at_its_place_between_the_pipes()
    {
        DocumentLayout layout = LayoutDump.Build(Empty);

        // 3 and 10 are the end stops of "a" and "c", the first padding cell after each.
        Assert.Equal(new[] { 2, 3, 7, 9, 10 }, layout.Line(1).Cells.Where(cell => cell.IsMapped).Select(cell => cell.Offset).ToArray());
    }

    [Fact]
    public void The_grid_keeps_its_width_with_the_blank_in_place()
    {
        string[] lines = LayoutDump.Lines(Empty);

        Assert.Equal(new[] { "  ┌───┬───┬───┐", "  │ a │   │ c │", "  ├───┼───┼───┤", "  │ 1 │ 2 │ 3 │", "  └───┴───┴───┘" }, lines);
    }

    [Fact]
    public void Every_mapped_offset_of_the_table_is_unique_in_the_source_map()
    {
        DocumentLayout layout = LayoutDump.Build(Empty);
        int[] offsets = Enumerable.Range(0, layout.LineCount)
            .SelectMany(line => layout.Line(line).Cells.Where(cell => cell.IsMapped).Select(cell => cell.Offset))
            .ToArray();

        Assert.Equal(offsets.Length, offsets.Distinct().Count());
    }

    [Fact]
    public void The_blank_round_trips_through_the_position_API()
    {
        DocumentLayout layout = LayoutDump.Build(Empty);

        (int line, int column) = layout.PositionOf(7);

        Assert.Equal(1, line);
        Assert.Equal(7, layout.OffsetAt(line, column));
    }

    [Fact]
    public void The_runs_of_the_empty_cell_stay_empty_so_copy_and_search_do_not_see_the_blank()
    {
        RenderDocument document = RenderDocument.Parse(Empty);

        Assert.Empty(document.Blocks[0].Table!.Rows[0].Cells[1].Cells);
        Assert.Equal(7, document.Blocks[0].Table!.Rows[0].Positions[1]);
    }

    [Fact]
    public void A_row_whose_other_cell_wraps_maps_the_blank_once_on_its_first_line()
    {
        // The first cell wraps to three lines at this width; the empty cell's blank is on the row's first line only.
        DocumentLayout layout = LayoutDump.Build("| aaaa bbbb cccc | |\n|---|---|\n| 1 | 2 |\n", 16);

        int[] offsets = Enumerable.Range(0, layout.LineCount)
            .SelectMany(line => layout.Line(line).Cells.Where(cell => cell.IsMapped).Select(cell => cell.Offset))
            .ToArray();

        Assert.Equal(offsets.Length, offsets.Distinct().Count());
        Assert.Equal(1, layout.Line(1).Cells.Count(cell => cell.IsMapped && cell.Offset == 19));
    }
}
