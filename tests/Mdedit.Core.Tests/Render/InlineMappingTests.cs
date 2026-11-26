using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>Exact cells, styles and absolute offsets for each row of the inline mapping table, through the first paragraph.</summary>
public sealed class InlineMappingTests
{
    /// <summary>The cells of the first run of the first block, which is a paragraph in every test here.</summary>
    private static string Cells(string source) =>
        RenderDump.Cells(RenderDocument.Parse(source).Blocks[0], 0);

    private static RenderBlock First(string source) => RenderDocument.Parse(source).Blocks[0];

    [Fact]
    public void Literal_text_maps_one_for_one()
    {
        Assert.Equal("a@0|b@1|c@2", Cells("abc"));
    }

    [Fact]
    public void Backslash_escape_gives_the_punctuation_at_its_own_offset_and_the_backslash_is_unmapped()
    {
        // The asterisk is at source index 2; the backslash at 1 is not a cell.
        Assert.Equal("a@0|*@2|b@3", Cells("a\\*b"));
    }

    [Fact]
    public void Escaped_backslash_maps_to_the_second_backslash()
    {
        Assert.Equal("a@0|\\@2", Cells("a\\\\"));
    }

    [Fact]
    public void Named_entity_gives_its_text_with_every_cell_at_the_ampersand()
    {
        Assert.Equal("&@0", Cells("&amp;"));
        Assert.Equal("©@0", Cells("&copy;"));
    }

    [Fact]
    public void Numeric_entity_gives_its_character_at_the_ampersand()
    {
        Assert.Equal("A@0", Cells("&#65;"));
    }

    [Fact]
    public void Nbsp_entity_gives_a_no_break_space_at_the_ampersand()
    {
        Assert.Equal(" @0", Cells("&nbsp;"));
    }

    [Fact]
    public void Entity_inside_text_keeps_its_neighbours_offsets()
    {
        // "x &amp; y": the entity starts at 2, the space after it is at 7.
        Assert.Equal("x@0| @1|&@2| @7|y@8", Cells("x &amp; y"));
    }

    [Fact]
    public void Soft_break_is_a_space_at_the_newline()
    {
        Assert.Equal("a@0| @1|b@2", Cells("a\nb"));
    }

    [Fact]
    public void Soft_break_with_crlf_is_a_space_at_the_carriage_return()
    {
        Assert.Equal("a@0| @1|b@3", Cells("a\r\nb"));
    }

    [Fact]
    public void Lone_carriage_return_is_a_soft_break()
    {
        Assert.Equal("a@0| @1|b@2", Cells("a\rb"));
    }

    [Fact]
    public void Hard_break_by_two_spaces_ends_the_run_and_the_next_run_starts_at_the_newline()
    {
        // The two spaces are not cells. The newline is at 3, so the second run starts there.
        RenderBlock block = First("a  \nb");

        Assert.Equal(2, block.Runs.Length);
        Assert.Equal("a@0", RenderDump.Cells(block, 0));
        Assert.Equal(3, block.Runs[1].Start);
        Assert.Equal("b@4", RenderDump.Cells(block, 1));
    }

    [Fact]
    public void Hard_break_by_backslash_ends_the_run_and_the_backslash_is_unmapped()
    {
        // "a\" then a newline at 2, then "b" at 3: the backslash is at 1 and is not a cell.
        RenderBlock block = First("a\\\nb");

        Assert.Equal(2, block.Runs.Length);
        Assert.Equal("a@0", RenderDump.Cells(block, 0));
        // Markdig's span of a backslash break starts at the backslash, so the next run starts there, and "b" is at 3.
        Assert.Equal(1, block.Runs[1].Start);
        Assert.Equal("b@3", RenderDump.Cells(block, 1));
    }

    [Fact]
    public void Code_span_with_one_backtick_is_its_content_in_the_code_style()
    {
        Assert.Equal("a@1+code", Cells("`a`"));
    }

    [Fact]
    public void Code_span_with_two_backticks_and_padding_strips_the_padding_from_the_mapping()
    {
        // "`` `a` ``": the content is "`a`" at 3, 4 and 5; the padding at 2 and 6 is unmapped.
        Assert.Equal("`@3+code|a@4+code|`@5+code", Cells("`` `a` ``"));
    }

    [Fact]
    public void Line_break_inside_a_code_span_is_a_space_at_the_newline()
    {
        Assert.Equal("a@1+code| @2+code|b@3+code", Cells("`a\nb`"));
    }

