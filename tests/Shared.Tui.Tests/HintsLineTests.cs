using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Shared.Tui.Tests;

/// <summary>
/// The hints line as drawn and as clicked: a left press performs the key under it, through the application's keyboard, and the view takes
/// no focus. A press on text with no target does nothing.
/// </summary>
[Collection("Terminal GUI")]
public sealed class HintsLineTests
{
    // Three targets and a plain run, as a row of key hints: "f filter  -/+ level  ^Q quit".
    private static readonly Hint Sample = KeyHints.Join(
        KeyHints.Bound('f', "f filter"),
        KeyHints.Concat(KeyHints.Bound('-', "-"), KeyHints.Plain("/"), KeyHints.Bound('+', "+"), KeyHints.Plain(" level")),
        KeyHints.Bound(Key.Q.WithCtrl, "^Q quit"));

    [Fact]
    public void The_line_cannot_take_the_focus()
    {
        Assert.False(new HintsLine().CanFocus);
    }

    [Fact]
    public void Columns_are_display_columns_so_a_wide_glyph_takes_the_columns_it_is_drawn_in()
    {
        // Two wide characters take four columns, so the plain space is column 4 and the key after it is column 5.
        Assert.Equal(4, TextWidth.Columns("文件"));
        var line = new HintsLine();
        line.Show(KeyHints.Concat(KeyHints.Bound('f', "文件"), KeyHints.Plain(" "), KeyHints.Bound('x', "x")));

        Assert.Equal((Key)'f', line.KeyAt(0, 200));
        Assert.Equal((Key)'f', line.KeyAt(3, 200));
        Assert.Null(line.KeyAt(4, 200));
        Assert.Equal((Key)'x', line.KeyAt(5, 200));
        Assert.Null(line.KeyAt(6, 200));
    }

    [Fact]
    public void A_left_press_on_text_without_a_target_is_consumed_and_performs_nothing()
    {
        var line = new HintsLine();
        line.Show(Sample);

        // "level" has no target.
        int column = Sample.Text.IndexOf("level", StringComparison.Ordinal);
        Assert.True(line.HandleMouse(Press(column)));
        Assert.Null(line.KeyAt(column, 200));
    }

