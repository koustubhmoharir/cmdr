using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class TextWrapTests
{
    [Fact]
    public void Breaks_after_the_last_space_that_fits()
    {
        Assert.Equal(new[] { "hello world", "foo" }, TextWidth.Wrap("hello world foo", 11));
        Assert.Equal(new[] { "hello", "world", "foo" }, TextWidth.Wrap("hello world foo", 8));
    }

    [Fact]
    public void Spaces_at_a_break_are_dropped()
    {
        Assert.Equal(new[] { "ab", "cd" }, TextWidth.Wrap("ab cd", 2));
        Assert.Equal(new[] { "ab", "cd" }, TextWidth.Wrap("ab   cd", 3));
        Assert.Equal(new[] { "abc", "def" }, TextWidth.Wrap("abc def", 3));
    }

    [Fact]
    public void Trailing_spaces_do_not_make_an_extra_line()
    {
        Assert.Equal(new[] { "ab" }, TextWidth.Wrap("ab ", 2));
        Assert.Equal(new[] { "ab cd" }, TextWidth.Wrap("ab cd ", 5));
    }

    [Fact]
    public void Leading_spaces_of_the_first_line_are_kept()
    {
        Assert.Equal(new[] { " ab" }, TextWidth.Wrap(" ab", 5));
    }

    [Fact]
    public void A_token_longer_than_the_width_is_broken_where_it_does_not_fit()
    {
        Assert.Equal(new[] { "abcd", "efgh", "ij" }, TextWidth.Wrap("abcdefghij", 4));
    }

    [Fact]
    public void A_long_token_after_a_short_word_breaks_at_the_space_first()
    {
        Assert.Equal(new[] { "ab", "abcd", "efgh" }, TextWidth.Wrap("ab abcdefgh", 4));
    }

    [Fact]
    public void Wide_characters_count_as_two_columns()
    {
        Assert.Equal(new[] { "日本", "語テ", "キス", "ト" }, TextWidth.Wrap("日本語テキスト", 4));
        Assert.Equal(new[] { "日本", "語" }, TextWidth.Wrap("日本 語", 4));
    }

    [Fact]
    public void Text_of_exactly_the_width_is_one_line()
    {
        Assert.Equal(new[] { "abcd" }, TextWidth.Wrap("abcd", 4));
        Assert.Equal(new[] { "日本" }, TextWidth.Wrap("日本", 4));
    }

    [Fact]
    public void Empty_text_is_one_empty_line()
    {
        Assert.Equal(new[] { string.Empty }, TextWidth.Wrap(string.Empty, 5));
    }

    [Fact]
    public void A_width_that_is_not_positive_gives_no_lines()
    {
        Assert.Empty(TextWidth.Wrap("abc", 0));
        Assert.Empty(TextWidth.Wrap("abc", -3));
    }

    [Fact]
    public void Control_characters_become_spaces()
    {
        Assert.Equal(new[] { "a b" }, TextWidth.Wrap("a\tb", 10));
    }

    [Theory]
    [InlineData("The quick brown fox jumps over the lazy dog", 1)]
    [InlineData("The quick brown fox jumps over the lazy dog", 7)]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz", 10)]
    [InlineData("a  b   c    d", 3)]
    public void Every_line_fits_the_width_and_no_text_is_lost(string text, int width)
    {
        IReadOnlyList<string> lines = TextWidth.Wrap(text, width);

        Assert.All(lines, line => Assert.True(TextWidth.Columns(line) <= width, $"'{line}' is wider than {width}"));
        Assert.Equal(
            text.Replace(" ", string.Empty),
            string.Concat(lines).Replace(" ", string.Empty));
    }

    [Fact]
    public void Truncate_keeps_a_combining_mark_with_its_base()
    {
        // "e" with U+0301 is one column; the cut lands after "x" and the ellipsis takes the last column.
        Assert.Equal("éx…", TextWidth.Truncate("éxyz", 3));
        Assert.Equal("éxyz", TextWidth.Truncate("éxyz", 4));
    }

    [Fact]
    public void Columns_counts_each_grapheme_once()
    {
        Assert.Equal(4, TextWidth.Columns("éx日"));
    }

    [Fact]
    public void Wrap_keeps_a_combining_mark_with_its_base_at_a_break()
    {
        Assert.Equal(new[] { "ab", "éc" }, TextWidth.Wrap("ab éc", 2));
    }
}
