using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>
/// The gutter: the two columns at the start of every display line that has cells. The fold glyph and a space sit on the first line of
/// a heading or a code block, blanks on the other lines, and the content after it starts at the content column.
/// </summary>
public sealed class GutterTests
{
    [Fact]
    public void Heading_gutter_is_the_fold_glyph_and_its_text_starts_at_the_content_column()
    {
        DocumentLayout layout = LayoutDump.Build("# One\n\ntext\n");

        Assert.Equal("▾ One", layout.TextOf(0));
        Assert.Equal(2, layout.Line(0).PrefixCells);
        Assert.Equal(("▾", StyleRole.FoldGlyph), (layout.Line(0).Cells[0].Text, layout.Line(0).Cells[0].Style.Role));
        Assert.Equal((" ", StyleRole.FoldGlyph), (layout.Line(0).Cells[1].Text, layout.Line(0).Cells[1].Style.Role));
        Assert.Equal(("O", StyleRole.Heading), (layout.Line(0).Cells[2].Text, layout.Line(0).Cells[2].Style.Role));
        Assert.Equal(Cell.Synthetic, layout.Line(0).Cells[0].Offset);
    }

    [Fact]
    public void Folded_heading_gutter_is_the_folded_glyph()
    {
        DocumentLayout layout = DocumentLayout.Build(RenderDocument.Parse("# One\npara\n"), 40, new LayoutCache(), new HashSet<int> { 0 });

        Assert.Equal("▸ One … 1 line", layout.TextOf(0));
        Assert.Equal(2, layout.Line(0).PrefixCells);
        Assert.Equal("▸", layout.Line(0).Cells[0].Text);
        Assert.Equal(StyleRole.FoldGlyph, layout.Line(0).Cells[0].Style.Role);
        Assert.Equal("O", layout.Line(0).Cells[2].Text);
    }

    [Fact]
    public void Heading_continuation_lines_have_blanks_in_the_gutter()
    {
        DocumentLayout layout = LayoutDump.Build("# aaa bbb ccc\n", 9);

        Assert.Equal("  ccc", layout.TextOf(1));
        Assert.Equal(StyleRole.Body, layout.Line(1).Cells[0].Style.Role);
        Assert.Equal(" ", layout.Line(1).Cells[0].Text);
        Assert.Equal(2, layout.Line(1).PrefixCells);
    }

    [Fact]
    public void Top_level_code_header_has_the_fold_glyph_at_column_zero_and_the_label_at_the_content_column()
    {
        DocumentLayout layout = LayoutDump.Build("```cs\nint x;\n```\n");

        Assert.Equal("▾ cs", layout.TextOf(0));
        Assert.Equal(2, layout.Line(0).PrefixCells);
        Assert.Equal("▾", layout.Line(0).Cells[0].Text);
        Assert.Equal(StyleRole.FoldGlyph, layout.Line(0).Cells[0].Style.Role);
        Assert.Equal("c", layout.Line(0).Cells[2].Text);
        Assert.Equal(StyleRole.CodeHeader, layout.Line(0).Cells[2].Style.Role);
        Assert.Equal("  int x;", layout.TextOf(1));
    }

    [Fact]
    public void Nested_code_header_has_the_glyph_in_column_zero_and_its_label_after_the_containers()
    {
        // The header shares its line with the item's bullet: the glyph, the bullet's two columns, then the label.
        DocumentLayout layout = LayoutDump.Build("- ```cs\n  int x;\n  ```\n");

        Assert.Equal("▾ • cs", layout.TextOf(0));
        Assert.Equal("▾", layout.Line(0).Cells[0].Text);
        Assert.Equal("c", layout.Line(0).Cells[4].Text);
        Assert.Equal(4, layout.Line(0).PrefixCells);
    }

    [Fact]
    public void Code_header_in_a_list_item_after_a_paragraph_is_the_glyph_then_the_item_blanks_then_the_label()
    {
        DocumentLayout layout = LayoutDump.Build("- a\n\n  ```py\n  print(1)\n  ```\n");

        Assert.Equal(new[] { "  • a", string.Empty, "▾   py", "    print(1)" }, Enumerable.Range(0, layout.LineCount).Select(layout.TextOf));
        Assert.Equal("p", layout.Line(2).Cells[4].Text);
        Assert.Equal(4, layout.Line(2).PrefixCells);
    }