    [Fact]
    public void A_press_past_the_text_and_a_release_are_not_key_presses()
    {
        var line = new HintsLine();
        line.Show(Sample);

        Assert.True(line.HandleMouse(Press(200)));
        Assert.False(line.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonReleased, Position = new(0, 0) }));
    }

    [Fact]
    public void The_target_cells_are_drawn_as_buttons_and_the_rest_of_the_row_on_the_bar_colours()
    {
        var line = new HintsLine();
        line.Show(Sample);

        TerminalScreen screen = TerminalHarness.Run(line, 120, 1)[0];
        string text = Sample.Text;

        Assert.Equal(text, screen.Text(0));
        AssertButton(screen, 0, "f filter");
        AssertButton(screen, text.IndexOf("-", StringComparison.Ordinal), "-");
        AssertButton(screen, text.IndexOf("+", StringComparison.Ordinal), "+");
        AssertBar(screen, text.IndexOf("level", StringComparison.Ordinal), "level");
        AssertBar(screen, text.IndexOf("/", StringComparison.Ordinal), "/");
        AssertBar(screen, text.IndexOf("  ", StringComparison.Ordinal), "separator");
        AssertBar(screen, 119, "the empty end of the row");
    }

    [Fact]
    public void A_too_long_hint_is_cut_at_the_right_edge_with_an_ellipsis_as_logvu_draws_it()
    {
        // Pinned before HintsLine takes the cmdr layout: the cut text, the ellipsis in the last column, and the colours of the cells.
        var line = new HintsLine();
        line.Show(Sample);

        TerminalScreen wide = TerminalHarness.Run(line, 20, 1)[0];
        Assert.Equal("f filter  -/+ level…", wide.Text(0));
        AssertButton(wide, 0, "f filter");
        AssertButton(wide, 10, "-");
        AssertBar(wide, 11, "/");
        AssertButton(wide, 12, "+");
        AssertBar(wide, 19, "the ellipsis, on a plain space of the text");

        // The ellipsis takes the colours of its index in the text: index 4 is a letter of "f filter", so the ellipsis is a button.
        TerminalScreen narrow = TerminalHarness.Run(line, 5, 1)[0];
        Assert.Equal("f fi…", narrow.Text(0));
        AssertButton(narrow, 4, "the ellipsis, on a letter of a target");
    }

    // The right part of the cmdr bar: the quit key, drawn at the right edge.
    private static readonly Hint Quit = KeyHints.Concat(KeyHints.Plain(KeyHints.Separator), KeyHints.Bound(Key.Q.WithCtrl, "^Q Quit"));

    [Fact]
    public void The_right_part_stays_at_the_right_edge_and_its_quit_key_is_at_its_columns()
    {
        var line = new HintsLine();
        line.Show(KeyHints.Join(KeyHints.Bound(Key.Enter, "Enter Start"), KeyHints.Bound(Key.F5, "F5 Restart")), Quit);

        // The right part is "  ^Q Quit" (9 columns), so it starts at column 51 of a 60-column line.
        Assert.Null(line.KeyAt(51, 60));
        Assert.Null(line.KeyAt(52, 60));
        Assert.Equal((Key)Key.Q.WithCtrl, line.KeyAt(53, 60));
        Assert.Equal((Key)Key.Q.WithCtrl, line.KeyAt(59, 60));
        Assert.Null(line.KeyAt(60, 60));
    }

    [Fact]
    public void The_left_text_is_cut_to_make_room_for_the_right_part_and_its_targets_keep_their_columns()
    {
        var line = new HintsLine();
        line.Show(KeyHints.Join(KeyHints.Bound(Key.Enter, "Enter Start"), KeyHints.Bound(Key.F5, "F5 Restart")), Quit);

        // At 20 columns the left part has 11 columns: "Enter Start" is cut inside its target, so the Enter target still covers column 0.
        Assert.Equal((Key)Key.Enter, line.KeyAt(0, 20));
        Assert.Null(line.KeyAt(11, 20));
        Assert.Equal((Key)Key.Q.WithCtrl, line.KeyAt(13, 20));
        Assert.Equal((Key)Key.Q.WithCtrl, line.KeyAt(19, 20));
    }

    [Fact]
    public void A_line_narrower_than_the_right_part_shows_the_start_of_it_at_column_zero()
    {
        var line = new HintsLine();
        line.Show(KeyHints.Bound(Key.Enter, "Enter Start"), Quit);

        // Five columns of "  ^Q Quit" are "  ^Q…": the quit key is on the ^ at column 2.
        Assert.Null(line.KeyAt(0, 5));
        Assert.Null(line.KeyAt(1, 5));
        Assert.Equal((Key)Key.Q.WithCtrl, line.KeyAt(2, 5));
    }

    [Fact]
    public void The_right_part_is_drawn_as_buttons_at_the_right_edge_and_the_left_text_is_cut_before_it()
    {
        var line = new HintsLine();
        line.Show(KeyHints.Join(KeyHints.Bound(Key.Enter, "Enter Start"), KeyHints.Bound(Key.F5, "F5 Restart")), Quit);

        TerminalScreen screen = TerminalHarness.Run(line, 20, 1)[0];

        // The left part has 11 columns: "Enter Star" and an ellipsis. The ellipsis takes the colours of the character at its index,
        // which is the "t" of "Enter Start", so it is a button too.
        Assert.Equal("Enter Star…  ^Q Quit", screen.Text(0));
        AssertButton(screen, 0, "Enter");
        AssertButton(screen, 10, "the ellipsis");
        AssertBar(screen, 11, "the spaces before the right part");
        AssertButton(screen, 13, "^");
        AssertButton(screen, 19, "the end of the quit key");
    }

    [Fact]
    public void A_line_narrower_than_the_right_part_draws_its_start_only()
    {
        var line = new HintsLine();
        line.Show(KeyHints.Bound(Key.Enter, "Enter Start"), Quit);

        TerminalScreen screen = TerminalHarness.Run(line, 5, 1)[0];

        Assert.Equal("  ^Q…", screen.Text(0));
        AssertButton(screen, 2, "^");
    }

    [Fact]
    public void A_message_is_bold_on_the_bar_colours_and_has_no_targets()
    {
        var line = new HintsLine();
        line.Show("Saved ~/cmdr.json", message: true, right: "  ^Q Quit");

        Assert.Empty(line.Shown.Targets);
        Assert.Empty(line.ShownRight.Targets);
        TerminalScreen screen = TerminalHarness.Run(line, 40, 1)[0];

        Assert.Equal("Saved ~/cmdr.json", screen.Text(0).Substring(0, 17));
        Assert.Equal("^Q Quit", screen.Text(0).Substring(33, 7));
        for (int column = 0; column < 17; column++)
        {
            Attribute cell = screen.Cells[0][column].Attribute;
            Assert.Equal(TextStyle.Bold, cell.Style);
            Assert.True(cell.Background.Equals(BarColors.Background), $"column {column} background");
        }

        for (int column = 0; column < 40; column++)
        {
            Assert.Null(line.KeyAt(column, 40));
        }
    }

    [Fact]
    public void Showing_hints_keeps_their_text_and_targets_for_the_next_draw()
    {
        var line = new HintsLine();
        Hint hint = KeyHints.Join(KeyHints.Bound(Key.Space, "Space Mark"));
        line.Show(hint, Quit);

        Assert.Equal(hint.Text, line.Shown.Text);
        Assert.Equal(hint.Targets, line.Shown.Targets);
        Assert.Equal(Quit.Text, line.ShownRight.Text);
    }

    private static Mouse Press(int x) => new() { Flags = MouseFlags.LeftButtonPressed, Position = new(x, 0) };

    private static void AssertButton(TerminalScreen screen, int column, string what)
    {
        Attribute cell = screen.Cells[0][column].Attribute;
        Assert.True(cell.Background.Equals(BarColors.TargetBackground), $"{what} background");
        Assert.True(cell.Foreground.Equals(BarColors.TargetText), $"{what} text");
    }

    private static void AssertBar(TerminalScreen screen, int column, string what)
    {
        Attribute cell = screen.Cells[0][column].Attribute;
        Assert.True(cell.Background.Equals(BarColors.Background), $"{what} background");
        Assert.True(cell.Foreground.Equals(BarColors.Text), $"{what} text");
    }
}
