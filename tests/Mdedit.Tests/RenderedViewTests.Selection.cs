using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Point = System.Drawing.Point;

namespace Mdedit.Tests;

/// <summary>
/// The rendered view's selection, as drawn and as offsets: Shift movement, the drag with its scrolling, the double-click word, the
/// selected table row, and the selection across a width change. The expected ranges come from the source text.
/// </summary>
public sealed partial class RenderedViewTests
{
    // A table whose data row is "│ one │ two │" at width 40.
    private const string Table = "| Name | Value |\n| --- | --- |\n| one | two |\n";

    [Fact]
    public void Shift_Right_three_times_selects_exactly_three_characters()
    {
        // The caret is a bar before a character: the selection is the range from the anchor to the caret.
        RenderedView view = View(Mixed);

        Result result = Run(
            view,
            v => v.HandleKey(Key.CursorRight.WithShift),
            v => v.HandleKey(Key.CursorRight.WithShift),
            v => v.HandleKey(Key.CursorRight.WithShift));

        int title = Offset(Mixed, "Title");
        Assert.Equal(new SourceRange(title, title + 3), view.SelectionRange);
        Assert.Equal("Tit", ReversedText(result.Screens[^1], 0));
        Assert.Equal(title + 3, result.Snaps[2].Offset);
    }

    [Fact]
    public void Shift_Down_extends_the_selection_across_a_wrapped_line()
    {
        RenderedView view = View(Mixed);

        Result result = Run(
            view,
            v => v.HandleMouse(Click(10, 2)),
            v => v.HandleKey(Key.CursorDown.WithShift));

        int intro = Offset(Mixed, "Intro");
        int wrap = Offset(Mixed, "wrap");
        Assert.Equal(new SourceRange(intro + 10, wrap + 10), view.SelectionRange);
        TerminalScreen screen = result.Screens[^1];
        Assert.Equal(Mixed.Substring(intro + 10, "Intro line one, which is long enough to ".Length - 10), ReversedText(screen, 2));
        Assert.Equal(Mixed.Substring(wrap, 10), ReversedText(screen, 3));
    }

    [Fact]
    public void A_plain_move_clears_the_selection_and_its_drawing()
    {
        RenderedView view = View(Mixed);

        Result result = Run(
            view,
            v => v.HandleKey(Key.CursorRight.WithShift),
            v => v.HandleKey(Key.CursorRight.WithShift),
            v => v.HandleKey(Key.CursorRight));

        Assert.Null(view.SelectionRange);
        Assert.Equal(string.Empty, ReversedText(result.Screens[^1], 0));
    }

    [Fact]
    public void A_click_clears_the_selection()
    {
        RenderedView view = View(Mixed);

        Result result = Run(
            view,
            v => v.HandleKey(Key.CursorRight.WithShift),
            v => v.HandleMouse(Click(20, 2)));

        Assert.Null(view.SelectionRange);
        Assert.Equal(string.Empty, ReversedText(result.Screens[^1], 0));
    }

    [Fact]
    public void A_drag_from_one_word_to_another_selects_the_range_between_them()
    {
        RenderedView view = View(Mixed);
        int intro = Offset(Mixed, "Intro");

        // The press is on "I" and the motion on the space after "line", in the same row: the selection stops before the space.
        Result result = Run(
            view,
            v => v.HandleMouse(Click(0, 2)),
            v => v.HandleMouse(Drag(10, 2)),
            v => v.HandleMouse(Release()));

        Assert.Equal(new SourceRange(intro, intro + "Intro line".Length), view.SelectionRange);
        Assert.Equal("Intro line", ReversedText(result.Screens[^1], 2));
    }

    [Fact]
    public void A_drag_below_the_view_scrolls_one_line_per_motion_and_a_drag_above_scrolls_back()
    {
        RenderedView view = View(Numbered(30));

        Result result = Run(
            view,
            v => v.HandleMouse(Click(0, 0)),
            v => v.HandleMouse(Drag(0, Height)),
            v => v.HandleMouse(Drag(0, Height)),
            v => v.HandleMouse(Drag(0, -1)));

        Assert.Equal(0, result.Snaps[0].Top);
        Assert.Equal(1, result.Snaps[1].Top);
        // The cursor is on the last row of the view, which is display line Height after one line of scrolling.
        Assert.Equal(Height, result.Snaps[1].Line);
        Assert.Equal(2, result.Snaps[2].Top);
        Assert.Equal(1, result.Snaps[3].Top);
        // The selection runs from the press, which is "p1", to the cursor below the view.
        Assert.Equal(Offset(Numbered(30), "p1"), view.SelectionRange!.Value.Start);
    }

    [Fact]
    public void A_double_click_selects_the_word_under_the_mouse_and_only_that_word()
    {
        RenderedView view = View(Mixed);

        Result result = Run(view, v => v.HandleMouse(DoubleClick(7, 2)));

        int line = Offset(Mixed, "line");
        Assert.Equal(new SourceRange(line, line + 4), view.SelectionRange);
        Assert.Equal("line", ReversedText(result.Screens[^1], 2));
    }

    [Fact]
    public void A_double_click_on_a_space_selects_nothing()
    {
        RenderedView view = View(Mixed);

        Result result = Run(view, v => v.HandleMouse(DoubleClick(5, 2)));

        Assert.Null(view.SelectionRange);
        Assert.Equal(string.Empty, ReversedText(result.Screens[^1], 2));
    }

