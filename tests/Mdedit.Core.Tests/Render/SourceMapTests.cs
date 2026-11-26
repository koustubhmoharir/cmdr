using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>Cell offsets of the lines of a document, checked as <c>text@offset</c> lists, and the offsets of prefix and synthetic cells.</summary>
public sealed class SourceMapTests
{
    [Fact]
    public void Prefix_cells_of_a_quote_are_synthetic_and_text_cells_keep_their_offsets()
    {
        DocumentLayout layout = LayoutDump.Build("> hi\n");

        Assert.Equal("│@-/QuoteBar| @-/QuoteBar|h@2|i@3", LayoutDump.Cells(layout, 0));
    }

    [Fact]
    public void Heading_fold_glyph_is_synthetic_and_its_text_is_mapped()
    {
        DocumentLayout layout = LayoutDump.Build("## Hi\n");

        Assert.Equal("▾@-/FoldGlyph| @-/FoldGlyph|H@3/Heading|i@4/Heading", LayoutDump.Cells(layout, 0));
    }

    [Fact]
    public void Code_wrap_marker_is_synthetic_and_the_code_keeps_its_offsets()
    {
        DocumentLayout layout = LayoutDump.Build("```\nabcd\n```\n", 3);

        Assert.Equal("a@4/CodeBlock|b@5/CodeBlock|c@6/CodeBlock", LayoutDump.Cells(layout, 1));
        Assert.Equal("↪@-/WrapMarker|d@7/CodeBlock", LayoutDump.Cells(layout, 2));
    }

    [Fact]
    public void Continuation_of_a_wrapped_word_keeps_the_offsets_of_its_cells()
    {
        DocumentLayout layout = LayoutDump.Build("abcdefghij\n", 6);

        Assert.Equal("a@0|b@1|c@2|d@3|e@4|f@5", LayoutDump.Cells(layout, 0));
        Assert.Equal("g@6|h@7|i@8|j@9", LayoutDump.Cells(layout, 1));
    }

    [Fact]
    public void Dropped_spaces_are_not_shown_and_the_next_line_starts_at_the_next_word()
    {
        DocumentLayout layout = LayoutDump.Build("aaa bbb ccc\n", 7);

        Assert.Equal("a@0|a@1|a@2| @3|b@4|b@5|b@6", LayoutDump.Cells(layout, 0));
        Assert.Equal("c@8|c@9|c@10", LayoutDump.Cells(layout, 1));
        Assert.Equal(7, layout.OffsetAt(0, 40));
    }

    [Fact]
    public void Anchor_of_a_wrapped_line_is_the_first_mapped_cell_after_it()
    {
        DocumentLayout layout = LayoutDump.Build("aaa bbb ccc\n", 7);

        Assert.Equal(7, layout.BaseOffset(0) + layout.Line(0).Anchor);
        Assert.Equal(11, layout.BaseOffset(1) + layout.Line(1).Anchor);
    }

    [Fact]
    public void Ordered_marker_digits_and_delimiter_are_mapped_to_the_source()
    {
        DocumentLayout layout = LayoutDump.Build("10. x\n");

        Assert.Equal("1@0/ListMarker|0@1/ListMarker|.@2/ListMarker| @-/ListMarker|x@4", LayoutDump.Cells(layout, 0));
    }

    [Fact]
    public void Alert_title_cells_are_synthetic()
    {
        DocumentLayout layout = LayoutDump.Build("> [!TIP]\n> x\n");

        Assert.Equal("│@-/QuoteBar| @-/QuoteBar|T@-/AlertTitle|i@-/AlertTitle|p@-/AlertTitle", LayoutDump.Cells(layout, 0));
    }

    [Fact]
    public void Position_of_the_cell_in_a_wrapped_line_is_its_line_and_column()
    {
        DocumentLayout layout = LayoutDump.Build("aaa bbb ccc\n", 7);

        Assert.Equal((1, 0), layout.PositionOf(8));
        Assert.Equal((0, 4), layout.PositionOf(4));
    }

    [Fact]
    public void Offset_at_a_column_of_a_mapped_cell_after_a_zero_width_cell_is_that_cell()
    {
        // The combining mark joins the cell before it, so the cell after the 'e' is the 'b' at column 1.
        DocumentLayout layout = LayoutDump.Build("éb\n");

        Assert.Equal(2, layout.OffsetAt(0, 1));
    }

    [Fact]
    public void Empty_ordered_item_maps_its_digit_and_delimiter_to_offsets_zero_and_one()
    {
        // The marker lies before the empty paragraph that carries it, at relative offsets -2 and -1: negative, and the
        // delimiter is at -1, which is not the synthetic sentinel.
        DocumentLayout layout = LayoutDump.Build("1.\n");

        Assert.Equal("1@0/ListMarker|.@1/ListMarker| @-/ListMarker", LayoutDump.Cells(layout, 0));
    }
}
