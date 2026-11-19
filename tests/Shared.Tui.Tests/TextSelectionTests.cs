using Shared.Tui;

namespace Shared.Tui.Tests;

public sealed class TextSelectionTests
{
    private static LogicalPoint P(int line, int offset) => new(line, offset);

    private static string Copy(IReadOnlyList<string> texts, LogicalPoint anchor, LogicalPoint active) =>
        TextSelection.Extract(texts, LogicalRange.Between(anchor, active));

    [Fact]
    public void Selection_across_a_wrap_break_copies_the_dropped_space_back()
    {
        string[] texts = { "abc def" };

        // "c" on the first row and "d" on the second: the space that the wrap dropped is between them.
        Assert.Equal("c d", Copy(texts, P(0, 2), P(0, 5)));
    }

    [Fact]
    public void A_long_path_copies_intact_when_it_spans_many_rows()
    {
        const string path = "/home/user/projects/some-very-long-directory-name/with words in it/out";
        string[] texts = { path };

        Assert.Equal(path, Copy(texts, P(0, 0), P(0, path.Length)));
    }

    [Fact]
    public void Selection_within_one_row_copies_those_characters()
    {
        Assert.Equal("ello", Copy(new[] { "hello world" }, P(0, 1), P(0, 5)));
    }

    [Fact]
    public void Selection_across_rows_of_one_logical_line_is_joined_without_a_newline()
    {
        string[] texts = { "abcdefghij" };

        Assert.Equal("cdefgh", Copy(texts, P(0, 2), P(0, 8)));
    }

    [Fact]
    public void Selection_across_logical_lines_is_joined_with_newlines()
    {
        string[] texts = { "first", "second", "third" };

        Assert.Equal("st\nsecond\nth", Copy(texts, P(0, 3), P(2, 2)));
    }

    [Fact]
    public void Selection_ending_at_the_start_of_the_next_line_takes_the_newline_only()
    {
        string[] texts = { "first", "second" };

        Assert.Equal("first\n", Copy(texts, P(0, 0), P(1, 0)));
    }

    [Fact]
    public void Reversed_anchor_and_active_copy_the_same_text()
    {
        string[] texts = { "abc def", "ghi" };

        Assert.Equal(Copy(texts, P(0, 1), P(1, 2)), Copy(texts, P(1, 2), P(0, 1)));
        Assert.Equal("bc def\ngh", Copy(texts, P(1, 2), P(0, 1)));
    }

    [Fact]
    public void Clamp_pulls_points_back_when_the_text_got_shorter()
    {
        string[] texts = { "abc" };

        LogicalRange range = TextSelection.Clamp(P(3, 30), P(0, 1), texts);

        Assert.Equal(new LogicalRange(P(0, 1), P(0, 3)), range);
        Assert.Equal("bc", TextSelection.Extract(texts, range));
    }

    [Fact]
    public void Clamp_gives_the_origin_when_there_is_no_text()
    {
        Assert.Equal(new LogicalRange(P(0, 0), P(0, 0)), TextSelection.Clamp(P(2, 5), P(1, 1), Array.Empty<string>()));
        Assert.True(TextSelection.Clamp(P(2, 5), P(1, 1), Array.Empty<string>()).IsEmpty);
    }

    [Fact]
    public void Extract_gives_empty_text_when_there_is_no_text()
    {
        Assert.Equal("", TextSelection.Extract(Array.Empty<string>(), new LogicalRange(P(1, 1), P(2, 5))));
    }

    [Fact]
    public void Covers_is_half_open_on_the_logical_range()
    {
        var range = new LogicalRange(P(0, 2), P(1, 1));

        Assert.False(range.Covers(0, 1));
        Assert.True(range.Covers(0, 2));
        Assert.True(range.Covers(0, 9));
        Assert.True(range.Covers(1, 0));
        Assert.False(range.Covers(1, 1));
    }

    [Fact]
    public void Hit_test_on_a_wrapped_row_gives_the_logical_offset_of_the_column()
    {
        string[] texts = { "abc def" };
        SelectionRow[] rows = { new(0, 0, 3), new(0, 4, 7) };

        Assert.Equal(P(0, 1), TextSelection.PointAt(rows, texts, 0, 1));
        Assert.Equal(P(0, 2), TextSelection.PointAt(rows, texts, 0, 2)); // left cell of "c": before it
        Assert.Equal(P(0, 3), TextSelection.PointAt(rows, texts, 0, 3)); // past the row: its end
        Assert.Equal(P(0, 4), TextSelection.PointAt(rows, texts, 1, 0));
        Assert.Equal(P(0, 5), TextSelection.PointAt(rows, texts, 1, 1));
    }

    [Fact]
    public void Hit_test_past_the_end_of_a_row_gives_the_end_of_the_row()
    {
        string[] texts = { "abc def" };
        SelectionRow[] rows = { new(0, 0, 3), new(0, 4, 7) };

        Assert.Equal(P(0, 3), TextSelection.PointAt(rows, texts, 0, 40));
        Assert.Equal(P(0, 7), TextSelection.PointAt(rows, texts, 1, 40));
    }

