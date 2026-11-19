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

        Assert.Equal((Key)'f', line.KeyAt(0));
        Assert.Equal((Key)'f', line.KeyAt(3));
        Assert.Null(line.KeyAt(4));
        Assert.Equal((Key)'x', line.KeyAt(5));
        Assert.Null(line.KeyAt(6));
    }

    [Fact]
    public void A_left_press_on_text_without_a_target_is_consumed_and_performs_nothing()
    {
        var line = new HintsLine();
        line.Show(Sample);

        // "level" has no target.
        int column = Sample.Text.IndexOf("level", StringComparison.Ordinal);
        Assert.True(line.HandleMouse(Press(column)));
        Assert.Null(line.KeyAt(column));
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
