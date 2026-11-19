using Cmdr.Tui;
using Terminal.Gui.Input;

namespace Cmdr.Tests;

/// <summary>
/// The hints line's mapping from display columns to keys: the left text, the right part at the right edge, the text cut to make room
/// for it, and messages, which have no targets. The drawing and the clicks are tested with the controller (HintsLineClickTests).
/// </summary>
public sealed class HintsLineTests
{
    private static readonly Hint Quit = KeyHints.Concat(KeyHints.Plain(KeyHints.Separator), KeyHints.Bound(Key.Q.WithCtrl, "^Q Quit"));

    [Fact]
    public void The_line_cannot_take_the_focus()
    {
        Assert.False(new StatusLine().CanFocus);
    }

    [Fact]
    public void Columns_are_display_columns_so_a_wide_glyph_takes_the_columns_it_is_drawn_in()
    {
        // Two wide characters take four columns, so the plain space is column 4 and the key after it is column 5.
        Assert.Equal(4, TextWidth.Columns("文件"));
        var line = new StatusLine();
        line.Show(KeyHints.Concat(KeyHints.Bound(Key.F, "文件"), KeyHints.Plain(" "), KeyHints.Bound(Key.X, "x")), KeyHints.Plain(string.Empty));

        Assert.Equal(Key.F, line.KeyAt(0, 20));
        Assert.Equal(Key.F, line.KeyAt(3, 20));
        Assert.Null(line.KeyAt(4, 20));
        Assert.Equal(Key.X, line.KeyAt(5, 20));
        Assert.Null(line.KeyAt(6, 20));
    }

    [Fact]
    public void The_right_part_stays_at_the_right_edge_and_its_quit_key_is_at_its_columns()
    {
        var line = new StatusLine();
        line.Show(KeyHints.Join(KeyHints.Bound(Key.Enter, "Enter Start"), KeyHints.Bound(Key.F5, "F5 Restart")), Quit);

        // The right part is "  ^Q Quit" (9 columns), so it starts at column 51 of a 60-column line.
        Assert.Null(line.KeyAt(51, 60));
        Assert.Null(line.KeyAt(52, 60));
        Assert.Equal(Key.Q.WithCtrl, line.KeyAt(53, 60));
        Assert.Equal(Key.Q.WithCtrl, line.KeyAt(59, 60));
        Assert.Null(line.KeyAt(60, 60));
    }

    [Fact]
    public void The_left_text_is_cut_to_make_room_for_the_right_part_and_its_targets_keep_their_columns()
    {
        var line = new StatusLine();
        line.Show(KeyHints.Join(KeyHints.Bound(Key.Enter, "Enter Start"), KeyHints.Bound(Key.F5, "F5 Restart")), Quit);

        // At 20 columns the left part has 11 columns: "Enter Start" is cut inside its target, so the Enter target still covers column 0.
        Assert.Equal(Key.Enter, line.KeyAt(0, 20));
        Assert.Null(line.KeyAt(11, 20));
        Assert.Equal(Key.Q.WithCtrl, line.KeyAt(13, 20));
        Assert.Equal(Key.Q.WithCtrl, line.KeyAt(19, 20));
    }

    [Fact]
    public void A_line_narrower_than_the_right_part_shows_the_start_of_it_at_column_zero()
    {
        var line = new StatusLine();
        line.Show(KeyHints.Bound(Key.Enter, "Enter Start"), Quit);

        // Five columns of "  ^Q Quit" are "  ^Q…": the quit key is on the ^ at column 2.
        Assert.Null(line.KeyAt(0, 5));
        Assert.Null(line.KeyAt(1, 5));
        Assert.Equal(Key.Q.WithCtrl, line.KeyAt(2, 5));
    }

    [Fact]
    public void A_message_has_no_targets_so_no_column_performs_a_key()
    {
        var line = new StatusLine();
        line.Show("Saved ~/cmdr.json", message: true, right: "  ^Q Quit");

        for (int column = 0; column < 60; column++)
        {
            Assert.Null(line.KeyAt(column, 60));
        }

        Assert.Empty(line.Shown.Targets);
        Assert.Empty(line.ShownRight.Targets);
    }

    [Fact]
    public void Showing_hints_keeps_their_text_and_targets_for_the_next_draw()
    {
        var line = new StatusLine();
        Hint hint = KeyHints.Join(KeyHints.Bound(Key.Space, "Space Mark"));
        line.Show(hint, Quit);

        Assert.Equal(hint.Text, line.Shown.Text);
        Assert.Equal(hint.Targets, line.Shown.Targets);
        Assert.Equal(Quit.Text, line.ShownRight.Text);
    }
}
