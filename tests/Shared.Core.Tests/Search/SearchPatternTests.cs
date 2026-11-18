using Shared.Core;

namespace Shared.Core.Tests.Search;

public sealed class SearchPatternTests
{
    private static SearchPattern Compile(string text, bool caseSensitive = false, bool wholeWord = false, bool regex = false)
    {
        SearchPattern? pattern = SearchPattern.Create(new SearchQuery(text, caseSensitive, wholeWord, regex), out string? error);
        Assert.Null(error);
        return Assert.IsType<SearchPattern>(pattern);
    }

    [Fact]
    public void Empty_text_is_no_search()
    {
        SearchPattern? pattern = SearchPattern.Create(new SearchQuery(string.Empty, false, false, true), out string? error);

        Assert.Null(pattern);
        Assert.Null(error);
    }

    [Fact]
    public void Null_text_argument_throws()
    {
        SearchPattern pattern = Compile("a");

        Assert.Throws<ArgumentNullException>(() => pattern.Matches(null!));
        Assert.Throws<ArgumentNullException>(() => pattern.Count(null!));
    }

    [Fact]
    public void Literal_text_does_not_treat_dot_as_a_wildcard()
    {
        SearchPattern pattern = Compile("a.b");

        Assert.Equal(0, pattern.Count("axb"));
        Assert.Equal(new[] { new SearchMatch(0, 3) }, pattern.Matches("a.b"));
    }

    [Fact]
    public void Literal_open_parenthesis_matches_itself()
    {
        SearchPattern pattern = Compile("(");

        Assert.Equal(new[] { new SearchMatch(1, 1) }, pattern.Matches("f(x)"));
    }

    [Fact]
    public void Matching_ignores_case_by_default()
    {
        SearchPattern pattern = Compile("FOO");

        Assert.Equal(new[] { new SearchMatch(0, 3), new SearchMatch(4, 3) }, pattern.Matches("foo Foo"));
    }

    [Fact]
    public void Case_sensitive_matching_respects_case()
    {
        SearchPattern pattern = Compile("Foo", caseSensitive: true);

        Assert.Equal(new[] { new SearchMatch(4, 3) }, pattern.Matches("foo Foo"));
    }

    [Fact]
    public void Whole_word_matches_only_complete_words()
    {
        SearchPattern pattern = Compile("foo", wholeWord: true);

        // Matches at 0 ("foo") and 25 ("foo" inside the parentheses); not in foobar, barfoo, or foo_x.
        Assert.Equal(new[] { new SearchMatch(0, 3), new SearchMatch(25, 3) }, pattern.Matches("foo foobar barfoo foo_x (foo)"));
    }

    [Fact]
    public void Whole_word_works_with_leading_and_trailing_punctuation()
    {
        SearchPattern pattern = Compile("-v", wholeWord: true);

        Assert.Equal(new[] { new SearchMatch(4, 2) }, pattern.Matches("run -v x"));
        Assert.Equal(0, pattern.Count("run -vv"));
    }

    [Fact]
    public void Regex_matches_and_reports_offsets()
    {
        SearchPattern pattern = Compile(@"\d+", regex: true);

        Assert.Equal(new[] { new SearchMatch(3, 2), new SearchMatch(8, 3) }, pattern.Matches("ab 12 c 345"));
    }

    [Theory]
    [InlineData("(")]
    [InlineData("a\\")]
    [InlineData("[z-a]")]
    public void Invalid_regex_gives_null_and_an_error(string text)
    {
        SearchPattern? pattern = SearchPattern.Create(new SearchQuery(text, false, false, true), out string? error);

        Assert.Null(pattern);
        Assert.Equal("invalid pattern", error);
    }

    [Fact]
    public void Invalid_regex_stays_invalid_with_whole_word()
    {
        SearchPattern? pattern = SearchPattern.Create(new SearchQuery("a)(b", false, true, true), out string? error);

        Assert.Null(pattern);
        Assert.Equal("invalid pattern", error);
    }

