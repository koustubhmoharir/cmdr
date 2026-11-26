using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>Block kinds, spans, containers, first-leaf flags, tightness and the lines of code, HTML and table blocks.</summary>
public sealed class BlockLoweringTests
{
    private static RenderDocument Parse(string text) => RenderDocument.Parse(text);

    [Fact]
    public void Atx_heading_has_its_level_and_its_text_in_the_heading_role()
    {
        RenderBlock block = Parse("# Head\n").Blocks[0];

        Assert.Equal("Heading 0..6 level=1 lang=- tight=no first=no containers=[-]", RenderDump.Shape(block));
        Assert.Equal("H@2/Heading#1|e@3/Heading#1|a@4/Heading#1|d@5/Heading#1", RenderDump.Cells(block, 0));
    }

    [Fact]
    public void Setext_heading_spans_both_lines_and_has_level_1()
    {
        RenderBlock block = Parse("Title\n===\n").Blocks[0];

        Assert.Equal("Heading 0..9 level=1 lang=- tight=no first=no containers=[-]", RenderDump.Shape(block));
        Assert.Equal("T@0/Heading#1|i@1/Heading#1|t@2/Heading#1|l@3/Heading#1|e@4/Heading#1", RenderDump.Cells(block, 0));
    }

    [Fact]
    public void Setext_heading_with_dashes_has_level_2()
    {
        RenderBlock block = Parse("Sub\n---\n").Blocks[0];

        Assert.Equal(BlockKind.Heading, block.Kind);
        Assert.Equal(2, block.HeadingLevel);
    }

    [Fact]
    public void Paragraphs_are_separate_blocks_with_their_spans()
    {
        RenderDocument document = Parse("a\nb\n\nc");

        Assert.Equal(2, document.Blocks.Length);
        Assert.Equal("Paragraph 0..3 level=0 lang=- tight=no first=no containers=[-]", RenderDump.Shape(document.Blocks[0]));
        Assert.Equal("a@0| @1|b@2", RenderDump.Cells(document.Blocks[0], 0));
        Assert.Equal("Paragraph 5..6 level=0 lang=- tight=no first=no containers=[-]", RenderDump.Shape(document.Blocks[1]));
        Assert.Equal("c@5", RenderDump.Cells(document.Blocks[1], 0));
    }

    [Fact]
    public void Fenced_code_has_its_language_and_one_run_per_line_with_empty_lines_kept()
    {
        const string text = "```py title\nx = 1\n\n  y\n```\n";
        RenderBlock block = Parse(text).Blocks[0];

        Assert.Equal("Code 0..26 level=0 lang=py tight=no first=no containers=[-]", RenderDump.Shape(block));
        Assert.Equal(3, block.Runs.Length);
        Assert.Equal("x = 1//  y", RenderDump.Texts(block));
        Assert.Equal(12, block.Runs[0].Start);
        Assert.Equal(18, block.Runs[1].Start);
        Assert.Equal(19, block.Runs[2].Start);
        Assert.Equal(" @19/CodeBlock| @20/CodeBlock|y@21/CodeBlock", RenderDump.Cells(block, 2));
    }

    [Fact]
    public void Fenced_code_without_info_has_no_language()
    {
        RenderBlock block = Parse("```\nx\n```\n").Blocks[0];

        Assert.Null(block.Language);
        Assert.Equal("x@4/CodeBlock", RenderDump.Cells(block, 0));
    }

    [Fact]
    public void Indented_code_with_crlf_has_lines_without_the_indentation()
    {
        // "    code\r\n      two\r\n": the second line keeps two spaces of its own indentation after the four that are removed.
        const string text = "    code\r\n      two\r\n";
        RenderBlock block = Parse(text).Blocks[0];

        Assert.Equal("Code 0..19 level=0 lang=- tight=no first=no containers=[-]", RenderDump.Shape(block));
        Assert.Equal("code", RenderDump.Text(block, 0));
        Assert.Equal(4, block.Runs[0].Start);
        Assert.Equal("  two", RenderDump.Text(block, 1));
        Assert.Equal(14, block.Runs[1].Start);
        Assert.Equal(" @14/CodeBlock| @15/CodeBlock|t@16/CodeBlock|w@17/CodeBlock|o@18/CodeBlock", RenderDump.Cells(block, 1));
    }

