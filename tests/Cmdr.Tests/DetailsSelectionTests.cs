using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class DetailsSelectionTests
{
    private static DetailsLine Plain(string text) => new(new[] { new DetailsSpan(text, DetailsStyle.Normal) });

    private static IReadOnlyList<DetailsLine> Lines(params string[] texts) => texts.Select(Plain).ToList();

    private static LogicalPoint P(int line, int offset) => new(line, offset);

    private static string Copy(IReadOnlyList<DetailsLine> lines, LogicalPoint anchor, LogicalPoint active)
    {
        IReadOnlyList<string> texts = DetailsSelection.Texts(lines);
        return DetailsSelection.Extract(texts, LogicalRange.Between(anchor, active));
    }

    [Fact]
    public void Rows_map_back_to_the_offsets_of_their_logical_line()
    {
        IReadOnlyList<DetailsRow> rows = DetailsText.WrapRows(Lines("abc def", "g"), 3);

        Assert.Equal(new[] { 0, 0, 1 }, rows.Select(row => row.LogicalLine).ToArray());
        Assert.Equal(new[] { (0, 3), (4, 7), (0, 1) }, rows.Select(row => (row.Start, row.End)).ToArray());
        Assert.Equal(new[] { "abc", "def", "g" }, rows.Select(row => row.Line.Text).ToArray());
    }

    [Fact]
    public void Each_row_text_is_the_range_of_its_logical_text()
    {
        string[] texts = { "ab   cd ef", "x" };
        IReadOnlyList<DetailsRow> rows = DetailsText.WrapRows(Lines(texts), 4);

        foreach (DetailsRow row in rows)
        {
            Assert.Equal(row.Line.Text, texts[row.LogicalLine][row.Start..row.End]);
        }
    }

    [Fact]
    public void Selection_across_a_wrap_break_copies_the_dropped_space_back()
    {
        IReadOnlyList<DetailsLine> lines = Lines("abc def");

        // "c" on the first row and "d" on the second: the space that the wrap dropped is between them.
        Assert.Equal("c d", Copy(lines, P(0, 2), P(0, 5)));
    }

    [Fact]
    public void A_long_path_copies_intact_when_it_spans_many_rows()
    {
        const string path = "/home/user/projects/some-very-long-directory-name/with words in it/out";
        IReadOnlyList<DetailsLine> lines = Lines(path);
        IReadOnlyList<DetailsRow> rows = DetailsText.WrapRows(lines, 12);
        Assert.True(rows.Count > 3);

        Assert.Equal(path, Copy(lines, P(0, 0), P(0, path.Length)));
    }

    [Fact]
    public void Selection_within_one_row_copies_those_characters()
    {
        Assert.Equal("ello", Copy(Lines("hello world"), P(0, 1), P(0, 5)));
    }

    [Fact]
    public void Selection_across_rows_of_one_logical_line_is_joined_without_a_newline()
    {
        IReadOnlyList<DetailsLine> lines = Lines("abcdefghij");

        Assert.Equal("cdefgh", Copy(lines, P(0, 2), P(0, 8)));
    }

    [Fact]
    public void Selection_across_logical_lines_is_joined_with_newlines()
    {
        IReadOnlyList<DetailsLine> lines = Lines("first", "second", "third");

        Assert.Equal("st\nsecond\nth", Copy(lines, P(0, 3), P(2, 2)));
    }

    [Fact]
    public void Selection_ending_at_the_start_of_the_next_line_takes_the_newline_only()
    {
        IReadOnlyList<DetailsLine> lines = Lines("first", "second");

        Assert.Equal("first\n", Copy(lines, P(0, 0), P(1, 0)));
    }

    [Fact]
    public void Reversed_anchor_and_active_copy_the_same_text()
    {
        IReadOnlyList<DetailsLine> lines = Lines("abc def", "ghi");

        Assert.Equal(Copy(lines, P(0, 1), P(1, 2)), Copy(lines, P(1, 2), P(0, 1)));
        Assert.Equal("bc def\ngh", Copy(lines, P(1, 2), P(0, 1)));
    }

    [Fact]
    public void Clamp_pulls_points_back_when_the_text_got_shorter()
    {
        string[] texts = { "abc" };

        LogicalRange range = DetailsSelection.Clamp(P(3, 30), P(0, 1), texts);

        Assert.Equal(new LogicalRange(P(0, 1), P(0, 3)), range);
        Assert.Equal("bc", DetailsSelection.Extract(texts, range));
    }

    [Fact]
    public void Clamp_gives_the_origin_when_there_is_no_text()
    {
        Assert.Equal(new LogicalRange(P(0, 0), P(0, 0)), DetailsSelection.Clamp(P(2, 5), P(1, 1), Array.Empty<string>()));
        Assert.True(DetailsSelection.Clamp(P(2, 5), P(1, 1), Array.Empty<string>()).IsEmpty);
    }

    [Fact]
    public void Extract_gives_empty_text_when_there_is_no_text()
    {
        Assert.Equal("", DetailsSelection.Extract(Array.Empty<string>(), new LogicalRange(P(1, 1), P(2, 5))));
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
        IReadOnlyList<DetailsLine> lines = Lines("abc def");
        IReadOnlyList<DetailsRow> rows = DetailsText.WrapRows(lines, 3);
        IReadOnlyList<string> texts = DetailsSelection.Texts(lines);

        Assert.Equal(P(0, 1), DetailsSelection.PointAt(rows, texts, 0, 1));
        Assert.Equal(P(0, 2), DetailsSelection.PointAt(rows, texts, 0, 2)); // left cell of "c": before it
        Assert.Equal(P(0, 3), DetailsSelection.PointAt(rows, texts, 0, 3)); // past the row: its end
        Assert.Equal(P(0, 4), DetailsSelection.PointAt(rows, texts, 1, 0));
        Assert.Equal(P(0, 5), DetailsSelection.PointAt(rows, texts, 1, 1));
    }

    [Fact]
    public void Hit_test_past_the_end_of_a_row_gives_the_end_of_the_row()
    {
        IReadOnlyList<DetailsLine> lines = Lines("abc def");
        IReadOnlyList<DetailsRow> rows = DetailsText.WrapRows(lines, 3);
        IReadOnlyList<string> texts = DetailsSelection.Texts(lines);

        Assert.Equal(P(0, 3), DetailsSelection.PointAt(rows, texts, 0, 40));
        Assert.Equal(P(0, 7), DetailsSelection.PointAt(rows, texts, 1, 40));
    }

    [Fact]
    public void Hit_test_clamps_the_row_index_and_handles_no_rows()
    {
        IReadOnlyList<DetailsLine> lines = Lines("abc");
        IReadOnlyList<DetailsRow> rows = DetailsText.WrapRows(lines, 10);
        IReadOnlyList<string> texts = DetailsSelection.Texts(lines);

        Assert.Equal(P(0, 2), DetailsSelection.PointAt(rows, texts, 5, 2));
        Assert.Equal(default, DetailsSelection.PointAt(Array.Empty<DetailsRow>(), texts, 0, 0));
    }

    [Fact]
    public void Hit_test_on_a_wide_character_counts_its_two_columns()
    {
        IReadOnlyList<DetailsLine> lines = Lines("日本");
        IReadOnlyList<DetailsRow> rows = DetailsText.WrapRows(lines, 4);
        IReadOnlyList<string> texts = DetailsSelection.Texts(lines);

        Assert.Equal(P(0, 0), DetailsSelection.PointAt(rows, texts, 0, 0));
        Assert.Equal(P(0, 1), DetailsSelection.PointAt(rows, texts, 0, 1)); // right half of 日: after it
        Assert.Equal(P(0, 1), DetailsSelection.PointAt(rows, texts, 0, 2));
        Assert.Equal(P(0, 2), DetailsSelection.PointAt(rows, texts, 0, 3));
    }

    [Fact]
    public void Control_characters_are_cleaned_in_the_logical_text_and_the_copy()
    {
        IReadOnlyList<DetailsLine> lines = Lines("a\tb");

        Assert.Equal("a b", Copy(lines, P(0, 0), P(0, 3)));
    }

    [Fact]
    public void Wrap_and_rows_agree_on_the_plain_text()
    {
        IReadOnlyList<DetailsLine> lines = Lines("one two three four", "  five");

        Assert.Equal(
            DetailsText.Plain(DetailsText.Wrap(lines, 6)),
            DetailsText.WrapRows(lines, 6).Select(row => row.Line.Text).ToList());
    }


    [Fact]
    public void Points_on_a_combining_mark_land_on_grapheme_boundaries()
    {
        IReadOnlyList<DetailsLine> lines = Lines("éx");
        IReadOnlyList<DetailsRow> rows = DetailsText.WrapRows(lines, 10);
        IReadOnlyList<string> texts = DetailsSelection.Texts(lines);

        // Column 0 is the "e" with its mark: the caret goes before it or after the whole grapheme, never between them.
        Assert.Equal(P(0, 0), DetailsSelection.PointAt(rows, texts, 0, 0));
        Assert.Equal(P(0, 2), DetailsSelection.PointAt(rows, texts, 0, 1));
        Assert.Equal(P(0, 3), DetailsSelection.PointAt(rows, texts, 0, 2));
    }
}
