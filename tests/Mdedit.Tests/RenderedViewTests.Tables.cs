using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Point = System.Drawing.Point;

namespace Mdedit.Tests;

/// <summary>
/// The horizontal scrolling of a wide table at 40 columns: the first columns and the right edge marker, the keys, the wheel, the clicks and
/// the drag on a scrolled table, the caret kept in view, the prefix of a table in a quote, and the clamp at the end. The expected rows are
/// the table's columns at each offset (the cell bars are at columns 23 and 46 of the table), not the layout's output.
/// </summary>
public sealed partial class RenderedViewTests
{
    // Three 20-letter header cells: the table is 70 columns wide, and its minimum widths (20 each) do not fit in 40 columns.
    private const string WideTable =
        "| Abcdefghijklmnopqrst | Bcdefghijklmnopqrstu | Cdefghijklmnopqrstuv |\n|---|---|---|\n| one | two | three |\n\nAfter the table.\n";

    private const string FitTable = "| a | b |\n|---|---|\n| 1 | 2 |\n\nAfter.\n";

    private static readonly Key Greater = (Key)'>';
    private static readonly Key Less = (Key)'<';

    [Fact]
    public void A_table_wider_than_the_view_shows_its_first_columns_and_a_right_edge_marker()
    {
        Result result = Run(View(WideTable), v => { });

        Assert.Equal(Pad(InitialTableRows()), Rows(result.Screens[0]));
        // The edge marker is drawn in the table-border colour, dim.
        Assert.Equal(MdColors.Dim, result.Screens[0].Cells[0][39].Attribute.Foreground);
    }

    [Fact]
    public void Greater_than_scrolls_the_table_by_eight_columns_with_a_left_marker_and_less_than_scrolls_back()
    {
        List<TerminalScreen> screens = Screens(
            View(WideTable),
            v => v.HandleKey(Greater),
            v => v.HandleKey(Less));

        Assert.Equal(Pad(ScrolledTableRows()), Rows(screens[1]));
        Assert.Equal(Pad(InitialTableRows()), Rows(screens[2]));
    }

    [Fact]
    public void Shift_wheel_over_the_table_scrolls_it_by_eight_columns()
    {
        List<TerminalScreen> screens = Screens(
            View(WideTable),
            v => v.HandleMouse(new Mouse { Flags = MouseFlags.WheeledDown | MouseFlags.Shift, Position = new Point(5, 1) }));

        Assert.Equal(Pad(ScrolledTableRows()), Rows(screens[1]));
    }

    [Fact]
    public void A_horizontal_wheel_over_the_table_scrolls_it_by_eight_columns()
    {
        List<TerminalScreen> screens = Screens(
            View(WideTable),
            v => v.HandleMouse(new Mouse { Flags = MouseFlags.WheeledRight, Position = new Point(5, 1) }));

        Assert.Equal(Pad(ScrolledTableRows()), Rows(screens[1]));
    }

    [Fact]
    public void A_click_on_the_right_edge_marker_scrolls_the_table_and_leaves_the_caret_where_it_was()
    {
        RenderedView view = View(WideTable);
        int caret = Offset(WideTable, "Abcdefghijklmnopqrst");

        List<TerminalScreen> screens = Screens(view, v => v.HandleMouse(Click(39, 1)));

        Assert.Equal(Pad(ScrolledTableRows()), Rows(screens[1]));
        Assert.Equal(caret, view.CursorOffset);
    }

    [Fact]
    public void A_click_on_the_left_edge_marker_scrolls_the_table_back_by_eight_columns()
    {
        List<TerminalScreen> screens = Screens(
            View(WideTable),
            v => v.HandleKey(Greater),
            v => v.HandleMouse(Click(0, 1)));

        Assert.Equal(Pad(InitialTableRows()), Rows(screens[2]));
    }

    [Fact]
    public void The_offset_stops_at_the_end_of_the_table_so_the_last_cell_is_the_last_column_before_the_right_edge()
    {
        List<TerminalScreen> screens = Screens(View(WideTable), Enumerable.Repeat<Action<RenderedView>>(v => v.HandleKey(Greater), 10).ToArray());

        string header = Rows(screens[^1])[1];
        // The last cell ends at the table's last column, with the left marker in the first column and no right marker.
        Assert.StartsWith("◂", header, StringComparison.Ordinal);
        Assert.EndsWith("Cdefghijklmnopqrstuv │", header, StringComparison.Ordinal);
        Assert.DoesNotContain("▸", header, StringComparison.Ordinal);
    }

    [Fact]
    public void A_click_on_a_scrolled_cell_places_the_caret_on_the_cell_at_its_offset_and_draws_it_in_its_column()
    {
        RenderedView view = View(WideTable);
        int first = Offset(WideTable, "Abcdefghijklmnopqrst");
        int bee = Offset(WideTable, "Bcdefghijklmnopqrstu");

        Result result = Run(
            view,
            v => v.HandleKey(Greater),
            v => v.HandleMouse(Click(1, 1)),
            v => v.HandleMouse(Click(17, 1)));

        // Column 1 of the scrolled header is column 9 of the table: the seventh letter of the first cell (its index 7, 'h').
        Assert.Equal(first + 7, result.Snaps[1].Offset);
        Assert.Equal(new Point(1, 1), result.Snaps[1].Cursor);
        // Column 17 is column 25 of the table, the first letter of the second cell.
        Assert.Equal(bee, result.Snaps[2].Offset);
        Assert.Equal(new Point(17, 1), result.Snaps[2].Cursor);
    }

