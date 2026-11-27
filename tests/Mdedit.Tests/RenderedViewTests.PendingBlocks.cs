using Mdedit.Core.Editing;
using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Point = System.Drawing.Point;

namespace Mdedit.Tests;

/// <summary>
/// The virtual line of a block pending (drawn below or above the caret's line, with the gutter and the display text, and the cursor after it), the
/// inline pending's display text, the caret of an empty document, the scroll that keeps the virtual line in view, and the caret stop at a hard
/// break's end.
/// </summary>
public sealed partial class RenderedViewTests
{
    // Two paragraphs with a blank line between them: the rows are the first paragraph, its separator, the second paragraph and its separator.
    private const string Pair = "Some text.\n\nOther.\n";

    [Fact]
    public void A_below_pending_draws_a_virtual_line_under_the_caret_with_the_gutter_and_the_display_text_and_the_caret_after_it()
    {
        RenderedView view = View(Pair);

        Result result = Run(
            view,
            v => v.PlaceCaret(0, null),
            v => v.SetPending(new ViewEditResult.Pending(0, string.Empty, PendingPlacement.Below, "> ")));

        string[] rows = Rows(result.Screens[^1]);
        Assert.Equal("  Some text.", rows[0].TrimEnd());
        Assert.Equal("  >", rows[1].TrimEnd());
        Assert.Equal(string.Empty, rows[2].TrimEnd());
        // The lines after the virtual line move down by one row.
        Assert.Equal("  Other.", rows[3].TrimEnd());
        Assert.Equal(new Point(4, 1), result.Snaps[^1].Cursor);
    }

    [Fact]
    public void An_above_pending_draws_a_virtual_line_over_the_caret_line_and_the_caret_is_on_it()
    {
        RenderedView view = View(Pair);

        Result result = Run(
            view,
            v => v.PlaceCaret(0, null),
            v => v.SetPending(new ViewEditResult.Pending(0, string.Empty, PendingPlacement.Above, "> ", "\n\n")));

        string[] rows = Rows(result.Screens[^1]);
        Assert.Equal("  >", rows[0].TrimEnd());
        Assert.Equal("  Some text.", rows[1].TrimEnd());
        Assert.Equal("  Other.", rows[3].TrimEnd());
        Assert.Equal(new Point(4, 0), result.Snaps[^1].Cursor);
    }

    [Fact]
    public void An_inline_pending_is_drawn_with_its_display_text_at_the_caret()
    {
        RenderedView view = View("Some text.\n");

        Result result = Run(view, v => v.PlaceCaret(0, null), v => v.SetPending(new ViewEditResult.Pending(0, "\\#", Display: "# ")));

        Assert.Equal("  # Some text.", Rows(result.Screens[^1])[0].TrimEnd());
        Assert.Equal(new Point(4, 0), result.Snaps[^1].Cursor);
    }

    [Fact]
    public void An_empty_document_has_a_caret_on_its_first_row_after_the_gutter_and_its_pending_is_drawn_there()
    {
        RenderedView view = View(string.Empty);

        Result result = Run(view, v => v.SetPending(new ViewEditResult.Pending(0, "# ", Display: "# ")));

        Assert.Equal("  #", Rows(result.Screens[^1])[0].TrimEnd());
        Assert.Equal(new Point(4, 0), result.Snaps[^1].Cursor);
    }

    [Fact]
    public void An_empty_document_without_a_pending_shows_its_caret_after_the_gutter()
    {
        RenderedView view = View(string.Empty);

        Result result = Run(view, v => v.SetPending(null));

        Assert.Equal(new Point(2, 0), result.Snaps[^1].Cursor);
    }

    [Fact]
    public void A_below_pending_at_the_bottom_of_the_viewport_scrolls_its_virtual_line_into_view()
    {
        // Paragraphs of two rows each: the caret on the 13th display line is on the view's last row, so a line below it needs one more scroll.
        string text = string.Concat(Enumerable.Range(1, 20).Select(n => $"Line {n}.\n\n"));
        RenderedView view = View(text);

        Result result = Run(
            view,
            v => v.PlaceCaret(Offset(text, "Line 7."), null),
            v => v.SetPending(new ViewEditResult.Pending(Offset(text, "Line 7.") + 7, string.Empty, PendingPlacement.Below, "> ")));

        Snap before = result.Snaps[0];
        Snap after = result.Snaps[^1];
        Assert.Equal(before.Top + 1, after.Top);
        Assert.Equal("  >", Rows(result.Screens[^1])[11].TrimEnd());
        Assert.Equal(new Point(4, 11), after.Cursor);
    }

    [Fact]
    public void A_caret_move_drops_a_below_pending_and_the_virtual_line_is_drawn_no_more()
    {
        RenderedView view = View(Pair);

        Result result = Run(
            view,
            v => v.PlaceCaret(0, null),
            v => v.SetPending(new ViewEditResult.Pending(0, string.Empty, PendingPlacement.Below, "> ")),
            v => v.HandleKey(Key.CursorRight));

        Assert.Null(view.Pending);
        string[] rows = Rows(result.Screens[^1]);
        Assert.Equal(string.Empty, rows[1].TrimEnd());
        Assert.Equal("  Other.", rows[2].TrimEnd());
    }

    [Fact]
    public void A_press_below_the_virtual_line_lands_on_the_line_drawn_there_and_drops_the_pending()
    {
        RenderedView view = View(Pair);

        Run(
            view,
            v => v.PlaceCaret(0, null),
            v => v.SetPending(new ViewEditResult.Pending(0, string.Empty, PendingPlacement.Below, "> ")),
            v => v.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new Point(2, 3) }));

        // Row 3 shows "Other." under the virtual line, so the press is on it, not on the separator above it.
        Assert.Null(view.Pending);
        Assert.Equal(Offset(Pair, "Other."), view.CursorOffset);
    }

    [Fact]
    public void The_end_of_a_line_ending_in_a_hard_break_is_a_caret_stop_and_the_caret_goes_to_it()
    {
        const string text = "First\\\nsecond.\n";
        RenderedView view = View(text);
        int end = Offset(text, "\\");

        Result result = Run(view, v => v.PlaceCaret(end, null));

        Assert.Equal(end, view.CursorOffset);
        Assert.Equal(new Point(7, 0), result.Snaps[^1].Cursor);
    }

    [Fact]
    public void The_right_arrow_at_a_hard_break_line_end_moves_to_the_next_line_s_first_character()
    {
        const string text = "First\\\nsecond.\n";
        RenderedView view = View(text);

        Run(view, v => v.PlaceCaret(Offset(text, "t"), null), v => v.HandleKey(Key.CursorRight), v => v.HandleKey(Key.CursorRight));

        Assert.Equal(Offset(text, "second"), view.CursorOffset);
    }
}
