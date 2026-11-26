using System.Collections.Immutable;
using Mdedit.Core.Render;
using Shared.Core;

namespace Mdedit.Core.Tests.Render;

/// <summary>Search over the rendered text, mapped to source ranges.</summary>
public sealed class RenderSearchTests
{
    /// <summary>Finds a query in the source and returns the source text of each match.</summary>
    private static List<string> Found(string source, string query, bool regex = false, bool caseSensitive = false)
    {
        SearchPattern pattern = SearchPattern.Create(new SearchQuery(query, caseSensitive, false, regex), out _)!;
        RenderSearchResult result = RenderSearch.Find(RenderDocument.Parse(source), pattern);
        return result.Matches.Select(range => source[range.Start..range.End]).ToList();
    }

    [Fact]
    public void A_plain_query_is_case_insensitive_and_finds_text_in_a_paragraph()
    {
        Assert.Equal(["text", "TEXT"], Found("some text and TEXT", "text"));
    }

    [Fact]
    public void A_match_in_emphasis_is_mapped_without_the_delimiters()
    {
        Assert.Equal(["text"], Found("some *text* here", "text"));
    }

    [Fact]
    public void A_heading_is_searched_without_its_fold_glyph()
    {
        Assert.Equal(["text"], Found("# Heading text\n", "text"));
        Assert.Empty(Found("# Heading text\n", "▾"));
    }

    [Fact]
    public void A_code_line_is_searched()
    {
        Assert.Equal(["text"], Found("```\nfoo text\n```\n", "text"));
    }

    [Fact]
    public void A_table_cell_is_searched()
    {
        Assert.Equal(["text"], Found("| a | text |\n|---|---|\n", "text"));
    }

    [Fact]
    public void A_match_across_a_soft_break_runs_from_the_first_character_to_the_end_of_the_last()
    {
        Assert.Equal(["foo\nbar"], Found("foo\nbar", "foo bar"));
    }

    [Fact]
    public void A_match_does_not_cross_a_hard_break()
    {
        Assert.Empty(Found("foo  \nbar\n", "foo bar"));
        Assert.Equal(["foo"], Found("foo  \nbar\n", "foo"));
    }

    [Fact]
    public void A_match_does_not_cross_blocks()
    {
        Assert.Empty(Found("foo\n\nbar\n", "foo bar"));
    }

    [Fact]
    public void A_match_through_an_escape_spans_the_backslash()
    {
        Assert.Equal(["a\\*b"], Found("a\\*b", "a*b"));
    }

    [Fact]
    public void A_match_on_an_entity_spans_the_whole_reference()
    {
        Assert.Equal(["&amp;T"], Found("AT&amp;T", "&T"));
    }

    [Fact]
    public void A_match_ending_on_an_entity_spans_the_whole_reference()
    {
        Assert.Equal(["&amp;"], Found("AT&amp;", "&"));
    }

    [Fact]
    public void A_regex_query_matches_the_rendered_text()
    {
        Assert.Equal(["12", "345"], Found("a 12 b 345", @"\d+", regex: true));
    }

    [Fact]
    public void The_image_marker_alone_is_not_a_match()
    {
        Assert.Empty(Found("![alt](x.png)", "image"));
        Assert.Equal(["alt"], Found("![alt](x.png)", "alt"));
    }

    [Fact]
    public void A_match_that_starts_on_the_image_marker_is_trimmed_to_the_alt_text()
    {
        Assert.Equal(["alt"], Found("![alt](x.png)", "] alt"));
    }

    [Fact]
    public void A_simple_pattern_does_not_time_out()
    {
        SearchPattern pattern = SearchPattern.Create(new SearchQuery("text", false, false, false), out _)!;

        Assert.False(RenderSearch.Find(RenderDocument.Parse("text\n"), pattern).TimedOut);
    }

    [Fact]
    public void Matches_are_in_document_order_and_are_absolute()
    {
        const string source = "> one x\n\n- two x\n";
        SearchPattern pattern = SearchPattern.Create(new SearchQuery("x", false, false, false), out _)!;

        ImmutableArray<SourceRange> matches = RenderSearch.Find(RenderDocument.Parse(source), pattern).Matches;

        Assert.Equal([source.IndexOf('x'), source.LastIndexOf('x')], matches.Select(range => range.Start));
    }
}
