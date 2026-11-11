using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class SelectionTextTests
{
    private static SelectionPoint P(long line, int column) => new(line, column);

    private static string Extract(string[] lines, long firstLine, SelectionPoint anchor, SelectionPoint active, bool lineMode = false) =>
        SelectionText.Extract(lines, firstLine, SelectionRange.Between(anchor, active, lineMode));

    [Fact]
    public void Single_line_partial_selection_takes_the_columns_between_the_points()
    {
        string[] lines = { "hello world" };

        Assert.Equal("ello", Extract(lines, 0, P(0, 1), P(0, 4)));
    }

    [Fact]
    public void Multi_line_character_selection_takes_the_tail_middle_and_head()
    {
        string[] lines = { "abcd", "efgh", "ijkl" };

        Assert.Equal("cd\nefgh\ni", Extract(lines, 0, P(0, 2), P(2, 0)));
    }

    [Fact]
    public void Reversed_anchor_and_active_give_the_same_text()
    {
        string[] lines = { "abcd", "efgh", "ijkl" };

        Assert.Equal(Extract(lines, 0, P(0, 2), P(2, 0)), Extract(lines, 0, P(2, 0), P(0, 2)));
    }

    [Fact]
    public void Line_mode_takes_whole_lines_regardless_of_columns()
    {
        string[] lines = { "abcd", "efgh", "ijkl" };

        Assert.Equal("efgh\nijkl", Extract(lines, 0, P(1, 3), P(2, 0), lineMode: true));
    }

    [Fact]
    public void Wide_character_is_included_whole_when_the_start_falls_in_its_second_column()
    {
        string[] lines = { "a中b" }; // 中 occupies columns 1 and 2

        Assert.Equal("中b", Extract(lines, 0, P(0, 2), P(0, 3)));
    }

    [Fact]
    public void Wide_character_is_included_whole_when_the_end_falls_in_its_first_column()
    {
        string[] lines = { "a中b" };

        Assert.Equal("a中", Extract(lines, 0, P(0, 0), P(0, 1)));
    }

    [Fact]
    public void Wide_character_is_excluded_when_the_selection_ends_before_it()
    {
        string[] lines = { "a中b" };

        Assert.Equal("a", Extract(lines, 0, P(0, 0), P(0, 0)));
    }

    [Fact]
    public void Empty_line_list_gives_an_empty_string()
    {
        Assert.Equal(string.Empty, Extract(Array.Empty<string>(), 0, P(0, 0), P(5, 5)));
    }

    [Fact]
    public void Columns_beyond_the_line_end_are_clamped()
    {
        string[] lines = { "abc", "defgh" };

        Assert.Equal("bc\ndefgh", Extract(lines, 0, P(0, 1), P(1, 99)));
        Assert.Equal("abc\nd", Extract(lines, 0, P(0, 0), P(1, 0)));
        Assert.Equal("", Extract(new[] { "abc" }, 0, P(0, 50), P(0, 60)));
    }

    [Fact]
    public void Lines_before_the_retained_range_are_absent()
    {
        string[] retained = { "line 5", "line 6" };

        // The selection starts at evicted line 3; the text starts at the first retained line, from column 0.
        Assert.Equal("line 5\nline", Extract(retained, 5, P(3, 2), P(6, 3)));
    }

    [Fact]
    public void Selection_ending_beyond_the_retained_lines_stops_at_the_last_one()
    {
        string[] retained = { "a", "b" };

        Assert.Equal("a\nb", Extract(retained, 10, P(10, 0), P(40, 9), lineMode: true));
    }

    [Fact]
    public void Selection_entirely_outside_the_retained_lines_is_empty()
    {
        Assert.Equal(string.Empty, Extract(new[] { "a" }, 10, P(0, 0), P(3, 0)));
    }

    [Fact]
    public void Combining_mark_stays_with_its_base_character()
    {
        string[] lines = { "e\u0301x" }; // e + combining acute, then x

        Assert.Equal("e\u0301", Extract(lines, 0, P(0, 0), P(0, 0)));
    }

    [Fact]
    public void Covers_marks_cells_of_a_character_selection_and_whole_lines_in_line_mode()
    {
        SelectionRange chars = SelectionRange.Between(P(1, 2), P(2, 1), lineMode: false);
        SelectionRange lines = SelectionRange.Between(P(1, 2), P(2, 1), lineMode: true);

        Assert.False(chars.Covers(1, 0, 1));
        Assert.True(chars.Covers(1, 2, 1));
        Assert.True(chars.Covers(1, 0, 3));
        Assert.True(chars.Covers(2, 1, 1));
        Assert.False(chars.Covers(2, 2, 1));
        Assert.False(chars.Covers(3, 0, 1));
        Assert.True(lines.Covers(2, 50, 1));
        Assert.False(lines.Covers(0, 0, 1));
    }
}