    [Fact]
    public void A_selection_in_a_table_row_draws_the_border_between_the_selected_cells_reversed()
    {
        // The positions are found on the drawn screen: the press on "o" of "one" and the drag to "w" of "two", in the data row.
        TerminalScreen before = Run(View(Table)).Screens[0];
        int row = before.RowOf("one");
        string text = before.Text(row);
        int first = text.IndexOf("one", StringComparison.Ordinal);
        int second = text.IndexOf("two", StringComparison.Ordinal);

        RenderedView view = View(Table);
        Result result = Run(
            view,
            v => v.HandleMouse(Click(first, row)),
            v => v.HandleMouse(Drag(second + 1, row)),
            v => v.HandleMouse(Release()));

        // The cells between the two selected ones are the padding and the border, which are reversed as well.
        Assert.Equal(text.Substring(first, second + 1 - first), ReversedText(result.Screens[^1], row));
    }

    [Fact]
    public void A_selection_survives_a_width_change()
    {
        string text = Repeat(Long, 12).TrimEnd();
        RenderedView view = View(text);
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(Width, Height);
        using var window = new Window { BorderStyle = LineStyle.None };
        view.X = 0;
        view.Y = 0;
        view.Width = Dim.Fill();
        view.Height = Dim.Fill();
        window.Add(view);

        Result result = RunResizing(
            app,
            window,
            view,
            v => { for (int i = 0; i < 3; i++) { v.HandleKey(Key.CursorRight.WithShift); } },
            _ => window.Width = 25);

        // Three moves from the start select "alp", and the range is the same after the reflow. The reversed cells on the screen are it.
        Assert.Equal(new SourceRange(0, 3), view.SelectionRange);
        Assert.Equal(text[..3], string.Concat(result.Screens[^1].Cells.SelectMany(row => row).Where(c => IsReversed(c.Attribute)).Select(c => c.Grapheme)));
    }

    [Fact]
    public void Shift_End_on_the_last_line_of_a_paragraph_selects_through_its_last_letter()
    {
        RenderedView view = View(Mixed);

        Result result = Run(view, v => v.HandleMouse(Click(0, 10)), v => v.HandleKey(Key.End.WithShift));

        int last = Offset(Mixed, "Last paragraph.");
        Assert.Equal(new SourceRange(last, last + "Last paragraph.".Length), view.SelectionRange);
        Assert.Equal("Last paragraph.", ReversedText(result.Screens[^1], 10));
    }

    [Fact]
    public void End_goes_to_the_end_position_and_the_terminal_cursor_is_drawn_after_the_last_character()
    {
        RenderedView view = View(Mixed);
        int end = Offset(Mixed, "Last paragraph.") + "Last paragraph.".Length;

        Result result = Run(view, v => v.HandleMouse(Click(3, 10)), v => v.HandleKey(Key.End), v => v.HandleKey(Key.CursorRight));

        Assert.Equal(end, result.Snaps[1].Offset);
        Assert.Equal(new Point(15, 10), result.Snaps[1].Cursor);
        // The document ends there, so Right has nothing to go to.
        Assert.Equal(end, result.Snaps[2].Offset);
    }

    [Fact]
    public void Right_from_a_blocks_last_character_goes_to_its_end_position_and_then_to_the_next_block_and_Left_mirrors_it()
    {
        const string text = "One.\n\nTwo.\n";
        RenderedView view = View(text);

        Result result = Run(
            view,
            v => v.HandleMouse(Click(3, 0)),
            v => v.HandleKey(Key.CursorRight),
            v => v.HandleKey(Key.CursorRight),
            v => v.HandleKey(Key.CursorLeft));

        Assert.Equal(text.IndexOf('.'), result.Snaps[0].Offset);
        Assert.Equal(text.IndexOf("\n", StringComparison.Ordinal), result.Snaps[1].Offset);
        Assert.Equal(new Point(4, 0), result.Snaps[1].Cursor);
        Assert.Equal(text.IndexOf("Two", StringComparison.Ordinal), result.Snaps[2].Offset);
        Assert.Equal(text.IndexOf("\n", StringComparison.Ordinal), result.Snaps[3].Offset);
    }

    [Fact]
    public void A_click_past_the_end_of_a_blocks_last_line_lands_on_its_end_position()
    {
        RenderedView view = View(Mixed);
        int end = Offset(Mixed, "Last paragraph.") + "Last paragraph.".Length;

        Result result = Run(view, v => v.HandleMouse(Click(30, 10)));

        Assert.Equal(end, result.Snaps[0].Offset);
        Assert.Equal(new Point(15, 10), result.Snaps[0].Cursor);
    }

    // ----- Selection helpers -----

    /// <summary>The drag motion: the left button is down, and the mouse reports a position.</summary>
    private static Mouse Drag(int x, int y) => new() { Flags = MouseFlags.LeftButtonPressed | MouseFlags.PositionReport, Position = new Point(x, y) };

    /// <summary>The release of the left button.</summary>
    private static Mouse Release() => new() { Flags = MouseFlags.LeftButtonReleased };

    /// <summary>The double-click of the left button at a view position.</summary>
    private static Mouse DoubleClick(int x, int y) => new() { Flags = MouseFlags.LeftButtonDoubleClicked, Position = new Point(x, y) };

    /// <summary>Whether an attribute is drawn reversed.</summary>
    private static bool IsReversed(Attribute attribute) => (attribute.Style & TextStyle.Reverse) != 0;

    /// <summary>The text of the reversed cells of a screen row, in order.</summary>
    private static string ReversedText(TerminalScreen screen, int row) =>
        string.Concat(screen.Cells[row].Where(cell => IsReversed(cell.Attribute)).Select(cell => cell.Grapheme));
}