    [Fact]
    public void A_drag_on_a_scrolled_table_selects_the_cells_under_the_mouse_through_the_offset()
    {
        RenderedView view = View(WideTable);
        int bee = Offset(WideTable, "Bcdefghijklmnopqrstu");

        Result result = Run(
            view,
            v => v.HandleKey(Greater),
            v => v.HandleMouse(Click(17, 1)),
            v => v.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed | MouseFlags.PositionReport, Position = new Point(20, 1) }));

        // Column 20 is column 28 of the table: the fourth letter of the second cell ('e'), so the selection is the first three letters.
        Assert.Equal(new SourceRange(bee, bee + 3), view.SelectionRange);
        Assert.Equal(bee + 3, result.Snaps[2].Offset);
    }

    [Fact]
    public void Moving_the_caret_into_a_hidden_column_scrolls_the_table_by_the_least_amount()
    {
        RenderedView view = View(WideTable);
        int last = Offset(WideTable, "Cdefghijklmnopqrstuv") + 19;

        // End goes to the last letter of the header row, which is in the third cell, at table column 67: the least offset that shows it
        // is 29, with the letter in the last column before the right marker.
        List<TerminalScreen> screens = Screens(view, v => v.HandleKey(Key.End));

        Assert.Equal(last, view.CursorOffset);
        Assert.EndsWith("Cdefghijklmnopqrstuv▸", Rows(screens[1])[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_that_fits_has_no_offset_and_the_keys_do_nothing_to_it()
    {
        List<TerminalScreen> screens = Screens(View(FitTable), v => v.HandleKey(Greater));

        Assert.Equal(Rows(screens[0]), Rows(screens[1]));
        Assert.Equal(Pad(["┌───┬───┐", "│ a │ b │", "├───┼───┤", "│ 1 │ 2 │", "└───┴───┘", "", "After."]), Rows(screens[0]));
    }

    [Fact]
    public void A_table_in_a_quote_keeps_its_bar_in_place_while_it_scrolls()
    {
        string quoted = string.Concat(WideTable.Split('\n').Select(line => line.Length == 0 ? "\n" : "> " + line + "\n"));
        List<TerminalScreen> screens = Screens(View(quoted), v => v.HandleKey(Greater));

        string before = Rows(screens[0])[1];
        string after = Rows(screens[1])[1];
        // The bar of the quote is the first two columns; the table's left border is the third column, which the marker replaces.
        Assert.Equal(before[..2], after[..2]);
        Assert.Equal("│", before.Substring(2, 1));
        Assert.Equal("◂", after.Substring(2, 1));
    }

    // ----- Helpers for the table tests -----

    /// <summary>The expected rows of <see cref="WideTable"/> at its start: the first 39 columns, and the right edge marker in column 39.</summary>
    private static string[] InitialTableRows() =>
    [
        "┌" + new string('─', 22) + "┬" + new string('─', 15) + "▸",
        "│ Abcdefghijklmnopqrst │ Bcdefghijklmno▸",
        "├" + new string('─', 22) + "┼" + new string('─', 15) + "▸",
        "│ one" + new string(' ', 17) + " │ two" + new string(' ', 11) + "▸",
        "└" + new string('─', 22) + "┴" + new string('─', 15) + "▸",
        "",
        "After the table.",
    ];

    /// <summary>The expected rows of <see cref="WideTable"/> scrolled by eight columns: a left marker, the cells from column 8, and the second bars.</summary>
    private static string[] ScrolledTableRows() =>
    [
        "◂" + new string('─', 14) + "┬" + new string('─', 22) + "┬▸",
        "◂hijklmnopqrst │ Bcdefghijklmnopqrstu │▸",
        "◂" + new string('─', 14) + "┼" + new string('─', 22) + "┼▸",
        "◂" + new string(' ', 13) + " │ two" + new string(' ', 17) + " │▸",
        "◂" + new string('─', 14) + "┴" + new string('─', 22) + "┴▸",
        "",
        "After the table.",
    ];

    /// <summary>The rows of a list padded with empty rows to the height of the view.</summary>
    private static string[] Pad(string[] rows) => rows.Concat(Enumerable.Repeat(string.Empty, Height - rows.Length)).ToArray();

    /// <summary>The text of every row of a screen, the trailing spaces trimmed.</summary>
    private static string[] Rows(TerminalScreen screen) => Enumerable.Range(0, Height).Select(screen.Text).ToArray();

    /// <summary>The screens after each action: the first is the view before any action, then one after each action.</summary>
    private static List<TerminalScreen> Screens(RenderedView view, params Action<RenderedView>[] actions) =>
        TerminalHarness.Run(view, Width, Height, actions.Select(action => (Action)(() => action(view))).ToArray());
}
