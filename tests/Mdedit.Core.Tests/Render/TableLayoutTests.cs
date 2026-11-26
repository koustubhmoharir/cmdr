using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>
/// The grid of a table: column widths (natural, narrowed widest first with the leftmost on a tie, minimums with overflow), cell
/// wrapping and padding, alignment, borders and their anchors, prefixes in quotes and list items, and the source map of the
/// lines. Expected lines are written out, so that a change in the drawing shows here.
/// </summary>
public sealed class TableLayoutTests
{
    [Fact]
    public void Simple_table_is_drawn_with_natural_widths_and_its_borders()
    {
        DocumentLayout layout = LayoutDump.Build("| a | b |\n|---|---|\n| 1 | 2 |\n", 40);

        Assert.Equal(new[] { "┌───┬───┐", "│ a │ b │", "├───┼───┤", "│ 1 │ 2 │", "└───┴───┘" }, Lines(layout));
        Assert.Equal(0, layout.Blocks[0].ScrollWidth);
    }

    [Fact]
    public void Borders_are_synthetic_and_the_cells_keep_their_source_offsets()
    {
        DocumentLayout layout = LayoutDump.Build("| a | b |\n|---|---|\n| 1 | 2 |\n", 40);

        Assert.Equal("│@-/TableBorder| @-|a@2| @-|│@-/TableBorder| @-|b@6| @-|│@-/TableBorder", LayoutDump.Cells(layout, 1));
        Assert.Equal("│@-/TableBorder| @-|1@22| @-|│@-/TableBorder| @-|2@26| @-|│@-/TableBorder", LayoutDump.Cells(layout, 3));
        Assert.All(layout.Line(0).Cells, cell => Assert.False(cell.IsMapped));
    }

    [Fact]
    public void Each_line_has_the_anchor_of_its_row_and_the_borders_anchor_at_the_next_row_or_the_end()
    {
        DocumentLayout layout = LayoutDump.Build("| a | b |\n|---|---|\n| 1 | 2 |\n", 40);

        Assert.Equal(new[] { 0, 0, 20, 20, 29 }, Enumerable.Range(0, 5).Select(line => layout.Line(line).Anchor).ToArray());
        Assert.Equal(new[] { false, false, false, false, false }, Enumerable.Range(0, 5).Select(line => layout.Line(line).IsContinuation).ToArray());
    }

    [Fact]
    public void Center_alignment_puts_the_extra_space_on_the_right_and_right_alignment_pushes_text_to_the_edge()
    {
        DocumentLayout layout = LayoutDump.Build("| left | mid | right |\n|:--|:-:|--:|\n| a | b | c |\n", 40);

        Assert.Equal(new[]
        {
            "┌──────┬─────┬───────┐",
            "│ left │ mid │ right │",
            "├──────┼─────┼───────┤",
            "│ a    │  b  │     c │",
            "└──────┴─────┴───────┘",
        }, Lines(layout));
    }

    [Fact]
    public void Odd_free_space_when_centred_goes_to_the_right()
    {
        // Column width 4 (the header), the body cell is 1 wide: 3 free columns, 1 on the left and 2 on the right.
        DocumentLayout layout = LayoutDump.Build("| abcd |\n|:-:|\n| x |\n", 40);

        Assert.Equal("│ abcd │", layout.TextOf(1));
        Assert.Equal("│  x   │", layout.TextOf(3));
    }

    [Fact]
    public void Narrowing_takes_the_widest_column_first_and_the_leftmost_on_a_tie()
    {
        // Natural widths 13 and 10 with borders need 30 columns; 26 is the content width. The first column is narrowed
        // from 13 to 9 (it is the widest each time, and at 10 against 10 the leftmost goes first).
        DocumentLayout layout = LayoutDump.Build("| one two three | alpha beta |\n|---|---|\n| x | y |\n", 26);

        Assert.Equal(new[]
        {
            "┌" + new string('─', 11) + "┬" + new string('─', 12) + "┐",
            "│ one two   │ alpha beta │",
            "│ three     │            │",
            "├" + new string('─', 11) + "┼" + new string('─', 12) + "┤",
            "│ x         │ y          │",
            "└" + new string('─', 11) + "┴" + new string('─', 12) + "┘",
        }, Lines(layout));
    }

    [Fact]
    public void A_row_as_tall_as_its_tallest_cell_pads_the_other_cells_and_its_second_line_continues_the_first()
    {
        DocumentLayout layout = LayoutDump.Build("| one two three | alpha beta |\n|---|---|\n", 26);

        Assert.Equal("│ three     │            │", layout.TextOf(2));
        Assert.False(layout.Line(1).IsContinuation);
        Assert.True(layout.Line(2).IsContinuation);
        Assert.Equal(new[] { 0, 0, 0 }, new[] { layout.Line(1).Anchor, layout.Line(2).Anchor, layout.Line(0).Anchor });
    }

