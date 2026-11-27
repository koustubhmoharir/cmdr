using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Mdedit.Core;
using Mdedit.Core.Render;
using Mdedit.Core.Tables;

namespace Mdedit.Core.Tests.Tables;

/// <summary>The source model of a pipe table: pipes, cells, contents, prefixes, line endings and alignment.</summary>
public sealed class TableSourceTests
{
    private static TableSource Parse(string text) => TableSource.Parse(text, TableCase.Table(text));

    [Fact]
    public void Outer_pipes_are_recorded_and_the_cells_are_the_text_between_them()
    {
        TableSource table = Parse("| a | b |\n|---|---|\n| 1 | 2 |\n");

        Assert.True(table.Header.LeadingPipe);
        Assert.True(table.Header.TrailingPipe);
        Assert.Equal(new[] { "a", "b" }, table.Header.Contents.Select(content => content.Slice(table.Text)));
        Assert.Equal(new[] { " a ", " b " }, table.Header.Cells.Select(cell => cell.Slice(table.Text)));
        Assert.Equal(new[] { 0, 4, 8 }, table.Header.Pipes.ToArray());
    }

    [Fact]
    public void Without_outer_pipes_the_first_and_last_cells_are_not_bounded_by_pipes()
    {
        TableSource table = Parse("a | b\n-|--\n1 | 2\n");

        Assert.False(table.Header.LeadingPipe);
        Assert.False(table.Header.TrailingPipe);
        Assert.Equal(new[] { "a ", " b" }, table.Header.Cells.Select(cell => cell.Slice(table.Text)));
        Assert.Equal(string.Empty, table.Header.Suffix.Slice(table.Text));
    }

    [Fact]
    public void A_trailing_pipe_with_spaces_after_it_is_the_suffix()
    {
        TableSource table = Parse("| a | b |   \n|---|---|\n| 1 | 2 |\n");

        Assert.True(table.Header.TrailingPipe);
        Assert.Equal("   ", table.Header.Suffix.Slice(table.Text));
        Assert.Equal(2, table.Header.Cells.Length);
    }

    [Fact]
    public void An_escaped_pipe_does_not_split_a_cell_and_its_cell_text_shows_the_pipe()
    {
        TableSource table = Parse("| a \\| b | c |\n|---|---|\n| 1 | 2 |\n");

        Assert.Equal(2, table.Header.Cells.Length);
        Assert.Equal("a \\| b", table.Header.Contents[0].Slice(table.Text));
        Assert.Equal("a | b", table.CellText(0, 0));
    }

    [Fact]
    public void A_pipe_after_an_escaped_backslash_splits_the_cell()
    {
        TableSource table = Parse("| a \\\\| b |\n|---|---|\n| 1 | 2 |\n");

        Assert.Equal(new[] { "a \\\\", "b" }, table.Header.Contents.Select(content => content.Slice(table.Text)));
    }

    [Fact]
    public void Pipes_in_code_spans_split_the_row_as_gfm_does_and_the_cell_count_matches_markdig()
    {
        const string text = "| `a|b` | c |\n|---|---|---|\n| 1 | 2 | 3 |\n";
        TableSource table = Parse(text);

        Assert.Equal(new[] { "`a", "b`", "c" }, table.Header.Contents.Select(content => content.Slice(text)));
        Assert.Equal(3, MdPipeline.Parse(text).Descendants<Table>().Single().OfType<TableRow>().First().Count());
        Assert.Equal(3, table.Header.Cells.Length);
    }

    [Fact]
    public void Ragged_rows_are_kept_as_written()
    {
        TableSource table = Parse("| a | b |\n|---|---|\n| 1 |\n| 3 | 4 | 5 |\n");

        Assert.Single(table.Row(1).Cells);
        Assert.Equal(3, table.Row(2).Cells.Length);
        Assert.Equal("", table.CellText(1, 1));
        Assert.Equal(3, table.RowCount);
    }