    [Fact]
    public void Regex_with_whole_word_matches_only_complete_words()
    {
        SearchPattern pattern = Compile(@"\d+", wholeWord: true, regex: true);

        // "345x" is not a whole number, so only "12" and the final "6" match.
        Assert.Equal(new[] { new SearchMatch(3, 2), new SearchMatch(13, 1) }, pattern.Matches("ab 12 c 345x 6"));
    }

    [Fact]
    public void Zero_length_matches_are_skipped()
    {
        SearchPattern pattern = Compile("a*", regex: true);

        Assert.Equal(new[] { new SearchMatch(1, 2) }, pattern.Matches("baab"));
        Assert.Equal(0, Compile("^", regex: true).Count("abc"));
    }

    [Fact]
    public void Multiple_matches_are_non_overlapping_and_left_to_right()
    {
        SearchPattern pattern = Compile("aa");

        Assert.Equal(new[] { new SearchMatch(0, 2), new SearchMatch(2, 2) }, pattern.Matches("aaaaa"));
        Assert.Equal(new[] { new SearchMatch(0, 2), new SearchMatch(2, 2), new SearchMatch(4, 2) }, Compile("ab").Matches("ababab"));
    }

    [Fact]
    public void Count_equals_the_number_of_matches()
    {
        SearchPattern pattern = Compile("o");

        Assert.Equal(4, pattern.Count("foo boo"));
        Assert.Equal(pattern.Matches("foo boo").Count, pattern.Count("foo boo"));
    }

    [Fact]
    public void Offsets_are_utf16_units_and_case_folding_covers_non_ascii()
    {
        SearchPattern pattern = Compile("é");

        // "Café CAFÉ café": the three é letters are at offsets 3, 8 and 13.
        Assert.Equal(
            new[] { new SearchMatch(3, 1), new SearchMatch(8, 1), new SearchMatch(13, 1) },
            pattern.Matches("Café CAFÉ café"));
    }

    [Fact]
    public void Surrogate_pairs_count_as_two_units()
    {
        SearchPattern pattern = Compile("x");

        Assert.Equal(new[] { new SearchMatch(2, 1) }, pattern.Matches("\U0001F600x"));
    }

    [Fact]
    public void Backreference_regex_runs_on_the_fallback_engine()
    {
        SearchPattern pattern = Compile(@"(a)\1", regex: true);

        Assert.Equal(new[] { new SearchMatch(1, 2) }, pattern.Matches("xaay"));
        Assert.False(pattern.TimedOut);
    }

    [Fact]
    public void Lookbehind_regex_is_valid_and_not_reported_as_invalid()
    {
        SearchPattern? pattern = SearchPattern.Create(new SearchQuery("(?<=x)a", false, false, true), out string? error);

        Assert.Null(error);
        Assert.NotNull(pattern);
        Assert.Equal(new[] { new SearchMatch(1, 1) }, pattern!.Matches("xa a"));
    }

    [Fact]
    public void Literal_with_whole_word_matches_on_long_text()
    {
        SearchPattern pattern = Compile("ab", wholeWord: true);
        string text = string.Concat(Enumerable.Repeat("ab ", 20000));

        Assert.Equal(20000, pattern.Count(text));
        Assert.False(pattern.TimedOut);
    }

    [Fact]
    public void Normal_patterns_are_not_timed_out()
    {
        SearchPattern pattern = Compile(@"\d+", regex: true);

        Assert.Equal(2, pattern.Count("1 22"));
        Assert.False(pattern.TimedOut);
    }

    [Fact]
    public void Timeout_on_the_fallback_engine_abandons_the_pattern()
    {
        // The backreference forces the backtracking engine, where the nested quantifier takes exponential time.
        SearchPattern pattern = Compile(@"(a+)+\1b", regex: true);
        string text = new string('a', 30);

        Assert.Equal(0, pattern.Count(text));
        Assert.True(pattern.TimedOut);
        Assert.Empty(pattern.Matches("ab ab"));
        Assert.Equal(0, pattern.Count("ab ab"));
    }
}
