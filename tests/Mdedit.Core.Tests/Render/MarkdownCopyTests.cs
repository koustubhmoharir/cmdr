using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>Markdown copies: the source slice made valid on its own, with the markup of the containers it is cut from.</summary>
public sealed class MarkdownCopyTests
{
    /// <summary>The Markdown of <c>[start, end)</c> of the source, with the range given by the text it covers.</summary>
    private static string Cut(string source, string covered, int occurrence = 0)
    {
        int start = -1;
        for (int i = 0; i <= occurrence; i++)
        {
            start = source.IndexOf(covered, start + 1, StringComparison.Ordinal);
        }

        return TextExtraction.Markdown(RenderDocument.Parse(source), start, start + covered.Length);
    }

    [Fact]
    public void The_whole_document_is_its_source()
    {
        const string source = "# Title\n\n- a *b*\n\n> q `c`\n";

        Assert.Equal(source, TextExtraction.Markdown(RenderDocument.Parse(source), 0, source.Length));
    }

    [Fact]
    public void A_range_cut_inside_strong_gets_its_delimiters_back()
    {
        // "**bold text**" with the range over "old te".
        Assert.Equal("**old te**", Cut("**bold text**", "old te"));
    }

    [Fact]
    public void A_link_cut_after_its_text_gets_its_closer()
    {
        Assert.Equal("[click](http://x)", TextExtraction.Markdown(RenderDocument.Parse("[click here](http://x)"), 1, 6));
    }

    [Fact]
    public void A_link_cut_before_its_text_gets_its_opener()
    {
        Assert.Equal("[here](http://x)", TextExtraction.Markdown(RenderDocument.Parse("[click here](http://x)"), 7, 22));
    }

    [Fact]
    public void Nested_containers_cut_inside_get_both_delimiters_in_order()
    {
        // "***both***" with the range over "ot": the outer opener and the inner one before it, the inner closer first.
        Assert.Equal("***ot***", Cut("***both***", "ot"));
    }

    [Fact]
    public void A_code_span_cut_inside_gets_its_backticks_back()
    {
        Assert.Equal("`b`", Cut("x `abc` y", "b"));
    }

    [Fact]
    public void A_range_starting_mid_line_in_a_quoted_item_gets_the_container_prefix()
    {
        const string source = "> - item text\n";

        Assert.Equal("> - text", TextExtraction.Markdown(RenderDocument.Parse(source), source.IndexOf("text", StringComparison.Ordinal), source.Length - 1));
    }

    [Fact]
    public void A_range_starting_at_a_line_start_gets_no_prefix()
    {
        const string source = "> a\n> b\n";

        Assert.Equal("> b\n", TextExtraction.Markdown(RenderDocument.Parse(source), 4, source.Length));
    }

    [Fact]
    public void A_range_inside_a_table_cell_is_the_header_delimiter_and_that_row()
    {
        const string source = "| a | b |\n|---|---|\n| 1 | 2 |\n";

        Assert.Equal("| a | b |\n|---|---|\n| 1 | 2 |", Cut(source, "2"));
        Assert.Equal("| a | b |\n|---|---|", Cut(source, "b"));
    }

    [Fact]
    public void A_range_spanning_two_table_rows_is_the_header_delimiter_and_both_rows()
    {
        const string source = "| a |\n|---|\n| 1 |\n| 2 |\n";

        Assert.Equal("| a |\n|---|\n| 1 |\n| 2 |", TextExtraction.Markdown(RenderDocument.Parse(source), source.IndexOf('1'), source.IndexOf('2') + 1));
    }

    [Fact]
    public void A_range_across_a_paragraph_and_a_table_is_the_source_slice_as_it_is()
    {
        const string source = "para\n\n| a |\n|---|\n| 1 |\n";

        Assert.Equal("para\n\n| a |\n|---|\n| 1", TextExtraction.Markdown(RenderDocument.Parse(source), 0, source.IndexOf('1') + 1));
    }

    [Fact]
    public void A_range_is_clamped_to_the_text()
    {
        RenderDocument doc = RenderDocument.Parse("abc\n");

        Assert.Equal("abc\n", TextExtraction.Markdown(doc, -1, 99));
        Assert.Equal(string.Empty, TextExtraction.Markdown(doc, 3, 1));
    }
}