    [Fact]
    public void A_quote_prefix_is_the_prefix_of_every_line()
    {
        TableSource table = Parse("> | a | b |\n> |---|---|\n> | 1 | 2 |\n");

        Assert.Equal("> ", table.Header.Prefix.Slice(table.Text));
        Assert.Equal("> ", table.Delimiter.Prefix.Slice(table.Text));
        Assert.Equal("> ", table.Row(1).Prefix.Slice(table.Text));
        Assert.True(table.Delimiter.LeadingPipe);
    }

    [Fact]
    public void A_list_item_indentation_is_the_prefix_of_the_body_rows()
    {
        TableSource table = Parse("- | a | b |\n  |---|---|\n  | 1 | 2 |\n");

        Assert.Equal("- ", table.Header.Prefix.Slice(table.Text));
        Assert.Equal("  ", table.Delimiter.Prefix.Slice(table.Text));
        Assert.Equal("  ", table.Row(1).Prefix.Slice(table.Text));
        Assert.Equal(new[] { "a", "b" }, table.Header.Contents.Select(content => content.Slice(table.Text)));
    }

    [Fact]
    public void A_table_in_a_quote_inside_a_list_item_has_both_prefixes()
    {
        TableSource table = Parse("> - | a |\n>   |---|\n>   | 1 |\n");

        Assert.Equal("> - ", table.Header.Prefix.Slice(table.Text));
        Assert.Equal(">   ", table.Delimiter.Prefix.Slice(table.Text));
        Assert.Equal(new[] { "a" }, table.Header.Contents.Select(content => content.Slice(table.Text)));
    }

    [Fact]
    public void Line_endings_are_recorded_per_line_and_the_last_line_has_none_at_the_end_of_the_text()
    {
        TableSource crlf = Parse("| a | b |\r\n|---|---|\r\n| 1 | 2 |");

        Assert.Equal("\r\n", crlf.NewLine);
        Assert.Equal("\r\n", crlf.Delimiter.Newline);
        Assert.Equal(string.Empty, crlf.Row(1).Newline);
    }

    [Fact]
    public void A_grid_of_pipes_is_aligned()
    {
        Assert.True(Parse("| a   | b   |\n| --- | --- |\n| 1   | 2   |\n").IsAligned);
    }

    [Fact]
    public void A_body_row_with_pipes_elsewhere_is_not_aligned()
    {
        Assert.False(Parse("| a | b |\n|---|---|\n| 1 | 22 |\n").IsAligned);
    }

    [Fact]
    public void The_delimiter_row_counts_for_alignment()
    {
        Assert.False(Parse("| a | b |\n|-|---|\n| 1 | 2 |\n").IsAligned);
    }

    [Fact]
    public void Alignment_counts_display_columns_so_wide_characters_take_two()
    {
        Assert.True(Parse("| 名前 | b   |\n| ---- | --- |\n| xxxx | y   |\n").IsAligned);
        Assert.False(Parse("| 名前 | b   |\n| ---- | --- |\n| xx | y   |\n").IsAligned);
    }

    [Fact]
    public void Cell_text_is_the_content_without_padding()
    {
        TableSource table = Parse("| a |   bb   |\n|---|---|\n| 1 | 2 |\n");

        Assert.Equal("a", table.CellText(0, 0));
        Assert.Equal("bb", table.CellText(0, 1));
        Assert.Equal("", table.CellText(0, 5));
    }

    [Fact]
    public void A_row_that_starts_with_an_empty_cell_has_all_its_cells()
    {
        const string text = "| a | b |\n|---|---|\n|  | 22 |\n";

        Assert.Equal(2, TableSource.Parse(text, TableCase.Table(text)).Row(1).Cells.Length);
    }

    [Fact]
    public void An_all_empty_row_is_scanned_on_its_own_line()
    {
        const string text = "| a | b |\n|---|---|\n|  |  |\n| 1 | 2 |\n";
        TableSource source = TableSource.Parse(text, TableCase.Table(text));

        Assert.Equal(20, source.Row(1).Line.Start);
        Assert.Equal(2, source.Row(1).Cells.Length);
        Assert.Equal(2, source.Row(2).Cells.Length);
    }
}