    [Fact]
    public void Wrapped_cell_lines_are_mapped_to_their_source_words()
    {
        DocumentLayout layout = LayoutDump.Build("| one two three | alpha beta |\n|---|---|\n", 26);

        Assert.StartsWith("│@-/TableBorder| @-|o@2|n@3|e@4| @5|t@6|w@7|o@8| @9|", LayoutDump.Cells(layout, 1));
        Assert.StartsWith("│@-/TableBorder| @-|t@10|h@11|r@12|e@13|e@14|", LayoutDump.Cells(layout, 2));
    }

    [Fact]
    public void A_table_wider_than_the_content_keeps_its_minimum_widths_and_overflows()
    {
        DocumentLayout layout = LayoutDump.Build("| aaaaaaaaaa | bbbbbbbbbb |\n|---|---|\n| c | d |\n", 10);

        Assert.Equal("┌" + new string('─', 12) + "┬" + new string('─', 12) + "┐", layout.TextOf(0));
        Assert.Equal(27, layout.Blocks[0].ScrollWidth);
        Assert.Equal("│ aaaaaaaaaa │ bbbbbbbbbb │", layout.TextOf(1));
    }

    [Fact]
    public void A_word_longer_than_the_minimum_cap_breaks_inside_its_cell()
    {
        string word = new('a', 37);
        DocumentLayout layout = LayoutDump.Build($"| {word} |\n|---|\n| x |\n", 20);

        Assert.Equal("│ " + new string('a', 24) + " │", layout.TextOf(1));
        Assert.Equal("│ " + new string('a', 13) + new string(' ', 12) + "│", layout.TextOf(2));
        Assert.Equal(28, layout.Blocks[0].ScrollWidth);
    }

    [Fact]
    public void Header_only_table_has_a_separator_and_its_bottom_border_anchors_at_the_block_end()
    {
        DocumentLayout layout = LayoutDump.Build("| a | b |\n|---|---|\n", 40);

        Assert.Equal(new[] { "┌───┬───┐", "│ a │ b │", "├───┼───┤", "└───┴───┘" }, Lines(layout));
        Assert.Equal(19, layout.Line(2).Anchor);
        Assert.Equal(19, layout.Line(3).Anchor);
    }

    [Fact]
    public void Table_in_a_quote_has_the_bar_on_every_line_and_a_prefix_of_two_cells()
    {
        DocumentLayout layout = LayoutDump.Build("> | a |\n> |---|\n> | 1 |\n", 40);

        Assert.Equal(new[] { "│ ┌───┐", "│ │ a │", "│ ├───┤", "│ │ 1 │", "│ └───┘" }, Lines(layout));
        Assert.All(Enumerable.Range(0, layout.LineCount), line => Assert.Equal(2, layout.Line(line).PrefixCells));
    }

    [Fact]
    public void Table_in_a_list_item_has_the_bullet_on_its_first_line_and_blanks_on_the_others()
    {
        DocumentLayout layout = LayoutDump.Build("- | a |\n  |---|\n  | 1 |\n", 40);

        Assert.Equal(new[] { "• ┌───┐", "  │ a │", "  ├───┤", "  │ 1 │", "  └───┘" }, Lines(layout));
        Assert.All(Enumerable.Range(0, layout.LineCount), line => Assert.Equal(2, layout.Line(line).PrefixCells));
    }

    [Fact]
    public void Table_that_starts_an_alert_comes_after_the_alert_title()
    {
        DocumentLayout layout = LayoutDump.Build("> [!NOTE]\n> | a |\n> |---|\n> | 1 |\n", 40);

        Assert.Equal(new[] { "│ Note", "│ ┌───┐", "│ │ a │", "│ ├───┤", "│ │ 1 │", "│ └───┘" }, Lines(layout));
        Assert.All(Enumerable.Range(0, layout.LineCount), line => Assert.Equal(2, layout.Line(line).PrefixCells));
    }

    [Fact]
    public void Overflowing_table_in_a_quote_scrolls_by_its_width_without_the_quote_bar()
    {
        DocumentLayout layout = LayoutDump.Build("> | aaaaaaaaaa | bbbbbbbbbb |\n> |---|---|\n> | c | d |\n", 10);

        Assert.Equal(27, layout.Blocks[0].ScrollWidth);
    }

    [Fact]
    public void Source_map_reaches_cells_and_treats_synthetic_borders_by_the_next_mapped_cell()
    {
        DocumentLayout layout = LayoutDump.Build("| a | b |\n|---|---|\n| 1 | 2 |\n", 40);

        Assert.Equal(2, layout.OffsetAt(1, 2));
        Assert.Equal(2, layout.OffsetAt(1, 0));
        Assert.Equal(0, layout.OffsetAt(0, 0));
        Assert.Equal(29, layout.OffsetAt(4, 0));
        Assert.Equal((1, 2), layout.PositionOf(2));
        Assert.Equal((3, 6), layout.PositionOf(26));
    }

    private static string[] Lines(DocumentLayout layout) => Enumerable.Range(0, layout.LineCount).Select(layout.TextOf).ToArray();
}