    [Fact]
    public void Code_header_label_maps_the_block_start_and_the_gutter_glyph_does_not()
    {
        DocumentLayout layout = LayoutDump.Build("- ```cs\n  int x;\n  ```\n");

        Assert.False(layout.Line(0).Cells[0].IsMapped);
        Assert.Equal(0, layout.Line(0).Cells[4].Offset);
        Assert.Equal(StyleRole.CodeHeader, layout.Line(0).Cells[4].Style.Role);
    }

    [Fact]
    public void Paragraph_list_table_quote_and_thematic_break_lines_have_blank_gutters()
    {
        DocumentLayout layout = LayoutDump.Build("para\n\n- item\n\n| a |\n|---|\n\n> q\n\n***\n", 40);

        foreach (int line in Enumerable.Range(0, layout.LineCount).Where(line => layout.TextOf(line).Length > 0))
        {
            Assert.True(layout.Line(line).PrefixCells >= 2, $"line {line} '{layout.TextOf(line)}' has no gutter");
        }

        Assert.Equal(" ", layout.Line(0).Cells[0].Text);
        Assert.Equal(StyleRole.Body, layout.Line(0).Cells[0].Style.Role);
        Assert.Equal(Cell.Synthetic, layout.Line(0).Cells[1].Offset);
        Assert.Equal("  para", layout.TextOf(0));
    }

    [Fact]
    public void A_line_with_no_cells_stays_empty_and_has_no_gutter()
    {
        DocumentLayout layout = LayoutDump.Build("```\n\n```\n");

        Assert.Equal(string.Empty, layout.TextOf(1));
        Assert.Empty(layout.Line(1).Cells);
        Assert.Equal(0, layout.Line(1).PrefixCells);
    }

    [Fact]
    public void Separator_between_plain_blocks_stays_empty_and_one_between_quotes_gets_a_gutter()
    {
        DocumentLayout plain = LayoutDump.Build("a\n\nb\n");
        DocumentLayout quotes = LayoutDump.Build("> a\n\n> b\n");

        Assert.Empty(plain.Line(1).Cells);
        Assert.Equal("  │ ", quotes.TextOf(1));
        Assert.Equal(StyleRole.Body, quotes.Line(1).Cells[0].Style.Role);
    }

    [Fact]
    public void Table_lines_keep_the_gutter_and_the_wrapped_grid_is_narrower_by_two_columns()
    {
        DocumentLayout layout = LayoutDump.Build("| a |\n|---|\n| 1 |\n", 40);

        Assert.All(Enumerable.Range(0, layout.LineCount), line => Assert.Equal(2, layout.Line(line).PrefixCells));
        Assert.Equal(new[] { "  ┌───┐", "  │ a │", "  ├───┤", "  │ 1 │", "  └───┘" }, Enumerable.Range(0, layout.LineCount).Select(layout.TextOf));
    }

    [Fact]
    public void A_paragraph_wraps_in_the_width_less_the_gutter_and_a_width_of_two_or_less_gives_one_column()
    {
        Assert.Equal(new[] { "  aaaaaaaa", "  aaaa" }, LayoutDump.Lines(new string('a', 12) + "\n", 10));
        Assert.Equal(new[] { "  a", "  a", "  a" }, LayoutDump.Lines("aaa\n", 2));
    }

    [Fact]
    public void Alert_title_line_has_the_gutter_before_the_bar()
    {
        DocumentLayout layout = LayoutDump.Build("> [!TIP]\n> x\n");

        Assert.Equal("  │ Tip", layout.TextOf(0));
        Assert.Equal(4, layout.Line(0).PrefixCells);
        Assert.Equal(StyleRole.AlertTitle, layout.Line(0).Cells[4].Style.Role);
    }

    [Fact]
    public void Gutter_is_the_first_prefix_cells_whatever_the_nesting()
    {
        DocumentLayout layout = LayoutDump.Build("> - a\n>\n>   ```py\n>   x\n>   ```\n", 40);

        Assert.Equal(" ", layout.Line(0).Cells[0].Text);
        Assert.Equal(StyleRole.Body, layout.Line(0).Cells[0].Style.Role);
        Assert.Equal("▾", layout.Line(2).Cells[0].Text);
        Assert.Equal(StyleRole.FoldGlyph, layout.Line(2).Cells[0].Style.Role);
        Assert.Equal(6, layout.Line(2).PrefixCells);
    }

    [Fact]
    public void Block_layout_prefix_is_empty_for_a_line_without_cells()
    {
        BlockLayout block = BlockLayout.Layout(RenderDocument.Parse("```\n\n```\n").Blocks[0], 40);

        Assert.Empty(block.Lines[1].Cells);
    }
}