    [Fact]
    public void Thematic_break_has_no_runs()
    {
        RenderBlock block = Parse("---\n").Blocks[0];

        Assert.Equal("ThematicBreak 0..3 level=0 lang=- tight=no first=no containers=[-]", RenderDump.Shape(block));
        Assert.Empty(block.Runs);
    }

    [Fact]
    public void Html_block_has_one_run_per_source_line_in_the_html_role()
    {
        RenderBlock block = Parse("<div>\nx\n</div>\n\nafter").Blocks[0];

        Assert.Equal("Html 0..14 level=0 lang=- tight=no first=no containers=[-]", RenderDump.Shape(block));
        Assert.Equal("<div>/x/</div>", RenderDump.Texts(block));
        Assert.Equal("x@6/Html", RenderDump.Cells(block, 1));
        Assert.Equal(8, block.Runs[2].Start);
    }

    [Fact]
    public void Pipe_table_is_lowered_as_its_source_lines_in_the_body_role()
    {
        const string text = "| a | b |\n|---|---|\n| 1 | 2 |\n";
        RenderBlock block = Parse(text).Blocks[0];

        Assert.Equal("Table 0..29 level=0 lang=- tight=no first=no containers=[-]", RenderDump.Shape(block));
        Assert.Equal("| a | b |/|---|---|/| 1 | 2 |", RenderDump.Texts(block));
        Assert.Equal(10, block.Runs[1].Start);
        Assert.Equal(20, block.Runs[2].Start);
    }

    [Fact]
    public void Link_reference_definition_is_one_raw_block_of_its_source_cells_in_the_html_role()
    {
        const string text = "[x]: https://example.com\n";
        RenderBlock block = Parse(text).Blocks[0];

        Assert.Equal("Raw 0..24 level=0 lang=- tight=no first=no containers=[-]", RenderDump.Shape(block));
        Assert.Equal("[x]: https://example.com", RenderDump.Text(block, 0));
        Assert.Equal(string.Join(",", Enumerable.Range(0, 24)), RenderDump.Offsets(block, 0));
        Assert.StartsWith("[@0/Html|x@1/Html|]@2/Html|:@3/Html|", RenderDump.Cells(block, 0));
    }

    [Fact]
    public void Definition_and_a_paragraph_using_it_keep_both_blocks_in_order()
    {
        const string text = "[x]: https://example.com\n\n[link][x]\n";
        RenderDocument document = Parse(text);

        Assert.Equal(2, document.Blocks.Length);
        Assert.Equal(BlockKind.Raw, document.Blocks[0].Kind);
        Assert.Equal(BlockKind.Paragraph, document.Blocks[1].Kind);
        Assert.Equal(text.IndexOf("[link]", StringComparison.Ordinal), document.Blocks[1].Start);
        Assert.Equal("link", RenderDump.Text(document.Blocks[1], 0));
    }

    [Fact]
    public void Unknown_alert_kind_is_a_quote_whose_first_line_is_the_marker_as_written()
    {
        // "> [!FOO]\n> text\n": the marker is at 2 to 7, the text at 11 to 14.
        const string text = "> [!FOO]\n> text\n";
        RenderDocument document = Parse(text);

        Assert.Equal(2, document.Blocks.Length);
        Assert.Equal("Paragraph 2..8 level=0 lang=- tight=no first=no containers=[quote*]", RenderDump.Shape(document.Blocks[0]));
        Assert.Equal("[@2|!@3|F@4|O@5|O@6|]@7", RenderDump.Cells(document.Blocks[0], 0));
        // Markdig's span of the content paragraph starts at the marker line (as for a known alert), so its Start is 2.
        Assert.Equal("Paragraph 2..15 level=0 lang=- tight=no first=no containers=[quote]", RenderDump.Shape(document.Blocks[1]));
        Assert.Equal("t@11|e@12|x@13|t@14", RenderDump.Cells(document.Blocks[1], 0));
    }

