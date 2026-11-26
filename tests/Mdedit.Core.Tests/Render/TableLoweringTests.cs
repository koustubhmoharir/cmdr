using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>
/// The table model of a pipe table: alignments, row source lines, header flags, the cells as runs with their mapping, and how
/// short, long and empty rows are padded or cut. A table's runs are its cells in row order, so run <c>r * columns + c</c> is
/// the cell of row <c>r</c> and column <c>c</c>.
/// </summary>
public sealed class TableLoweringTests
{
    [Fact]
    public void Pipe_table_is_one_table_block_whose_runs_are_its_cells_in_row_order()
    {
        RenderBlock block = RenderDocument.Parse("| a | b |\n|---|---|\n| 1 | 2 |\n").Blocks[0];

        Assert.Equal("Table 0..29 level=0 lang=- tight=no first=no containers=[-]", RenderDump.Shape(block));
        Assert.Equal("a@2+strong|b@6+strong|1@22|2@26", string.Join("|", Enumerable.Range(0, block.Runs.Length).Select(run => RenderDump.Cells(block, run))));
    }

    [Fact]
    public void Model_has_the_alignment_of_each_column_and_the_source_line_start_of_each_row()
    {
        TableModel table = RenderDocument.Parse("| l | c | r | n |\n|:--|:-:|--:|---|\n| x | y | z | w |\n").Blocks[0].Table!;

        Assert.Equal(new[] { ColumnAlignment.Left, ColumnAlignment.Center, ColumnAlignment.Right, ColumnAlignment.Left }, table.Alignments);
        Assert.Equal(new[] { true, false }, table.Rows.Select(row => row.IsHeader).ToArray());
        Assert.Equal(new[] { 0, 36 }, table.Rows.Select(row => row.Start).ToArray());
    }

    [Fact]
    public void Row_source_lines_are_relative_to_a_table_that_starts_in_a_list_item()
    {
        TableModel table = RenderDocument.Parse("- | a |\n  |---|\n  | 1 |\n").Blocks[0].Table!;

        Assert.Equal(new[] { 0, 14 }, table.Rows.Select(row => row.Start).ToArray());
    }

    [Fact]
    public void Header_cells_are_strong_and_body_cells_are_not()
    {
        RenderBlock block = RenderDocument.Parse("| a |\n|---|\n| b |\n").Blocks[0];

        Assert.Equal("a@2+strong", RenderDump.Cells(block, 0));
        Assert.Equal("b@14", RenderDump.Cells(block, 1));
    }

    [Fact]
    public void Header_cell_keeps_its_own_inline_styles_and_adds_strong()
    {
        RenderBlock block = RenderDocument.Parse("| *x* |\n|---|\n").Blocks[0];

        Assert.Equal("x@3+em+strong", RenderDump.Cells(block, 0));
    }

    [Fact]
    public void Emphasis_inside_a_body_cell_keeps_its_mapped_offsets_and_the_space_after_it()
    {
        RenderBlock block = RenderDocument.Parse("| a |\n|---|\n| **b** c |\n").Blocks[0];

        Assert.Equal("b@16+strong| @19|c@20", RenderDump.Cells(block, 1));
    }

    [Fact]
    public void Escaped_pipe_is_one_cell_and_its_backslash_is_not_mapped()
    {
        RenderBlock block = RenderDocument.Parse("| a \\| b |\n|---|\n").Blocks[0];

        Assert.Equal("a@2+strong| @3+strong||@5+strong| @6+strong|b@7+strong", RenderDump.Cells(block, 0));
        Assert.Equal("a | b", RenderDump.Text(block, 0));
    }

    [Fact]
    public void Code_span_in_a_cell_has_the_code_style_and_maps_its_content()
    {
        RenderBlock block = RenderDocument.Parse("| `x` |\n|---|\n| y |\n").Blocks[0];

        Assert.Equal("x@3+strong+code", RenderDump.Cells(block, 0));
    }

    [Fact]
    public void Links_in_cells_have_the_link_style_and_ids_unique_within_the_table()
    {
        RenderBlock block = RenderDocument.Parse("| [a](u) | [b](v) |\n|---|---|\n| [c](w) | d |\n").Blocks[0];

        Assert.Equal("a@3+strong+link^0", RenderDump.Cells(block, 0));
        Assert.Equal("b@12+strong+link^1", RenderDump.Cells(block, 1));
        Assert.Equal("c@33+link^2", RenderDump.Cells(block, 2));
    }

    [Fact]
    public void Short_row_is_padded_with_empty_runs_to_the_column_count()
    {
        RenderBlock block = RenderDocument.Parse("| a | b | c |\n|---|---|---|\n| 1 |\n").Blocks[0];

        Assert.Equal(6, block.Runs.Length);
        Assert.Equal("1", RenderDump.Text(block, 3));
        Assert.Empty(block.Runs[4].Cells);
        Assert.Empty(block.Runs[5].Cells);
    }

    [Fact]
    public void Cells_past_the_last_column_are_not_lowered()
    {
        RenderBlock block = RenderDocument.Parse("| a |\n|---|\n| 1 | 2 | 3 |\n").Blocks[0];

        Assert.Equal(2, block.Runs.Length);
        Assert.Equal("1@14", RenderDump.Cells(block, 1));
    }

    [Fact]
    public void Header_only_table_has_one_row_and_no_body()
    {
        RenderBlock block = RenderDocument.Parse("| a | b |\n|---|---|\n").Blocks[0];

        Assert.Single(block.Table!.Rows);
        Assert.Equal(2, block.Runs.Length);
        Assert.Equal(new[] { ColumnAlignment.Left, ColumnAlignment.Left }, block.Table!.Alignments);
    }

    [Fact]
    public void Table_in_a_quote_has_the_quote_container_and_its_cells_keep_their_offsets()
    {
        RenderBlock block = RenderDocument.Parse("> | a |\n> |---|\n> | 1 |\n").Blocks[0];

        Assert.Equal("Table 2..23 level=0 lang=- tight=no first=no containers=[quote*]", RenderDump.Shape(block));
        Assert.Equal("a@4+strong", RenderDump.Cells(block, 0));
        Assert.Equal("1@20", RenderDump.Cells(block, 1));
    }

    [Fact]
    public void Table_in_a_list_item_has_the_item_container_on_its_first_leaf()
    {
        RenderBlock block = RenderDocument.Parse("- | a |\n  |---|\n  | 1 |\n").Blocks[0];

        Assert.Contains("containers=[bullet[0]*]", RenderDump.Shape(block));
        Assert.Equal("a@4+strong", RenderDump.Cells(block, 0));
    }
}
