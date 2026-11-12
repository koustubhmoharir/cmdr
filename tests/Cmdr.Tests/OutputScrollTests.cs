using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class OutputScrollTests
{
    // Lines 0..rows.Length-1, line n wraps to rows[n] screen rows.
    private static Func<long, int> RowsOf(params int[] rows) => line => rows[line];

    private static long Last(int[] rows) => rows.Length - 1;

    [Fact]
    public void Max_top_shows_the_last_rows()
    {
        int[] rows = { 1, 1, 1, 1, 1 };
        Assert.Equal(new ScrollPosition(2, 0), OutputScroll.MaxTop(0, Last(rows), 3, RowsOf(rows)));
    }

    [Fact]
    public void Max_top_can_start_inside_a_tall_last_line()
    {
        int[] rows = { 1, 1, 5 };
        Assert.Equal(new ScrollPosition(2, 2), OutputScroll.MaxTop(0, Last(rows), 3, RowsOf(rows)));
    }

    [Fact]
    public void Max_top_is_the_first_line_when_everything_fits()
    {
        int[] rows = { 1, 2 };
        Assert.Equal(new ScrollPosition(0, 0), OutputScroll.MaxTop(0, Last(rows), 10, RowsOf(rows)));
    }

    [Fact]
    public void Clamp_limits_the_row_to_the_line_and_the_position_to_max_top()
    {
        int[] rows = { 1, 3, 1 };
        Func<long, int> rowsOf = RowsOf(rows);
        Assert.Equal(new ScrollPosition(1, 2), OutputScroll.Clamp(new ScrollPosition(1, 9), 0, 2, 2, rowsOf));
        Assert.Equal(new ScrollPosition(1, 2), OutputScroll.Clamp(new ScrollPosition(2, 0), 0, 2, 2, rowsOf));
        Assert.Equal(new ScrollPosition(0, 0), OutputScroll.Clamp(new ScrollPosition(-5, 3), 0, 2, 2, rowsOf));
    }

    [Fact]
    public void Scroll_moves_by_screen_rows_across_lines()
    {
        int[] rows = { 2, 2, 2 };
        Func<long, int> rowsOf = RowsOf(rows);
        Assert.Equal(new ScrollPosition(1, 1), OutputScroll.ScrollBy(new ScrollPosition(0, 0), 3, 0, 2, 2, rowsOf));
        Assert.Equal(new ScrollPosition(0, 1), OutputScroll.ScrollBy(new ScrollPosition(1, 0), -1, 0, 2, 2, rowsOf));
        Assert.Equal(new ScrollPosition(0, 0), OutputScroll.ScrollBy(new ScrollPosition(2, 0), -5, 0, 2, 2, rowsOf));
    }

    [Fact]
    public void Scroll_stops_at_max_top()
    {
        int[] rows = { 2, 2, 2 };
        Assert.Equal(
            new ScrollPosition(2, 0),
            OutputScroll.ScrollBy(new ScrollPosition(0, 0), 10, 0, 2, 2, RowsOf(rows)));
    }

    [Fact]
    public void Ensure_visible_scrolls_down_just_enough_to_show_the_cursor_line()
    {
        int[] rows = { 1, 1, 1, 1, 1, 1 };
        Assert.Equal(new ScrollPosition(2, 0), OutputScroll.EnsureVisible(new ScrollPosition(0, 0), 4, 0, 5, 3, RowsOf(rows)));
    }

    [Fact]
    public void Ensure_visible_keeps_the_top_when_the_cursor_line_is_already_shown()
    {
        int[] rows = { 1, 1, 1, 1, 1, 1 };
        Assert.Equal(new ScrollPosition(0, 0), OutputScroll.EnsureVisible(new ScrollPosition(0, 0), 2, 0, 5, 3, RowsOf(rows)));
    }

    [Fact]
    public void Ensure_visible_puts_a_cursor_line_above_the_top_at_the_top()
    {
        int[] rows = { 1, 1, 1, 1, 1 };
        Assert.Equal(new ScrollPosition(1, 0), OutputScroll.EnsureVisible(new ScrollPosition(3, 0), 1, 0, 4, 3, RowsOf(rows)));
    }

    [Fact]
    public void Ensure_visible_puts_a_cursor_line_that_the_top_cuts_at_the_top()
    {
        int[] rows = { 1, 1, 4, 1 };
        Assert.Equal(new ScrollPosition(2, 0), OutputScroll.EnsureVisible(new ScrollPosition(2, 1), 2, 0, 3, 3, RowsOf(rows)));
    }

    [Fact]
    public void Ensure_visible_fills_the_rest_of_the_view_with_rows_above_the_cursor()
    {
        // Cursor line 3 (one row) at the bottom of a 4-row view: line 2 (one row) and the last two rows of line 1.
        int[] rows = { 1, 3, 1, 1 };
        Assert.Equal(new ScrollPosition(1, 1), OutputScroll.EnsureVisible(new ScrollPosition(0, 0), 3, 0, 3, 4, RowsOf(rows)));
    }

    [Fact]
    public void Ensure_visible_shows_the_first_row_of_a_cursor_line_taller_than_the_view()
    {
        int[] rows = { 1, 1, 5, 1 };
        Assert.Equal(new ScrollPosition(2, 0), OutputScroll.EnsureVisible(new ScrollPosition(0, 0), 2, 0, 3, 3, RowsOf(rows)));
    }

    [Fact]
    public void Page_target_is_the_line_a_view_of_rows_away()
    {
        int[] rows = { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };
        Assert.Equal(3L, OutputScroll.PageTarget(0, 1, 0, Last(rows), 3, RowsOf(rows)));
        Assert.Equal(5L, OutputScroll.PageTarget(8, -1, 0, Last(rows), 3, RowsOf(rows)));
    }

    [Fact]
    public void Page_target_is_clamped_to_the_retained_lines()
    {
        int[] rows = { 1, 1, 1, 1, 1 };
        Assert.Equal(4L, OutputScroll.PageTarget(3, 1, 0, Last(rows), 3, RowsOf(rows)));
        Assert.Equal(0L, OutputScroll.PageTarget(1, -1, 0, Last(rows), 10, RowsOf(rows)));
    }

    [Fact]
    public void Page_target_moves_at_least_one_line_even_when_a_line_is_taller_than_the_view()
    {
        int[] rows = { 4, 1, 1, 1 };
        Assert.Equal(1L, OutputScroll.PageTarget(0, 1, 0, Last(rows), 2, RowsOf(rows)));
        int[] up = { 5, 1 };
        Assert.Equal(0L, OutputScroll.PageTarget(1, -1, 0, Last(up), 2, RowsOf(up)));
    }

    [Fact]
    public void Page_target_stops_on_a_tall_line_that_holds_the_row()
    {
        int[] rows = { 1, 5, 1 };
        Assert.Equal(1L, OutputScroll.PageTarget(0, 1, 0, Last(rows), 3, RowsOf(rows)));
        int[] up = { 1, 1, 6, 1 };
        Assert.Equal(2L, OutputScroll.PageTarget(3, -1, 0, Last(up), 3, RowsOf(up)));
    }
}
