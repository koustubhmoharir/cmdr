using Mdedit.Core.Editing;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Editing;

/// <summary>Typing at a caret: the insertion point, the style it takes, escaping, and the places that are raw or refused.</summary>
public sealed class InsertTests
{
    [Fact]
    public void Typing_mid_word_inserts_between_the_letters()
    {
        Assert.Equal(new EditCase.Outcome("axb", 2), EditCase.Type("ab", 1, "x"));
    }

    [Fact]
    public void Typing_at_a_block_start_takes_the_style_of_the_next_character()
    {
        Assert.Equal(new EditCase.Outcome("**xa**", 3), EditCase.Type("**a**", 2, "x"));
    }

    [Fact]
    public void Typing_at_the_end_of_bold_continues_the_bold()
    {
        Assert.Equal(new EditCase.Outcome("**ax**", 4), EditCase.Type("**a**", 3, "x"));
    }

    [Fact]
    public void Typing_at_the_end_of_italic_continues_the_italic_inside_strikethrough()
    {
        Assert.Equal(new EditCase.Outcome("~~a*bx*~~", 6), EditCase.Type("~~a*b*~~", 5, "x"));
    }

    [Fact]
    public void Typing_before_a_plain_character_that_follows_bold_joins_the_bold()
    {
        // The text goes after the previous cell, "a", so it is inside the strong span, not in front of "b".
        Assert.Equal(new EditCase.Outcome("**ax**b", 4), EditCase.Type("**a**b", 5, "x"));
    }

    [Fact]
    public void Typing_after_a_link_is_outside_the_link()
    {
        Assert.Equal(new EditCase.Outcome("[a](u)x b", 7), EditCase.Type("[a](u) b", 6, "x"));
    }

    [Fact]
    public void Typing_after_a_link_inside_bold_is_outside_the_link_and_still_bold()
    {
        // The link is at 2..8; the typed text goes after its ")" at 8, inside the strong span that closes at 8..10.
        Assert.Equal(new EditCase.Outcome("**[a](u)x** b", 9), EditCase.Type("**[a](u)** b", 10, "x"));
    }

    [Fact]
    public void Typing_at_the_end_of_a_paragraph_that_ends_in_a_link_is_outside_the_link()
    {
        // The end position of "[a](u)" is after the link's text, before its "]", so the link's end is the place.
        Assert.Equal(new EditCase.Outcome("[a](u)x", 7), EditCase.Type("[a](u)", 2, "x"));
    }

    [Fact]
    public void Typing_at_the_start_of_a_link_text_is_inside_the_link()
    {
        Assert.Equal(new EditCase.Outcome("[xa](u)", 2), EditCase.Type("[a](u)", 1, "x"));
    }

    [Fact]
    public void Typing_in_a_code_span_is_raw()
    {
        Assert.Equal(new EditCase.Outcome("`a*b`", 3), EditCase.Type("`ab`", 2, "*"));
    }

    [Fact]
    public void Typing_at_the_end_of_a_code_span_is_raw_and_inside_the_span()
    {
        Assert.Equal(new EditCase.Outcome("`a_`", 3), EditCase.Type("`a`", 2, "_"));
    }

    [Theory]
    [InlineData("\\")]
    [InlineData("*")]
    [InlineData("_")]
    [InlineData("`")]
    [InlineData("[")]
    [InlineData("]")]
    [InlineData("<")]
    [InlineData("&")]
    [InlineData("~")]
    public void Markup_characters_are_escaped_where_they_are_typed(string typed)
    {
        Assert.Equal(new EditCase.Outcome("a\\" + typed + "b", 3), EditCase.Type("ab", 1, typed));
    }

    [Theory]
    [InlineData("#")]
    [InlineData(">")]
    [InlineData("-")]
    [InlineData("+")]
    [InlineData("=")]
    [InlineData(".")]
    [InlineData("!")]
    [InlineData("(")]
    [InlineData("|")]
    public void Characters_that_only_matter_at_a_line_start_are_written_as_they_are_mid_line(string typed)
    {
        Assert.Equal(new EditCase.Outcome("a" + typed + "b", 2), EditCase.Type("ab", 1, typed));
    }

    [Theory]
    [InlineData("#")]
    [InlineData(">")]
    [InlineData("-")]
    [InlineData("+")]
    [InlineData("=")]
    public void Characters_that_start_a_block_are_escaped_at_the_start_of_a_paragraph(string typed)
    {
        Assert.Equal(new EditCase.Outcome("\\" + typed + "ab", 2), EditCase.Type("ab", 0, typed));
    }

    [Fact]
    public void A_hash_after_text_on_the_same_line_is_not_escaped()
    {
        Assert.Equal(new EditCase.Outcome("a #b", 3), EditCase.Type("a b", 2, "#"));
    }

