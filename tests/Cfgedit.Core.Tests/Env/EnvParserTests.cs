using System.Text;
using Cfgedit.Core.Env;
using Shared.Core;
using static Cfgedit.Core.Tests.Env.EnvTestSupport;

namespace Cfgedit.Core.Tests.Env;

public sealed class EnvParserTests
{
    // ----- Lines -----

    [Fact]
    public void Blank_lines_hold_only_spaces_and_tabs_and_include_their_line_break()
    {
        EnvDocument doc = Parse(" \t\n\n  \r\n");

        Assert.Equal(["blank", "blank", "blank"], doc.Items.Select(Kind).ToArray());
        Assert.Equal(new EnvSpan(0, 3), new EnvSpan(doc.Items[0].Start, doc.Items[0].End));
        Assert.Equal(new EnvSpan(3, 4), new EnvSpan(doc.Items[1].Start, doc.Items[1].End));
        Assert.Equal(new EnvSpan(4, 8), new EnvSpan(doc.Items[2].Start, doc.Items[2].End));
    }

    [Fact]
    public void Comment_line_content_excludes_the_hash_and_keeps_the_rest_of_the_line()
    {
        EnvDocument doc = Parse("  #  spaced \n#\n# a # b");

        Assert.Equal("  spaced ", Assert.IsType<EnvCommentLine>(doc.Items[0]).Comment.Content);
        Assert.Equal("", Assert.IsType<EnvCommentLine>(doc.Items[1]).Comment.Content);
        Assert.Equal(" a # b", Assert.IsType<EnvCommentLine>(doc.Items[2]).Comment.Content);
        AssertAt(doc, Assert.IsType<EnvCommentLine>(doc.Items[0]).Comment.Start, 1, 3);
    }

    [Fact]
    public void Empty_file_has_no_items()
    {
        Assert.Empty(Parse("").Items);
    }

    [Fact]
    public void Final_line_without_a_line_break_ends_at_the_end_of_the_text()
    {
        EnvDocument doc = Parse("A=1");

        Assert.Equal(3, doc.Items[0].End);
        Assert.Equal("1", Entry(doc, 0).Value);
    }

    [Fact]
    public void Lf_cr_and_crlf_each_end_a_line()
    {
        string text = "A=1\rB=2\r\nC=3\nD=4";
        EnvDocument doc = Parse(text);

        Assert.Equal(["1", "2", "3", "4"], doc.Items.Select((_, i) => Entry(doc, i).Value).ToArray());
        Assert.Equal(text, Reassemble(doc));
        AssertAt(doc, Entry(doc, 2).KeySpan.Start, 3, 1);
    }