    [Fact]
    public void Known_alert_kind_still_lowers_as_an_alert_with_no_marker_block()
    {
        RenderDocument document = Parse("> [!NOTE]\n> text\n");

        Assert.Single(document.Blocks);
        Assert.Equal("Paragraph 2..16 level=0 lang=- tight=no first=no containers=[alert:note*]", RenderDump.Shape(document.Blocks[0]));
    }

    [Fact]
    public void Empty_list_item_is_an_empty_paragraph_just_after_its_marker_and_is_the_first_leaf_of_the_item()
    {
        // "- a\n-\n- b\n": the empty item's marker is at 4, so its paragraph is at 5.
        const string text = "- a\n-\n- b\n";
        RenderDocument document = Parse(text);

        Assert.Equal(3, document.Blocks.Length);
        Assert.Equal("Paragraph 5..5 level=0 lang=- tight=yes first=yes containers=[bullet[0]*]", RenderDump.Shape(document.Blocks[1]));
        Assert.Empty(document.Blocks[1].Runs[0].Cells);
        Assert.StartsWith("b@8|", RenderDump.Cells(document.Blocks[2], 0) + "|");
    }

    [Fact]
    public void Empty_ordered_item_is_an_empty_paragraph_after_its_marker_text()
    {
        RenderBlock block = Parse("1.\n").Blocks[0];

        Assert.Equal("Paragraph 2..2 level=0 lang=- tight=yes first=yes containers=[ordered:1.@0*]", RenderDump.Shape(block));
        Assert.Empty(block.Runs[0].Cells);
    }

    [Fact]
    public void Quote_containing_a_paragraph_has_the_quote_as_its_container_and_the_leaf_is_its_first()
    {
        // "> a\n> b\n": the paragraph's source starts at the a and includes the "> " before the b, which is not a cell.
        RenderBlock block = Parse("> a\n> b\n").Blocks[0];

        Assert.Equal("Paragraph 2..7 level=0 lang=- tight=no first=no containers=[quote*]", RenderDump.Shape(block));
        Assert.Equal("a@2| @3|b@6", RenderDump.Cells(block, 0));
    }

    [Fact]
    public void Nested_quote_records_both_quotes_and_the_leaf_is_the_first_of_each()
    {
        RenderDocument document = Parse("> a\n> b\n\n> > nested\n");
        RenderBlock block = document.Blocks[1];

        Assert.Equal("Paragraph 13..19 level=0 lang=- tight=no first=no containers=[quote* quote*]", RenderDump.Shape(block));
        Assert.Equal("nested", RenderDump.Text(block, 0));
    }

    [Fact]
    public void Alert_records_its_kind_and_its_first_leaf_is_the_one_that_gets_the_title()
    {
        const string text = "> [!NOTE]\n> hi\n>\n> there\n";
        RenderDocument document = Parse(text);

        Assert.Equal(2, document.Blocks.Length);
        Assert.Equal("Paragraph 2..14 level=0 lang=- tight=no first=no containers=[alert:note*]", RenderDump.Shape(document.Blocks[0]));
        Assert.Equal("hi", RenderDump.Text(document.Blocks[0], 0));
        Assert.Equal("Paragraph 19..24 level=0 lang=- tight=no first=no containers=[alert:note]", RenderDump.Shape(document.Blocks[1]));
        Assert.Equal("there", RenderDump.Text(document.Blocks[1], 0));
    }

    [Fact]
    public void Alert_with_no_content_is_one_empty_paragraph_so_the_title_still_has_a_leaf()
    {
        RenderBlock block = Parse("> [!WARNING]\n").Blocks[0];

        Assert.Equal("Paragraph 2..12 level=0 lang=- tight=no first=no containers=[alert:warning*]", RenderDump.Shape(block));
        Assert.Empty(block.Runs[0].Cells);
    }

