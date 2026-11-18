using Logvu.Core.Detail;
using Logvu.Tui;
using Shared.Tui;

namespace Logvu.Tests;

/// <summary>The lazy wrapping of a detail line: the rows, the continuation indent, and that only the requested rows are measured.</summary>
public sealed class DetailWrapTests
{
    private static DetailLine Line(string text, int indent = 0) => new(text, [], LineKind.Message, indent, null, null);

    [Fact]
    public void A_short_line_is_one_row()
    {
        var wrap = new DetailWrap(Line("hello", indent: 2), 20);

        Assert.Equal(new[] { new WrapRow(0, 5, 0) }, wrap.Rows(0, 5));
        Assert.Equal(1, wrap.RowCount);
    }

    [Fact]
    public void A_long_line_wraps_at_word_cells_and_continuation_rows_start_at_the_indent()
    {
        // Width 20 with an indent of 5 (under the 10 columns that switch it off): the first row takes 20 cells, the space after "dddd"
        // included, and the continuation rows take 15 cells after the indent.
        var wrap = new DetailWrap(Line("aaaa bbbb cccc dddd eeee ffff gggg hhhh", indent: 5), 20);

        Assert.Equal(
            new[] { new WrapRow(0, 20, 0), new WrapRow(20, 15, 5), new WrapRow(35, 4, 5) },
            wrap.Rows(0, 10));
    }

    [Fact]
    public void An_indent_of_width_minus_ten_or_more_wraps_the_continuation_rows_at_column_zero()
    {
        var line = Line("aaaa bbbb cccc dddd eeee ffff", indent: 10);

        Assert.Equal(0, DetailWrap.ContinuationIndent(line, 15));
        Assert.Equal(10, DetailWrap.ContinuationIndent(line, 21));
        Assert.Equal(
            new[] { new WrapRow(0, 15, 0), new WrapRow(15, 14, 0) },
            new DetailWrap(line, 15).Rows(0, 10));
    }

    [Fact]
    public void Wide_characters_are_never_split_and_no_row_exceeds_the_width()
    {
        // Each character is two columns: two fit in a row of five, with the third left to the next row.
        string text = "漢字漢字漢字";
        var wrap = new DetailWrap(Line(text), 5);

        IReadOnlyList<WrapRow> rows = wrap.Rows(0, 10);

        Assert.Equal(new[] { new WrapRow(0, 2, 0), new WrapRow(2, 2, 0), new WrapRow(4, 2, 0) }, rows);
        foreach (WrapRow row in rows)
        {
            Assert.True(TextWidth.Columns(text.Substring(row.Start, row.Length)) <= 5);
        }
    }

    [Fact]
    public void A_combining_mark_stays_with_its_base_character()
    {
        // "e" with a combining acute accent is one grapheme of one column: two of them fit in a row of two.
        string text = string.Concat(Enumerable.Repeat("é", 5));
        var wrap = new DetailWrap(Line(text), 2);

        IReadOnlyList<WrapRow> rows = wrap.Rows(0, 10);

        Assert.Equal(new[] { new WrapRow(0, 4, 0), new WrapRow(4, 4, 0), new WrapRow(8, 2, 0) }, rows);
        Assert.All(rows, row => Assert.NotEqual('́', text[row.Start]));
    }

    [Fact]
    public void An_empty_line_is_one_empty_row()
    {
        var wrap = new DetailWrap(Line(string.Empty), 10);

        Assert.Equal(new[] { new WrapRow(0, 0, 0) }, wrap.Rows(0, 3));
        Assert.Equal(1, wrap.RowCount);
    }

    [Fact]
    public void Rows_past_the_last_row_are_not_returned_and_the_row_count_is_the_total()
    {
        var wrap = new DetailWrap(Line("abc"), 2);

        Assert.Equal(new[] { new WrapRow(0, 2, 0), new WrapRow(2, 1, 0) }, wrap.Rows(0, 5));
        Assert.Empty(wrap.Rows(2, 1));
        Assert.Equal(2, wrap.RowCount);
    }

    [Fact]
    public void Consecutive_requests_return_the_rows_of_one_request()
    {
        // Requests continue where the last one stopped: the rows are those of one request for all of them.
        string text = string.Join(' ', Enumerable.Range(0, 40).Select(i => $"w{i:00}"));
        var line = Line(text, indent: 3);

        var piecewise = new DetailWrap(line, 16);
        List<WrapRow> rows = piecewise.Rows(0, 2).Concat(piecewise.Rows(2, 3)).Concat(piecewise.Rows(5, 4)).ToList();

        Assert.Equal(9, rows.Count);
        Assert.Equal(new DetailWrap(line, 16).Rows(0, 9), rows);
    }

    [Fact]
    public void Requesting_the_first_rows_of_a_megabyte_line_measures_only_those_rows()
    {
        const int width = 80;
        var line = Line(new string('a', 1_000_000));
        var wrap = new DetailWrap(line, width);

        IReadOnlyList<WrapRow> rows = wrap.Rows(0, 3);

        Assert.Equal(new[] { new WrapRow(0, 80, 0), new WrapRow(80, 80, 0), new WrapRow(160, 80, 0) }, rows);

        // Each row measures its cells and the one grapheme that did not fit, so three rows measure at most 3 * (width + 1).
        Assert.True(wrap.Measured <= 3 * (width + 1), $"measured {wrap.Measured} graphemes for three rows");
    }
}
