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
    public void Zero_width_rune_takes_no_column_and_is_skipped()
    {
        // U+0301 (combining acute accent) has no columns of its own.
        Assert.Equal(new[] { (0, 2) }, OutputWrap.Rows(Line("áb"), 2));
        Assert.Equal(new[] { 'a', 'b' }, OutputWrap.Cells(Line("áb")).Select(cell => cell.Rune.ToString()[0]));
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
}