    [Fact]
    public void Hit_test_clamps_the_row_index_and_handles_no_rows()
    {
        string[] texts = { "abc" };
        SelectionRow[] rows = { new(0, 0, 3) };

        Assert.Equal(P(0, 2), TextSelection.PointAt(rows, texts, 5, 2));
        Assert.Equal(default, TextSelection.PointAt(Array.Empty<SelectionRow>(), texts, 0, 0));
    }

    [Fact]
    public void Hit_test_on_a_wide_character_counts_its_two_columns()
    {
        string[] texts = { "日本" };
        SelectionRow[] rows = { new(0, 0, 2) };

        Assert.Equal(P(0, 0), TextSelection.PointAt(rows, texts, 0, 0));
        Assert.Equal(P(0, 1), TextSelection.PointAt(rows, texts, 0, 1)); // right half of 日: after it
        Assert.Equal(P(0, 1), TextSelection.PointAt(rows, texts, 0, 2));
        Assert.Equal(P(0, 2), TextSelection.PointAt(rows, texts, 0, 3));
    }

    [Fact]
    public void Control_characters_are_cleaned_in_the_logical_text_and_the_copy()
    {
        // The caller cleans the text; the copy then has the cleaned characters.
        string[] texts = { TextWidth.Clean("a\tb") };

        Assert.Equal("a b", Copy(texts, P(0, 0), P(0, 3)));
    }

    [Fact]
    public void Points_on_a_combining_mark_land_on_grapheme_boundaries()
    {
        string[] texts = { "éx" };
        SelectionRow[] rows = { new(0, 0, 3) };

        // Column 0 is the "e" with its mark: the caret goes before it or after the whole grapheme, never between them.
        Assert.Equal(P(0, 0), TextSelection.PointAt(rows, texts, 0, 0));
        Assert.Equal(P(0, 2), TextSelection.PointAt(rows, texts, 0, 1));
        Assert.Equal(P(0, 3), TextSelection.PointAt(rows, texts, 0, 2));
    }

    [Fact]
    public void Hit_test_measures_the_column_from_the_row_column()
    {
        string[] texts = { "abc def" };
        SelectionRow[] rows = { new(0, 0, 3, Column: 10), new(0, 4, 7, Column: 10) };

        Assert.Equal(P(0, 0), TextSelection.PointAt(rows, texts, 0, 10)); // the first cell of the row
        Assert.Equal(P(0, 1), TextSelection.PointAt(rows, texts, 0, 11));
        Assert.Equal(P(0, 2), TextSelection.PointAt(rows, texts, 0, 12));
        Assert.Equal(P(0, 3), TextSelection.PointAt(rows, texts, 0, 13)); // past the row: its end
        Assert.Equal(P(0, 4), TextSelection.PointAt(rows, texts, 1, 10));
        Assert.Equal(P(0, 6), TextSelection.PointAt(rows, texts, 1, 12));
    }

    [Fact]
    public void A_column_left_of_the_row_column_gives_the_start_of_the_row()
    {
        string[] texts = { "abc def" };
        SelectionRow[] rows = { new(0, 0, 3, Column: 10), new(0, 4, 7, Column: 10) };

        Assert.Equal(P(0, 0), TextSelection.PointAt(rows, texts, 0, 9));
        Assert.Equal(P(0, 0), TextSelection.PointAt(rows, texts, 0, 0));
        Assert.Equal(P(0, 4), TextSelection.PointAt(rows, texts, 1, 3));
    }

    [Fact]
    public void Hit_test_with_a_row_column_on_a_wide_character_counts_its_two_columns()
    {
        string[] texts = { "日本" };
        SelectionRow[] rows = { new(0, 0, 2, Column: 5) };

        Assert.Equal(P(0, 0), TextSelection.PointAt(rows, texts, 0, 5));
        Assert.Equal(P(0, 1), TextSelection.PointAt(rows, texts, 0, 6)); // right half of 日: after it
        Assert.Equal(P(0, 1), TextSelection.PointAt(rows, texts, 0, 7));
        Assert.Equal(P(0, 2), TextSelection.PointAt(rows, texts, 0, 8));
    }

    [Fact]
    public void CharacterCount_counts_a_surrogate_pair_once()
    {
        const string text = "a😀b";

        Assert.Equal(4, text.Length); // UTF-16 units: a, two for the pair, b
        Assert.Equal(3, TextSelection.CharacterCount(text));
        Assert.Equal(2, TextSelection.CharacterCount("😀😀"));
    }

    [Fact]
    public void CharacterCount_of_plain_and_empty_text()
    {
        Assert.Equal(0, TextSelection.CharacterCount(string.Empty));
        Assert.Equal(3, TextSelection.CharacterCount("abc"));
    }

    [Fact]
    public void CharacterCount_counts_a_lone_surrogate_as_one_character()
    {
        // A lone surrogate is one replacement character, as in Rune enumeration.
        Assert.Equal(2, TextSelection.CharacterCount("\uD83Da"));
    }
}
