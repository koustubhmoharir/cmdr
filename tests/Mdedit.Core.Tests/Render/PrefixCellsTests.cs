using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>
/// The prefix of each display line: the cells of its container prefix (quote bars, list markers and their indentation),
/// which the horizontal scroll keeps in place. The heading fold glyph, the code wrap marker and the alert title are content.
/// </summary>
public sealed class PrefixCellsTests
{
    [Fact]
    public void Paragraph_in_a_nested_list_in_a_quote_counts_the_bar_and_both_item_columns()
    {
        DocumentLayout layout = LayoutDump.Build("> - outer\n>   - inner\n", 40);

        Assert.Equal(new[] { "│ • outer", "│   ◦ inner" }, Lines(layout));
        Assert.Equal(4, layout.Line(0).PrefixCells);
        Assert.Equal(6, layout.Line(1).PrefixCells);
    }

    [Fact]
    public void Heading_fold_glyph_is_content_and_not_prefix()
    {
        DocumentLayout layout = LayoutDump.Build("> # T\n", 40);

        Assert.Equal("│ ▾ T", layout.TextOf(0));
        Assert.Equal(2, layout.Line(0).PrefixCells);
        Assert.Equal(StyleRole.FoldGlyph, layout.Line(0).Cells[2].Style.Role);
    }

    [Fact]
    public void Code_wrap_marker_is_content_and_not_prefix()
    {
        DocumentLayout layout = LayoutDump.Build("```\nabcdefghij\n```\n", 6);

        Assert.Equal("↪ghij", layout.TextOf(2));
        Assert.Equal(0, layout.Line(2).PrefixCells);
        Assert.Equal("↪", layout.Line(2).Cells[0].Text);
    }

    [Fact]
    public void Code_wrap_inside_a_quote_has_the_bar_as_prefix_and_the_marker_after_it()
    {
        DocumentLayout layout = LayoutDump.Build("> ```\n> abcdefghij\n> ```\n", 8);

        Assert.Equal(new[] { "│ ▾ code", "│ abcdef", "│ ↪ghij" }, Lines(layout));
        Assert.All(Enumerable.Range(0, layout.LineCount), line => Assert.Equal(2, layout.Line(line).PrefixCells));
    }

    [Fact]
    public void Alert_title_is_content_after_the_bar()
    {
        DocumentLayout layout = LayoutDump.Build("> [!NOTE]\n> hi\n", 40);

        Assert.Equal(2, layout.Line(0).PrefixCells);
        Assert.Equal("N", layout.Line(0).Cells[2].Text);
    }

    [Fact]
    public void Separator_between_two_quotes_is_all_prefix()
    {
        DocumentLayout layout = LayoutDump.Build("> a\n\n> b\n", 40);

        Assert.Equal("│ ", layout.TextOf(1));
        Assert.Equal(layout.Line(1).Cells.Length, layout.Line(1).PrefixCells);
    }

    [Fact]
    public void Separator_between_plain_blocks_has_no_prefix()
    {
        DocumentLayout layout = LayoutDump.Build("a\n\nb\n", 40);

        Assert.Equal(0, layout.Line(1).PrefixCells);
        Assert.Empty(layout.Line(1).Cells);
    }

    [Fact]
    public void Thematic_break_and_plain_text_have_no_prefix_and_a_wrapped_paragraph_scrolls_by_nothing()
    {
        DocumentLayout layout = LayoutDump.Build("***\n\nabcdefghijklmnop\n", 5);

        Assert.Equal(0, layout.Line(0).PrefixCells);
        Assert.Equal(0, layout.Blocks[0].ScrollWidth);
        Assert.Equal(0, layout.Blocks[1].ScrollWidth);
    }

    [Fact]
    public void Quote_prefix_is_not_counted_in_the_scroll_width_of_a_long_code_line_that_wraps()
    {
        DocumentLayout layout = LayoutDump.Build("> ```\n> abcdefghijklmnop\n> ```\n", 8);

        Assert.Equal(0, layout.Blocks[0].ScrollWidth);
    }

    private static string[] Lines(DocumentLayout layout) => Enumerable.Range(0, layout.LineCount).Select(layout.TextOf).ToArray();
}