    [Fact]
    public void A_byte_order_mark_is_kept_by_the_source_and_not_part_of_the_text()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, (byte)'A', (byte)'=', (byte)'1', (byte)'\n'];
        SourceText source = SourceText.Decode(bytes);
        EnvDocument doc = EnvDocument.Parse(source);

        Assert.True(source.HasBom);
        Assert.Equal("A=1\n", source.Text);
        Assert.Equal(source.Text, Reassemble(doc));
        Assert.Equal(bytes, source.Encode());
    }

    [Fact]
    public void Leading_spaces_and_tabs_belong_to_the_entry_span_but_not_to_the_key()
    {
        EnvEntry entry = SingleEntry("\t  K=1\n", out EnvDocument doc);

        Assert.Equal(0, entry.Start);
        Assert.Equal(7, entry.End);
        Assert.Equal("K", Slice(doc, entry.KeySpan));
    }

    [Fact]
    public void Duplicate_keys_are_two_entries()
    {
        EnvDocument doc = Parse("A=1\nA=2\n");

        Assert.Equal(["1", "2"], doc.Items.Select((_, i) => Entry(doc, i).Value).ToArray());
    }

    // ----- Keys and export -----

    [Theory]
    [InlineData("A.b-c_9=1", "A.b-c_9")]
    [InlineData("_x=1", "_x")]
    [InlineData("Logging__LogLevel=1", "Logging__LogLevel")]
    public void Keys_accept_letters_underscores_digits_dots_and_hyphens(string text, string key)
    {
        Assert.Equal(key, SingleEntry(text, out _).Key);
    }

    [Fact]
    public void Export_keyword_followed_by_a_tab_is_recorded()
    {
        EnvEntry entry = SingleEntry("  export\tKEY=1", out EnvDocument doc);

        Assert.NotNull(entry.Export);
        Assert.Equal("export", Slice(doc, entry.Export.Value));
        AssertAt(doc, entry.Export.Value.Start, 1, 3);
        Assert.Equal("KEY", entry.Key);
    }

    [Fact]
    public void Export_followed_by_equals_is_a_key_named_export()
    {
        EnvEntry spaced = SingleEntry("export = 1", out _);
        Assert.Null(spaced.Export);
        Assert.Equal("export", spaced.Key);
        Assert.Equal("1", spaced.Value);

        EnvEntry tight = SingleEntry("export=1", out _);
        Assert.Null(tight.Export);
        Assert.Equal("export", tight.Key);
    }

    [Fact]
    public void A_key_that_starts_with_export_is_not_the_keyword()
    {
        EnvEntry entry = SingleEntry("exported=1", out _);

        Assert.Null(entry.Export);
        Assert.Equal("exported", entry.Key);
    }

    [Fact]
    public void Spaces_and_tabs_around_equals_are_not_part_of_key_or_value()
    {
        EnvEntry entry = SingleEntry(" K \t=\t v \t", out EnvDocument doc);

        Assert.Equal("K", entry.Key);
        Assert.Equal("v", entry.Value);
        Assert.Equal("=", Slice(doc, new EnvSpan(entry.EqualsStart, entry.EqualsStart + 1)));
    }

    // ----- Unquoted values -----

    [Fact]
    public void Unquoted_value_drops_trailing_spaces_and_keeps_inner_ones()
    {
        Assert.Equal("a b", SingleEntry("K=a b\t \n", out _).Value);
    }

    [Fact]
    public void Unquoted_value_keeps_quote_characters_that_are_not_at_its_start()
    {
        EnvEntry entry = SingleEntry("K=it's \"x\"", out _);

        Assert.Equal("it's \"x\"", entry.Value);
        Assert.Equal(EnvQuote.None, entry.Quote);
    }

    [Fact]
    public void Hash_without_a_space_before_it_is_part_of_an_unquoted_value()
    {
        EnvEntry entry = SingleEntry("K=a#b", out _);

        Assert.Equal("a#b", entry.Value);
        Assert.Null(entry.Comment);
    }

    [Fact]
    public void Hash_after_a_space_starts_a_trailing_comment()
    {
        EnvEntry entry = SingleEntry("K=a #b c", out EnvDocument doc);

        Assert.Equal("a", entry.Value);
        Assert.Equal("b c", entry.Comment?.Content);
        AssertAt(doc, entry.Comment!.Value.Start, 1, 5);
        Assert.Equal(8, entry.Comment.Value.End);
    }

    [Fact]
    public void Hash_straight_after_equals_is_part_of_the_value()
    {
        Assert.Equal("#x", SingleEntry("K=#x", out _).Value);
    }

    [Fact]
    public void Hash_after_equals_and_a_space_is_a_comment_and_the_value_is_empty()
    {
        EnvEntry entry = SingleEntry("K= #x", out EnvDocument doc);

        Assert.Equal("", entry.Value);
        Assert.Equal("x", entry.Comment?.Content);
        Assert.Equal(0, entry.ValueSpan.Length);
        AssertAt(doc, entry.ValueSpan.Start, 1, 4);
    }

    [Fact]
    public void Empty_unquoted_value_at_the_end_of_the_line_has_an_empty_span_there()
    {
        EnvEntry plain = SingleEntry("K=", out EnvDocument doc);
        Assert.Equal("", plain.Value);
        Assert.Equal(EnvQuote.None, plain.Quote);
        Assert.Equal(new EnvSpan(2, 2), plain.ValueSpan);

        EnvEntry spaces = SingleEntry("K=   \n", out _);
        Assert.Equal("", spaces.Value);
        Assert.Equal(new EnvSpan(5, 5), spaces.ValueSpan);
        AssertAt(doc, plain.ValueSpan.Start, 1, 3);
    }

    // ----- Single quotes -----

    [Fact]
    public void Single_quoted_value_is_literal()
    {
        EnvEntry entry = SingleEntry("K='a\\b \"c\" $d'", out EnvDocument doc);

        Assert.Equal("a\\b \"c\" $d", entry.Value);
        Assert.Equal(EnvQuote.Single, entry.Quote);
        Assert.Equal("'a\\b \"c\" $d'", Slice(doc, entry.ValueSpan));
    }

    [Fact]
    public void Single_quoted_value_may_span_lines_and_each_line_break_decodes_to_lf()
    {
        EnvEntry entry = SingleEntry("K='a\r\nb\rc\nd'", out EnvDocument doc);

        Assert.Equal("a\nb\nc\nd", entry.Value);
        Assert.Equal(EnvQuote.Single, entry.Quote);
        Assert.Equal(doc.Source.Text.Length, entry.End);
    }

    [Fact]
    public void Empty_single_quoted_value_is_empty_and_quoted()
    {
        EnvEntry entry = SingleEntry("K=''", out _);

        Assert.Equal("", entry.Value);
        Assert.Equal(EnvQuote.Single, entry.Quote);
    }

    // ----- Double quotes -----

    [Fact]
    public void Double_quoted_value_decodes_the_four_escapes_and_the_backslash()
    {
        EnvEntry entry = SingleEntry("K=\"\\n\\r\\t\\\"\\\\\"", out _);

        Assert.Equal("\n\r\t\"\\", entry.Value);
        Assert.Equal(EnvQuote.Double, entry.Quote);
    }

    [Fact]
    public void Double_quoted_value_keeps_a_backslash_before_any_other_character()
    {
        Assert.Equal("a\\qb", SingleEntry("K=\"a\\qb\"", out _).Value);
    }

    [Fact]
    public void Double_quoted_backslash_before_a_line_break_is_kept_and_the_break_decodes_to_lf()
    {
        Assert.Equal("a\\\nb", SingleEntry("K=\"a\\\nb\"", out _).Value);
    }

    [Fact]
    public void Escaped_backslash_before_the_closing_quote_does_not_escape_it()
    {
        Assert.Equal("a\\", SingleEntry("K=\"a\\\\\"", out _).Value);
    }

    [Fact]
    public void Double_quoted_value_may_span_lines_and_each_line_break_decodes_to_lf()
    {
        EnvEntry entry = SingleEntry("K=\"a\r\nb\rc\nd\"", out _);

        Assert.Equal("a\nb\nc\nd", entry.Value);
    }

    [Fact]
    public void Two_entries_around_a_multi_line_value_take_their_offsets_from_the_source()
    {
        EnvDocument doc = Parse("K=\"a\r\nb\"\r\nN=1");

        Assert.Equal(2, doc.Items.Count);
        Assert.Equal(new EnvSpan(0, 10), new EnvSpan(doc.Items[0].Start, doc.Items[0].End));
        Assert.Equal(new EnvSpan(10, 13), new EnvSpan(doc.Items[1].Start, doc.Items[1].End));
        Assert.Equal("N", Entry(doc, 1).Key);
    }

    // ----- Trailing comments after quoted values -----

    [Fact]
    public void Comment_may_follow_a_closing_quote_with_no_space()
    {
        EnvEntry entry = SingleEntry("K=\"a\"#c", out _);

        Assert.Equal("a", entry.Value);
        Assert.Equal("c", entry.Comment?.Content);
    }

    [Fact]
    public void Comment_may_follow_a_closing_quote_after_blanks()
    {
        EnvEntry entry = SingleEntry("K='a'  \t# c", out _);

        Assert.Equal("a", entry.Value);
        Assert.Equal(" c", entry.Comment?.Content);
    }

    // ----- Errors: message, line and column -----

    [Fact]
    public void Unterminated_single_quote_is_reported_at_the_opening_quote()
    {
        AssertParseError("KEY='abc", "unterminated quoted value", 1, 5);
    }

    [Fact]
    public void Unterminated_double_quote_across_lines_is_reported_at_the_opening_quote()
    {
        AssertParseError("A=1\nB=\"x\ny", "unterminated quoted value", 2, 3);
    }

    [Fact]
    public void Escaped_closing_quote_leaves_a_double_quoted_value_unterminated()
    {
        AssertParseError("K=\"a\\\"", "unterminated quoted value", 1, 3);
    }

    [Fact]
    public void Key_followed_by_a_colon_is_expected_equals()
    {
        AssertParseError("KEY: value", "expected '='", 1, 4);
    }

    [Fact]
    public void Key_with_no_equals_sign_is_expected_equals_at_the_end_of_the_key()
    {
        AssertParseError("KEY", "expected '='", 1, 4);
        AssertParseError("KEY value", "expected '='", 1, 5);
        AssertParseError("KEY\nA=1", "expected '='", 1, 4);
    }

    [Fact]
    public void Export_with_no_key_is_expected_equals()
    {
        AssertParseError("export", "expected '='", 1, 7);
    }

    [Fact]
    public void Backtick_quoted_value_is_refused()
    {
        AssertParseError("K=`x`", "backtick quotes are not supported", 1, 3);
    }

    [Fact]
    public void Key_that_starts_with_a_digit_is_an_invalid_key()
    {
        AssertParseError("1A=x", "invalid key", 1, 1);
    }

    [Fact]
    public void Invalid_key_is_reported_on_its_own_line()
    {
        AssertParseError("A=1\n\n2B=x", "invalid key", 3, 1);
    }

    [Fact]
    public void Line_that_starts_with_equals_or_a_quote_is_an_invalid_key()
    {
        AssertParseError("=value", "invalid key", 1, 1);
        AssertParseError("\"KEY\"=1", "invalid key", 1, 1);
    }

    [Fact]
    public void Text_after_a_closing_single_quote_is_refused()
    {
        AssertParseError("K='a'b", "unexpected text after closing quote", 1, 6);
    }

    [Fact]
    public void Text_after_a_closing_double_quote_on_a_later_line_is_refused()
    {
        AssertParseError("A=\"x\ny\"z", "unexpected text after closing quote", 2, 3);
    }

    [Fact]
    public void Missing_equals_after_a_line_break_is_reported_on_the_line_it_is_on()
    {
        AssertParseError("A=1\rB", "expected '='", 2, 2);
    }
}
