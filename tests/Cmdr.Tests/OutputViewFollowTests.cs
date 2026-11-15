using Cmdr.Tui;
using Terminal.Gui.App;

namespace Cmdr.Tests;

/// <summary>
/// Following means the viewport is at the tail. Scrolling within a tall last line, dragging, and Home must not set it just
/// because the cursor is on the last logical line.
/// </summary>
[Collection("Terminal GUI")]
public sealed class OutputViewFollowTests
{
    private const string Tall = "abcdefghijklmnopqrst"; // 20 cells: 5 rows in a 4-column view

    [Fact]
    public void Wheel_up_then_press_and_drag_inside_a_tall_last_line_select_by_logical_column_without_resuming()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, Tall);
        Assert.True(h.Following);
        Assert.Equal(new ScrollPosition(0, 2), h.EffectiveTop);

        h.Wheel(-3);
        Assert.False(h.Following);
        Assert.Equal(new ScrollPosition(0, 0), h.Top);

        h.Press(1, 1);
        Assert.False(h.Following);
        Assert.Equal(new ScrollPosition(0, 0), h.Top);
        Assert.Equal(new SelectionPoint(0, 5), h.Selection!.Value.Anchor);

        h.Drag(2, 1);
        Assert.False(h.Following);
        Assert.Equal(new ScrollPosition(0, 0), h.Top);
        Assert.Equal(new SelectionPoint(0, 6), h.Selection!.Value.Active);

        h.Release(2, 1);
        Assert.Equal(new SelectionPoint(0, 5), h.Selection!.Value.Anchor);
        Assert.Equal(new SelectionPoint(0, 6), h.Selection!.Value.Active);
        Assert.False(h.Following);
        Assert.Equal(new ScrollPosition(0, 0), h.Top);
    }

    [Fact]
    public void Home_on_a_single_tall_line_pauses_at_its_first_row()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, Tall);

        h.Home();

        Assert.False(h.Following);
        Assert.Equal(new ScrollPosition(0, 0), h.Top);
        Assert.Equal(new ScrollPosition(0, 0), h.EffectiveTop);
    }

    [Fact]
    public void Cursor_down_onto_a_tall_last_line_keeps_the_top_chosen_by_EnsureVisible()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, "x", Tall);
        h.Home();
        Assert.False(h.Following);

        h.CursorDown();

        Assert.Equal(1, h.CursorLine);
        Assert.Equal(new ScrollPosition(1, 0), h.Top);
        Assert.False(h.Following);
    }

    [Fact]
    public void Cursor_down_onto_a_last_line_that_fits_at_the_tail_resumes_following()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, "x", "abcdefgh"); // "abcdefgh" is 2 rows
        h.Home();
        Assert.False(h.Following);

        h.CursorDown();

        Assert.Equal(1, h.CursorLine);
        Assert.Equal(new ScrollPosition(0, 0), h.Top);
        Assert.True(h.Following);
    }

    [Fact]
    public void End_resumes_following_at_the_tail()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, Tall);
        h.Home();

        h.End();

        Assert.True(h.Following);
        Assert.Equal(new ScrollPosition(0, 2), h.EffectiveTop);
    }

    [Fact]
    public void Wheel_down_resumes_following_only_at_the_bottom()
    {
        using var app = Application.Create();
        app.Init("ansi");
        // Three tall lines: 15 rows, so the viewport (3 rows) can stop short of the bottom after a wheel step.
        using OutputViewHarness h = OutputViewHarness.Create(app, Tall, Tall, Tall);
        Assert.Equal(new ScrollPosition(2, 2), h.EffectiveTop);

        h.Wheel(-3);
        Assert.False(h.Following);
        Assert.Equal(new ScrollPosition(1, 4), h.Top);
        h.Wheel(-3);
        Assert.Equal(new ScrollPosition(1, 1), h.Top);

        h.Wheel(3);
        Assert.False(h.Following);
        Assert.Equal(new ScrollPosition(1, 4), h.Top);

        h.Wheel(3);
        Assert.True(h.Following);
        Assert.Equal(new ScrollPosition(2, 2), h.EffectiveTop);
    }

    [Fact]
    public void Wheel_down_at_the_bottom_moves_an_earlier_cursor_down_and_resumes_following_on_the_last_line()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, "a", "b", "c", "d", "e", "f");

        h.Home();
        Assert.Equal(0, h.CursorLine);

        h.Wheel(3); // the viewport reaches the bottom; the cursor stays where it was
        Assert.Equal(new ScrollPosition(3, 0), h.Top);
        Assert.Equal(0, h.CursorLine);
        Assert.False(h.Following);

        h.Wheel(3); // the cursor was above the view: it moves to the top line in view
        Assert.Equal(3, h.CursorLine);
        Assert.False(h.Following);

        h.Wheel(3); // three lines down, stopping at the last line
        Assert.Equal(5, h.CursorLine);
        Assert.True(h.Following);
    }

    [Fact]
    public void Plain_click_on_the_last_line_at_the_tail_keeps_following()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, Tall);
        Assert.True(h.Following);

        h.Press(0, 0);
        Assert.False(h.Following);
        h.Release(0, 0);

        Assert.True(h.Following);
        Assert.Null(h.Selection);
        Assert.Equal(new ScrollPosition(0, 2), h.EffectiveTop);
    }

    [Fact]
    public void Autoscroll_during_a_drag_that_reaches_the_bottom_does_not_resume_following()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, Tall);
        h.Wheel(-3);

        h.Press(0, 2);
        h.Drag(0, 3); // autoscroll one row: top (0,1)
        h.Drag(0, 3); // autoscroll reaches the bottom: top (0,2) = MaxTop

        Assert.Equal(new ScrollPosition(0, 2), h.Top);
        Assert.False(h.Following);

        h.Release(0, 2);
        Assert.False(h.Following);
        Assert.Equal(new ScrollPosition(0, 2), h.Top);
    }
}
