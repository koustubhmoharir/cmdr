using Cmdr.Core.Output;
using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class OutputWrapTests
{
    private static OutputLine Line(params string[] texts) =>
        new(OutputSource.StdOut, texts.Select(text => new StyledSpan(text, SpanStyle.Default)).ToList());

    [Fact]
    public void Empty_line_is_one_empty_row()
    {
        Assert.Equal(new[] { (0, 0) }, OutputWrap.Rows(Line(), 5));
        Assert.Equal(new[] { (0, 0) }, OutputWrap.Rows(Line(""), 5));
        Assert.Equal(1, OutputWrap.RowCount(Line(""), 5));
    }

    [Fact]
    public void Line_shorter_than_the_width_is_one_row()
    {
        Assert.Equal(new[] { (0, 3) }, OutputWrap.Rows(Line("abc"), 5));
    }

    [Fact]
    public void Line_of_exactly_the_width_is_one_row()
    {
        Assert.Equal(new[] { (0, 4) }, OutputWrap.Rows(Line("abcd"), 4));
        Assert.Equal(1, OutputWrap.RowCount(Line("abcd"), 4));
    }

    [Fact]
    public void One_column_more_than_the_width_is_two_rows()
    {
        Assert.Equal(new[] { (0, 4), (4, 5) }, OutputWrap.Rows(Line("abcde"), 4));
    }

    [Fact]
    public void Breaks_are_by_cell_not_by_word_and_spaces_are_kept()
    {
        Assert.Equal(new[] { (0, 4), (4, 8), (8, 12), (12, 13) }, OutputWrap.Rows(Line("ab cd efg hij"), 4));
    }

    [Fact]
    public void Wide_character_that_does_not_fit_starts_the_next_row_and_the_rest_of_the_row_is_blank()
    {
        // "a" takes column 0; the wide character needs columns 1-2, which is past the width of 2.
        Assert.Equal(new[] { (0, 1), (1, 3), (3, 5) }, OutputWrap.Rows(Line("a日本"), 2));
    }

    [Fact]
    public void Wide_characters_that_fit_exactly_stay_in_one_row()
    {
        Assert.Equal(new[] { (0, 4) }, OutputWrap.Rows(Line("日本"), 4));
        Assert.Equal(new[] { (0, 4), (4, 6) }, OutputWrap.Rows(Line("日本語"), 4));
    }

    [Fact]
    public void Wide_character_wider_than_the_width_gets_a_row_of_its_own()
    {
        Assert.Equal(new[] { (0, 2), (2, 4) }, OutputWrap.Rows(Line("日本"), 1));
    }

    [Fact]
    public void Control_character_counts_as_one_cell()
    {
        Assert.Equal(new[] { (0, 3) }, OutputWrap.Rows(Line("a\tb"), 3));
        Assert.Equal(new[] { (0, 2), (2, 3) }, OutputWrap.Rows(Line("a\tb"), 2));
    }

    [Fact]
    public void Combining_mark_stays_with_its_base_in_one_cell()
    {
        // "e" followed by U+0301 (combining acute accent) is one grapheme, so it is one cell.
        List<OutputWrap.Cell> cells = OutputWrap.Cells(Line("éx"));

        Assert.Equal(new[] { "é", "x" }, cells.Select(cell => cell.Text));
        Assert.Equal(new[] { 0, 1 }, cells.Select(cell => cell.Column));
        Assert.Equal(new[] { 1, 1 }, cells.Select(cell => cell.Width));
        Assert.Equal(new[] { (0, 2) }, OutputWrap.Rows(Line("éx"), 2));
    }

    [Fact]
    public void Multiple_combining_marks_stay_in_the_base_cell()
    {
        List<OutputWrap.Cell> cells = OutputWrap.Cells(Line("á̂b"));

        Assert.Equal(new[] { "á̂", "b" }, cells.Select(cell => cell.Text));
        Assert.Equal(new[] { 0, 1 }, cells.Select(cell => cell.Column));
    }

    [Fact]
    public void Combining_mark_at_a_wrap_boundary_stays_with_its_base_on_the_same_row()
    {
        // "abc" fills the row of width 3; the base "d" with its mark starts the next row whole.
        OutputLine line = Line("abcd́");

        Assert.Equal(new[] { (0, 3), (3, 4) }, OutputWrap.Rows(line, 3));
        Assert.Equal("d́", OutputWrap.Cells(line)[3].Text);
        Assert.Equal(2, OutputWrap.RowCount(line, 3));
    }

    [Fact]
    public void Combining_mark_in_the_next_span_joins_its_base_cell_and_takes_the_base_span()
    {
        var line = new OutputLine(
            OutputSource.StdOut,
            new[]
            {
                new StyledSpan("e", SpanStyle.Default),
                new StyledSpan("́x", new SpanStyle(AnsiColor.Default, AnsiColor.Default, TextAttrs.Bold)),
            });

        List<OutputWrap.Cell> cells = OutputWrap.Cells(line);

        Assert.Equal(new[] { "é", "x" }, cells.Select(cell => cell.Text));
        Assert.Equal(new[] { 0, 1 }, cells.Select(cell => cell.Span));
        Assert.Equal(new[] { 0, 1 }, cells.Select(cell => cell.Column));
    }

    [Fact]
    public void Leading_combining_mark_gives_no_cell_and_the_plain_text_is_unchanged()
    {
        OutputLine line = Line("́a");

        Assert.Equal("́a", line.PlainText);
        Assert.Equal(new[] { "a" }, OutputWrap.Cells(line).Select(cell => cell.Text));
        Assert.Equal(0, OutputWrap.Cells(line)[0].Column);
        Assert.Equal(new[] { (0, 1) }, OutputWrap.Rows(line, 5));
    }

    [Fact]
    public void Zwj_family_emoji_is_one_cell_with_the_width_of_its_grapheme()
    {
        const string family = "\U0001F468‍\U0001F469‍\U0001F467";
        List<OutputWrap.Cell> cells = OutputWrap.Cells(Line(family + "x"));

        Assert.Equal(2, cells.Count);
        Assert.Equal(family, cells[0].Text);
        Assert.Equal(0, cells[0].Column);
        Assert.Equal(DisplayWidth.OfGrapheme(family), cells[0].Width);
        Assert.Equal(cells[0].Width, cells[1].Column);
    }

    [Fact]
    public void Control_character_grapheme_is_a_space_of_width_one()
    {
        List<OutputWrap.Cell> cells = OutputWrap.Cells(Line("a\tb"));

        Assert.Equal(new[] { "a", " ", "b" }, cells.Select(cell => cell.Text));
        Assert.Equal(new[] { 1, 1, 1 }, cells.Select(cell => cell.Width));
    }

    [Fact]
    public void RowCount_equals_the_number_of_rows_for_every_width()
    {
        OutputLine[] lines =
        {
            Line("hello world, this is ascii text"),
            Line("日本語 テキスト 日本"),
            Line("éâ x́y", "z̈̈w"),
            Line("\U0001F468‍\U0001F469‍\U0001F467 ok \U0001F600\U0001F600"),
            Line("a\tb\u0001c\rd"),
            Line("́lead and 日́x"),
            Line("각 🇯🇵"),
            Line(""),
            Line(),
        };

        foreach (OutputLine line in lines)
        {
            for (int width = 1; width <= 12; width++)
            {
                Assert.Equal(OutputWrap.Rows(line, width).Count, OutputWrap.RowCount(line, width));
            }
        }
    }

    [Theory]
    [InlineData("a")]
    [InlineData("日")]
    [InlineData("😀")]
    public void RowCount_does_not_allocate_per_cell_or_row(string unit)
    {
        OutputLine line = Line(string.Concat(Enumerable.Repeat(unit, 65_536 / unit.Length)));
        OutputWrap.RowCount(line, 80); // Warm up (fills the width cache).

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++)
        {
            OutputWrap.RowCount(line, 80);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 1024, $"RowCount allocated {allocated} bytes over 10 calls");
    }

    [Fact]
    public void Rows_cover_the_line_in_order_with_no_gaps()
    {
        OutputLine line = Line("the quick brown 日本語 fox");
        List<(int StartColumn, int EndColumn)> rows = OutputWrap.Rows(line, 6);
        Assert.Equal(0, rows[0].StartColumn);
        for (int i = 1; i < rows.Count; i++)
        {
            Assert.Equal(rows[i - 1].EndColumn, rows[i].StartColumn);
        }

        Assert.Equal(OutputWrap.Cells(line).Sum(cell => cell.Width), rows[^1].EndColumn);
    }

    [Fact]
    public void Cells_keep_their_span_across_a_break()
    {
        var line = new OutputLine(
            OutputSource.StdErr,
            new[] { new StyledSpan("ab", SpanStyle.Default), new StyledSpan("cd", new SpanStyle(AnsiColor.Default, AnsiColor.Default, TextAttrs.Bold)) });

        Assert.Equal(new[] { (0, 3), (3, 4) }, OutputWrap.Rows(line, 3));
        Assert.Equal(new[] { 0, 0, 1, 1 }, OutputWrap.Cells(line).Select(cell => cell.Span));
        Assert.Equal(new[] { 0, 1, 2, 3 }, OutputWrap.Cells(line).Select(cell => cell.Column));
    }

    [Fact]
    public void Widths_below_one_count_as_one()
    {
        Assert.Equal(new[] { (0, 1), (1, 2), (2, 3) }, OutputWrap.Rows(new[] { 1, 1, 1 }, 0));
        Assert.Equal(new[] { (0, 1), (1, 2), (2, 3) }, OutputWrap.Rows(new[] { 1, 1, 1 }, -4));
    }

    [Fact]
    public void Zero_widths_are_skipped_and_no_cells_give_one_empty_row()
    {
        Assert.Equal(new[] { (0, 2) }, OutputWrap.Rows(new[] { 1, 0, 1 }, 2));
        Assert.Equal(new[] { (0, 0) }, OutputWrap.Rows(Array.Empty<int>(), 3));
    }

    [Fact]
    public void First_cell_at_finds_the_first_cell_from_a_column()
    {
        List<OutputWrap.Cell> cells = OutputWrap.Cells(Line("a日b"));
        Assert.Equal(0, OutputWrap.FirstCellAt(cells, 0));
        Assert.Equal(1, OutputWrap.FirstCellAt(cells, 1));
        Assert.Equal(2, OutputWrap.FirstCellAt(cells, 2));
        Assert.Equal(2, OutputWrap.FirstCellAt(cells, 3));
        Assert.Equal(3, OutputWrap.FirstCellAt(cells, 4));
    }

    [Fact]
    public void Ascii_cells_have_offsets_counting_up_from_zero_with_length_one()
    {
        List<OutputWrap.Cell> cells = OutputWrap.Cells(Line("abc"));

        Assert.Equal(new[] { 0, 1, 2 }, cells.Select(cell => cell.Offset));
        Assert.Equal(new[] { 1, 1, 1 }, cells.Select(cell => cell.Length));
    }

    [Fact]
    public void Wide_characters_have_one_unit_each_in_offset_and_length()
    {
        List<OutputWrap.Cell> cells = OutputWrap.Cells(Line("a日本"));

        Assert.Equal(new[] { 0, 1, 2 }, cells.Select(cell => cell.Offset));
        Assert.Equal(new[] { 1, 1, 1 }, cells.Select(cell => cell.Length));
    }

    [Fact]
    public void Combining_mark_cell_covers_base_and_mark_in_offset_and_length()
    {
        // "e" + U+0301 is two UTF-16 units and one grapheme.
        List<OutputWrap.Cell> cells = OutputWrap.Cells(Line("e\u0301x"));

        Assert.Equal(new[] { 0, 2 }, cells.Select(cell => cell.Offset));
        Assert.Equal(new[] { 2, 1 }, cells.Select(cell => cell.Length));
    }

    [Fact]
    public void Surrogate_pair_cell_has_length_two_and_the_next_cell_starts_after_it()
    {
        List<OutputWrap.Cell> cells = OutputWrap.Cells(Line("\U0001F600x"));

        Assert.Equal(new[] { 0, 2 }, cells.Select(cell => cell.Offset));
        Assert.Equal(new[] { 2, 1 }, cells.Select(cell => cell.Length));
    }

    [Fact]
    public void Control_character_cell_keeps_the_source_offset_and_length()
    {
        List<OutputWrap.Cell> cells = OutputWrap.Cells(Line("a\tb"));

        Assert.Equal(" ", cells[1].Text);
        Assert.Equal(1, cells[1].Offset);
        Assert.Equal(1, cells[1].Length);
        Assert.Equal(2, cells[2].Offset);
    }

    [Fact]
    public void Zero_width_grapheme_gives_no_cell_so_the_next_offset_jumps_past_it()
    {
        // A leading combining mark has no cell: "a" is the first cell, at offset 1, and its length is 1.
        List<OutputWrap.Cell> cells = OutputWrap.Cells(Line("\u0301a"));

        Assert.Single(cells);
        Assert.Equal(1, cells[0].Offset);
        Assert.Equal(1, cells[0].Length);
    }
}