    [Theory]
    [InlineData("NOTE", AlertKind.Note)]
    [InlineData("TIP", AlertKind.Tip)]
    [InlineData("IMPORTANT", AlertKind.Important)]
    [InlineData("WARNING", AlertKind.Warning)]
    [InlineData("CAUTION", AlertKind.Caution)]
    public void Each_alert_kind_is_recognised(string written, AlertKind expected)
    {
        Container container = Parse($"> [!{written}]\n> x\n").Blocks[0].Containers[0];

        Assert.Equal(ContainerKind.Alert, container.Kind);
        Assert.Equal(expected, container.Alert);
    }

    [Fact]
    public void Bullet_list_items_record_their_depth_and_the_outer_item_is_not_first_for_a_nested_leaf()
    {
        const string text = "- a\n  - b\n- c\n";
        RenderDocument document = Parse(text);

        Assert.Equal(3, document.Blocks.Length);
        Assert.Equal("Paragraph 2..3 level=0 lang=- tight=yes first=yes containers=[bullet[0]*]", RenderDump.Shape(document.Blocks[0]));
        Assert.Equal("Paragraph 8..9 level=0 lang=- tight=yes first=yes containers=[bullet[0] bullet[1]*]", RenderDump.Shape(document.Blocks[1]));
        Assert.Equal("Paragraph 12..13 level=0 lang=- tight=yes first=yes containers=[bullet[0]*]", RenderDump.Shape(document.Blocks[2]));
    }

    [Fact]
    public void Ordered_list_records_the_marker_as_written_and_its_offset()
    {
        const string text = "3) x\n4) y\n";
        RenderDocument document = Parse(text);

        Assert.Equal("Paragraph 3..4 level=0 lang=- tight=yes first=yes containers=[ordered:3)@0*]", RenderDump.Shape(document.Blocks[0]));
        Assert.Equal("Paragraph 8..9 level=0 lang=- tight=yes first=yes containers=[ordered:4)@5*]", RenderDump.Shape(document.Blocks[1]));
        Assert.Equal("x@3", RenderDump.Cells(document.Blocks[0], 0));
    }

    [Fact]
    public void Ordered_marker_with_a_dot_is_kept_with_its_delimiter()
    {
        Container container = Parse("1. a\n").Blocks[0].Containers[0];

        Assert.Equal(ContainerKind.OrderedItem, container.Kind);
        Assert.Equal("1.", container.Marker);
        Assert.Equal(-3, container.MarkerOffset);
    }

    [Fact]
    public void Tight_list_blocks_are_tight_and_loose_list_blocks_are_not()
    {
        Assert.True(Parse("- a\n- b\n").Blocks[0].Tight);
        Assert.False(Parse("- a\n\n- b\n").Blocks[0].Tight);
        Assert.False(Parse("- a\n\n- b\n").Blocks[1].Tight);
    }

    [Fact]
    public void List_item_with_two_paragraphs_marks_only_the_first_as_first_in_item()
    {
        const string text = "- a\n\n  p2\n";
        RenderDocument document = Parse(text);

        Assert.Equal(2, document.Blocks.Length);
        Assert.Equal("Paragraph 2..3 level=0 lang=- tight=no first=yes containers=[bullet[0]*]", RenderDump.Shape(document.Blocks[0]));
        Assert.Equal("Paragraph 7..9 level=0 lang=- tight=no first=no containers=[bullet[0]]", RenderDump.Shape(document.Blocks[1]));
    }

    [Fact]
    public void Code_inside_a_quote_is_lowered_without_the_quote_prefix()
    {
        const string text = "> ```\n> a\n>\n> b\n> ```\n";
        RenderBlock block = Parse(text).Blocks[0];

        Assert.Equal(BlockKind.Code, block.Kind);
        Assert.Equal("quote*", RenderDump.Containers(block));
        Assert.Equal("a//b", RenderDump.Texts(block));
        Assert.Equal("a@8/CodeBlock", RenderDump.Cells(block, 0));
    }

    [Fact]
    public void Empty_code_line_has_its_line_start_as_its_run_start()
    {
        RenderBlock block = Parse("```\na\n\nb\n```\n").Blocks[0];

        Assert.Equal("a//b", RenderDump.Texts(block));
        Assert.Equal(6, block.Runs[1].Start);
        Assert.Empty(block.Runs[1].Cells);
    }
}