    [Fact]
    public void Emphasis_is_emphasis_and_its_delimiters_are_unmapped()
    {
        Assert.Equal("a@1+em", Cells("*a*"));
        Assert.Equal("a@1+em", Cells("_a_"));
    }

    [Fact]
    public void Double_delimiters_are_strong()
    {
        Assert.Equal("a@2+strong", Cells("**a**"));
    }

    [Fact]
    public void Double_tilde_is_strikethrough()
    {
        Assert.Equal("a@2+del", Cells("~~a~~"));
    }

    [Fact]
    public void Nested_emphasis_carries_both_flags()
    {
        Assert.Equal("b@3+em+strong|o@4+em+strong|t@5+em+strong|h@6+em+strong", Cells("***both***"));
    }

    [Fact]
    public void Link_text_is_link_with_an_id_and_brackets_and_target_are_unmapped()
    {
        Assert.Equal("a@1+link^0", Cells("[a](u)"));
    }

    [Fact]
    public void Two_links_get_distinct_ids()
    {
        // "[a](u) [b](v)": the second link's text is at 8.
        Assert.Equal("a@1+link^0| @6|b@8+link^1", Cells("[a](u) [b](v)"));
    }

    [Fact]
    public void Emphasis_inside_a_link_is_link_and_emphasis()
    {
        Assert.Equal("a@1+link^0| @2+link^0|b@4+em+link^0", Cells("[a *b*](u)"));
    }

    [Fact]
    public void Image_gives_a_synthetic_marker_then_the_alt_text_in_the_image_style()
    {
        // "![alt *x*](u \"t\")": the alt text is "alt " at 2 to 5 and the emphasised x at 7.
        string cells = Cells("![alt *x*](u \"t\")");

        Assert.StartsWith("[@-/ImageMarker|i@-/ImageMarker|m@-/ImageMarker|a@-/ImageMarker|g@-/ImageMarker|e@-/ImageMarker|]@-/ImageMarker| @-/ImageMarker|", cells);
        Assert.EndsWith("a@2+img|l@3+img|t@4+img| @5+img|x@7+em+img", cells);
    }

    [Fact]
    public void Angle_autolink_maps_the_url_inside_the_brackets_with_a_new_link_id()
    {
        // "<http://a.b>": the URL is at 1 to 10.
        string cells = Cells("<http://a.b>");

        Assert.Equal("h@1+link^0|t@2+link^0|t@3+link^0|p@4+link^0|:@5+link^0|/@6+link^0|/@7+link^0|a@8+link^0|.@9+link^0|b@10+link^0", cells);
    }

    [Fact]
    public void Bare_autolink_is_a_link_over_its_literal_text()
    {
        Assert.Equal("h@0+link^0|t@1+link^0|t@2+link^0|p@3+link^0|:@4+link^0|/@5+link^0|/@6+link^0|c@7+link^0|.@8+link^0|d@9+link^0", Cells("http://c.d"));
    }

    [Fact]
    public void Inline_html_is_html_as_written()
    {
        // "<span>x</span>": the tags are dim HTML, the x is body text.
        Assert.Equal(
            "<@0/Html|s@1/Html|p@2/Html|a@3/Html|n@4/Html|>@5/Html|x@6|<@7/Html|/@8/Html|s@9/Html|p@10/Html|a@11/Html|n@12/Html|>@13/Html",
            Cells("<span>x</span>"));
    }

    [Fact]
    public void Task_box_is_the_box_as_written_then_a_synthetic_space_and_the_text_follows()
    {
        // "- [ ] a": the box is at 2 to 4, the source space at 5 is not a cell (the synthetic space stands for it), and a is at 6.
        RenderBlock block = RenderDocument.Parse("- [ ] a\n- [x] b\n").Blocks[0];
        RenderBlock second = RenderDocument.Parse("- [ ] a\n- [x] b\n").Blocks[1];

        Assert.Equal("[@2/TaskBox| @3/TaskBox|]@4/TaskBox| @-|a@6", RenderDump.Cells(block, 0));
        Assert.Equal("[@10/TaskBox|x@11/TaskBox|]@12/TaskBox| @-|b@14", RenderDump.Cells(second, 0));
    }

    [Fact]
    public void Plain_text_after_a_task_box_keeps_its_offsets()
    {
        const string text = "- [x] done\n";
        RenderBlock block = RenderDocument.Parse(text).Blocks[0];

        Assert.Contains($"d@{text.IndexOf("done", StringComparison.Ordinal)}|", RenderDump.Cells(block, 0));
    }
}
