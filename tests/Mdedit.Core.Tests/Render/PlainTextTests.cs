using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>The plain text of ranges: markup of the source dropped, rendering markup (bullets, tabs, task boxes) kept.</summary>
public sealed class PlainTextTests
{
    /// <summary>The plain text of the whole source.</summary>
    private static string Whole(string source) => TextExtraction.PlainText(RenderDocument.Parse(source), 0, source.Length);

    /// <summary>The plain text of the range from the first occurrence of <paramref name="from"/> to the end of the source.</summary>
    private static string From(string source, string from) =>
        TextExtraction.PlainText(RenderDocument.Parse(source), source.IndexOf(from, StringComparison.Ordinal), source.Length);

    [Fact]
    public void Emphasis_markers_are_dropped_and_soft_breaks_are_spaces()
    {
        Assert.Equal("Some em text more", Whole("Some *em* text\nmore\n"));
    }

    [Fact]
    public void Nested_items_and_later_paragraphs_of_an_item_are_indented_as_on_screen()
    {
        Assert.Equal("- a\n  - b\n- c", Whole("- a\n  - b\n- c\n"));
        Assert.Equal("1. one\n\n   two", Whole("1. one\n\n   two\n"));
    }

    [Fact]
    public void Two_paragraphs_are_joined_by_a_blank_line()
    {
        Assert.Equal("a\n\nb", Whole("a\n\nb\n"));
    }

    [Fact]
    public void Tight_list_items_are_joined_by_a_line_feed_with_their_bullets()
    {
        Assert.Equal("- a\n- b", Whole("- a\n- b\n"));
    }

    [Fact]
    public void A_range_from_after_the_first_character_of_an_item_has_no_bullet_on_that_item_but_one_on_the_next()
    {
        // "- a\n- b\n": the first item's text is 'a' at 2; the range starts after the newline that follows it.
        Assert.Equal("- b", TextExtraction.PlainText(RenderDocument.Parse("- a\n- b\n"), 3, 8));
    }

    [Fact]
    public void A_range_starting_at_the_first_character_of_an_item_has_its_bullet()
    {
        Assert.Equal("- a\n- b", TextExtraction.PlainText(RenderDocument.Parse("- a\n- b\n"), 2, 8));
    }

    [Fact]
    public void Ordered_items_keep_their_marker_as_written()
    {
        Assert.Equal("3) x", Whole("3) x\n"));
    }

    [Fact]
    public void Quote_bars_are_dropped()
    {
        Assert.Equal("q", Whole("> q\n"));
    }

    [Fact]
    public void Task_boxes_stay_as_text()
    {
        Assert.Equal("- [x] done", Whole("- [x] done\n"));
    }

    [Fact]
    public void A_hard_break_is_a_line_feed()
    {
        Assert.Equal("a\nb", Whole("a  \nb\n"));
    }

    [Fact]
    public void Code_lines_are_joined_by_line_feeds_without_fence_or_header()
    {
        Assert.Equal("a\nb", Whole("```\r\na\r\nb\r\n```\r\n"));
    }

    [Fact]
    public void Blank_code_lines_inside_the_range_are_kept()
    {
        Assert.Equal("a\n\nb", Whole("```\na\n\nb\n```\n"));
    }

    [Fact]
    public void Table_cells_are_joined_by_tabs_and_rows_by_line_feeds()
    {
        Assert.Equal("a\tb\n1\t2", Whole("| a | b |\n|---|---|\n| 1 | 2 |\n"));
    }

    [Fact]
    public void A_range_covering_only_some_cells_gives_only_those_cells_and_rows()
    {
        const string source = "| a | b |\n|---|---|\n| 1 | 2 |\n";
        int start = source.IndexOf('b');
        int end = source.IndexOf('2');

        Assert.Equal("b\n1", TextExtraction.PlainText(RenderDocument.Parse(source), start, end));
    }

    [Fact]
    public void Entities_are_their_transcoded_text()
    {
        Assert.Equal("a & b", Whole("a &amp; b"));
    }

    [Fact]
    public void An_image_is_its_marker_and_alt_text()
    {
        Assert.Equal("[image] alt", Whole("![alt](x.png)"));
    }

    [Fact]
    public void The_image_marker_is_left_out_when_the_range_starts_after_the_first_alt_character()
    {
        const string source = "![alt](x.png)";

        Assert.Equal("lt", TextExtraction.PlainText(RenderDocument.Parse(source), 3, 5));
    }

    [Fact]
    public void A_range_inside_a_folded_section_is_copied_like_any_other()
    {
        // Folding is a layout concept: the copy takes the document and a range only, and the text of a heading is copied as is.
        Assert.Equal("Heading\n\nBody", Whole("# Heading\n\nBody\n"));
    }

    [Fact]
    public void A_range_is_clamped_to_the_text()
    {
        RenderDocument doc = RenderDocument.Parse("abc\n");

        Assert.Equal("abc", TextExtraction.PlainText(doc, -5, 100));
        Assert.Equal(string.Empty, TextExtraction.PlainText(doc, 2, 1));
    }

    [Fact]
    public void Heading_text_is_copied_without_its_markers()
    {
        Assert.Equal("Title", From("## Title\n", "Title"));
    }
}
