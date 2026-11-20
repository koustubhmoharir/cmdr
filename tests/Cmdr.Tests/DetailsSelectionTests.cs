using Cmdr.Tui;
using Shared.Tui;

namespace Cmdr.Tests;

public sealed class DetailsSelectionTests
{
    private static DetailsLine Plain(string text) => new(new[] { new DetailsSpan(text, DetailsStyle.Normal) });

    private static IReadOnlyList<DetailsLine> Lines(params string[] texts) => texts.Select(Plain).ToList();

    private static LogicalPoint P(int line, int offset) => new(line, offset);

    private static string Copy(IReadOnlyList<DetailsLine> lines, LogicalPoint anchor, LogicalPoint active)
    {
        IReadOnlyList<string> texts = DetailsText.LogicalTexts(lines);
        return TextSelection.Extract(texts, LogicalRange.Between(anchor, active));
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
    public void A_long_path_copies_intact_when_it_spans_many_rows()
    {
        const string path = "/home/user/projects/some-very-long-directory-name/with words in it/out";
        IReadOnlyList<DetailsLine> lines = Lines(path);
        IReadOnlyList<DetailsRow> rows = DetailsText.WrapRows(lines, 12);
        Assert.True(rows.Count > 3);

        Assert.Equal(path, Copy(lines, P(0, 0), P(0, path.Length)));
    }

    [Fact]
    public void Hit_test_on_a_wrapped_row_gives_the_logical_offset_of_the_column()
    {
        IReadOnlyList<DetailsLine> lines = Lines("abc def");
        IReadOnlyList<DetailsRow> rows = DetailsText.WrapRows(lines, 3);
        IReadOnlyList<string> texts = DetailsText.LogicalTexts(lines);

        Assert.Equal(P(0, 1), TextSelection.PointAt(DetailsText.SelectionRows(rows), texts, 0, 1));
        Assert.Equal(P(0, 2), TextSelection.PointAt(DetailsText.SelectionRows(rows), texts, 0, 2)); // left cell of "c": before it
        Assert.Equal(P(0, 3), TextSelection.PointAt(DetailsText.SelectionRows(rows), texts, 0, 3)); // past the row: its end
        Assert.Equal(P(0, 4), TextSelection.PointAt(DetailsText.SelectionRows(rows), texts, 1, 0));
        Assert.Equal(P(0, 5), TextSelection.PointAt(DetailsText.SelectionRows(rows), texts, 1, 1));
    }

    [Fact]
    public void Hit_test_past_the_end_of_a_row_gives_the_end_of_the_row()
    {
        IReadOnlyList<DetailsLine> lines = Lines("abc def");
        IReadOnlyList<DetailsRow> rows = DetailsText.WrapRows(lines, 3);
        IReadOnlyList<string> texts = DetailsText.LogicalTexts(lines);

        Assert.Equal(P(0, 3), TextSelection.PointAt(DetailsText.SelectionRows(rows), texts, 0, 40));
        Assert.Equal(P(0, 7), TextSelection.PointAt(DetailsText.SelectionRows(rows), texts, 1, 40));
    }

    [Fact]
    public void Hit_test_clamps_the_row_index_and_handles_no_rows()
    {
        IReadOnlyList<DetailsLine> lines = Lines("abc");
        IReadOnlyList<DetailsRow> rows = DetailsText.WrapRows(lines, 10);
        IReadOnlyList<string> texts = DetailsText.LogicalTexts(lines);

        Assert.Equal(P(0, 2), TextSelection.PointAt(DetailsText.SelectionRows(rows), texts, 5, 2));
        Assert.Equal(default, TextSelection.PointAt(Array.Empty<SelectionRow>(), texts, 0, 0));
    }

    [Fact]
    public void Hit_test_on_a_wide_character_counts_its_two_columns()
    {
        IReadOnlyList<DetailsLine> lines = Lines("日本");
        IReadOnlyList<DetailsRow> rows = DetailsText.WrapRows(lines, 4);
        IReadOnlyList<string> texts = DetailsText.LogicalTexts(lines);

        Assert.Equal(P(0, 0), TextSelection.PointAt(DetailsText.SelectionRows(rows), texts, 0, 0));
        Assert.Equal(P(0, 1), TextSelection.PointAt(DetailsText.SelectionRows(rows), texts, 0, 1)); // right half of 日: after it
        Assert.Equal(P(0, 1), TextSelection.PointAt(DetailsText.SelectionRows(rows), texts, 0, 2));
        Assert.Equal(P(0, 2), TextSelection.PointAt(DetailsText.SelectionRows(rows), texts, 0, 3));
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
        IReadOnlyList<string> texts = DetailsText.LogicalTexts(lines);

        // Column 0 is the "e" with its mark: the caret goes before it or after the whole grapheme, never between them.
        Assert.Equal(P(0, 0), TextSelection.PointAt(DetailsText.SelectionRows(rows), texts, 0, 0));
        Assert.Equal(P(0, 2), TextSelection.PointAt(DetailsText.SelectionRows(rows), texts, 0, 1));
        Assert.Equal(P(0, 3), TextSelection.PointAt(DetailsText.SelectionRows(rows), texts, 0, 2));
    }
}