    [Fact]
    public void A_hash_at_the_start_of_a_line_after_a_soft_break_is_escaped()
    {
        Assert.Equal(new EditCase.Outcome("a\n\\#b", 4), EditCase.Type("a\nb", 2, "#"));
    }

    [Fact]
    public void A_hash_after_a_heading_marker_is_not_escaped()
    {
        Assert.Equal(new EditCase.Outcome("# #ab", 3), EditCase.Type("# ab", 2, "#"));
    }

    [Fact]
    public void A_dash_after_a_list_marker_is_escaped()
    {
        Assert.Equal(new EditCase.Outcome("- \\#ab", 4), EditCase.Type("- ab", 2, "#"));
    }

    [Fact]
    public void A_dot_after_digits_at_the_start_of_a_paragraph_is_escaped_so_it_is_not_a_list()
    {
        Assert.Equal(new EditCase.Outcome("1\\.", 3), EditCase.Type("1", 1, "."));
    }

    [Fact]
    public void A_dot_after_digits_at_the_end_of_a_paragraph_is_escaped()
    {
        Assert.Equal(new EditCase.Outcome("12\\.", 4), EditCase.Type("12", 2, "."));
    }

    [Fact]
    public void A_dot_after_text_is_not_escaped()
    {
        Assert.Equal(new EditCase.Outcome("a1.", 3), EditCase.Type("a1", 2, "."));
    }

    [Fact]
    public void Typing_inside_an_escape_puts_the_character_after_the_escape()
    {
        Assert.Equal(new EditCase.Outcome("a\\*xb", 4), EditCase.Type("a\\*b", 3, "x"));
    }

    [Fact]
    public void Typing_after_an_entity_is_after_the_whole_entity()
    {
        Assert.Equal(new EditCase.Outcome("a&amp;xb", 7), EditCase.Type("a&amp;b", 6, "x"));
    }

    [Fact]
    public void Typing_in_a_heading_is_plain_text_and_keeps_the_heading()
    {
        Assert.Equal(new EditCase.Outcome("# xab", 3), EditCase.Type("# ab", 2, "x"));
    }

    [Fact]
    public void Typing_in_a_quote_keeps_the_quote()
    {
        Assert.Equal(new EditCase.Outcome("> xab", 3), EditCase.Type("> ab", 2, "x"));
    }

    [Fact]
    public void Typing_in_a_list_item_keeps_the_item()
    {
        Assert.Equal(new EditCase.Outcome("- xab", 3), EditCase.Type("- ab", 2, "x"));
    }

    [Fact]
    public void Typing_in_a_code_block_is_raw_even_at_a_line_start()
    {
        Assert.Equal(new EditCase.Outcome("```\n#ab\n```", 5), EditCase.Type("```\nab\n```", 4, "#"));
    }

    [Fact]
    public void Typing_in_a_code_block_inserts_at_the_caret()
    {
        Assert.Equal(new EditCase.Outcome("```\na*b\n```", 6), EditCase.Type("```\nab\n```", 5, "*"));
    }

    [Fact]
    public void Typing_in_a_table_cell_is_refused_until_M6c()
    {
        const string source = "| a | b |\n|---|---|\n| 1 | 2 |\n";
        int cell = source.IndexOf('1');

        Assert.Equal(EditMessages.Table, EditCase.Refusal(ViewEdit.Insert(RenderDocument.Parse(source), cell, "x")));
    }

    [Fact]
    public void A_typed_line_break_is_refused()
    {
        Assert.Equal(EditMessages.LineBreak, EditCase.Refusal(ViewEdit.Insert(RenderDocument.Parse("ab"), 1, "\n")));
    }

    [Fact]
    public void A_caret_on_no_stop_is_refused()
    {
        Assert.Equal("Can't type here", EditCase.Refusal(ViewEdit.Insert(RenderDocument.Parse("ab"), 9, "x")));
    }

    [Fact]
    public void A_bang_before_a_link_would_make_an_image_and_is_refused_by_the_verifier()
    {
        // Escaping cannot help: "!" then "[" is an image opener, and the "[" is markup, not a cell to escape.
        Assert.Equal("Can't edit this here; Ctrl+T edits the source", EditCase.Refusal(ViewEdit.Insert(RenderDocument.Parse("a[x](u)"), 2, "!")));
    }

    [Fact]
    public void A_backtick_typed_inside_a_code_span_is_refused_by_the_verifier()
    {
        // Raw, the backtick closes the span early; the rendered text would be "ab`", not "a`b".
        Assert.Equal("Can't edit this here; Ctrl+T edits the source", EditCase.Refusal(ViewEdit.Insert(RenderDocument.Parse("`ab`"), 2, "`")));
    }
}
