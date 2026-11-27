using Mdedit.Core.Editing;
using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Point = System.Drawing.Point;

namespace Mdedit.Tests;

/// <summary>
/// The pending insertion of the view: drawn at the caret, with the rest of the caret's line moved right and clipped at the edge, the cursor
/// after it, and dropped by a caret move, a new document or a fold change. Also which keys type text in the view.
/// </summary>
public sealed partial class RenderedViewTests
{
    // A paragraph whose first line is full at 40 columns (the paragraph is indented by two): a pending space at its start pushes its last letter off
    // the edge.
    private const string FullLine = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMN";

    [Fact]
    public void A_pending_insertion_is_drawn_at_the_caret_and_moves_the_rest_of_the_line_right()
    {
        RenderedView view = View("Some text.\n");

        Result result = Run(view, v => v.PlaceCaret(0, null), v => v.SetPending(new ViewEditResult.Pending(0, " ")));

        // The paragraph is indented by two columns, so the pending space is drawn after the indent, and the text moves right of it.
        Assert.Equal("   Some text.", Rows(result.Screens[3])[0].TrimEnd());
        // The cursor is drawn after the pending text: one column right of the caret's cell.
        Assert.Equal(new Point(3, 0), result.Snaps[1].Cursor);
    }

    [Fact]
    public void A_pending_insertion_at_the_edge_clips_the_line_and_overwrites_nothing()
    {
        RenderedView view = View(FullLine + "\n");

        Result result = Run(view, v => v.PlaceCaret(0, null), v => v.SetPending(new ViewEditResult.Pending(0, " ")));

        Assert.Equal("   " + FullLine[..37], Rows(result.Screens[3])[0]);
    }

    [Fact]
    public void A_caret_move_drops_the_pending_insertion_and_the_line_is_drawn_as_it_was()
    {
        RenderedView view = View("Some text.\n");

        Result result = Run(
            view,
            v => v.PlaceCaret(0, null),
            v => v.SetPending(new ViewEditResult.Pending(0, " ")),
            v => v.HandleKey(Key.CursorRight));

        Assert.Null(view.Pending);
        Assert.Equal("  Some text.", Rows(result.Screens[5])[0].TrimEnd());
    }

    [Fact]
    public void A_mouse_press_drops_the_pending_insertion_and_raises_CommandPressed()
    {
        RenderedView view = View("Some text.\n");
        int commands = 0;
        view.CommandPressed += () => commands++;

        Run(
            view,
            v => v.PlaceCaret(0, null),
            v => v.SetPending(new ViewEditResult.Pending(0, " ")),
            v => v.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new Point(3, 0) }));

        Assert.Null(view.Pending);
        Assert.Equal(1, commands);
    }

    [Fact]
    public void A_new_document_drops_the_pending_insertion()
    {
        RenderedView view = View("Some text.\n");

        Run(view, v => v.PlaceCaret(0, null), v => v.SetPending(new ViewEditResult.Pending(0, " ")), v => v.SetDocument(RenderDocument.Parse("Other.\n")));

        Assert.Null(view.Pending);
    }

    [Fact]
    public void A_fold_change_drops_the_pending_insertion()
    {
        RenderedView view = View(Sections);

        Run(view, v => v.PlaceCaret(0, null), v => v.SetPending(new ViewEditResult.Pending(0, " ")), v => v.ToggleFold(v.Units[0]));

        Assert.Null(view.Pending);
    }

    [Fact]
    public void Keys_without_Ctrl_or_Alt_type_their_printable_character_and_Shift_is_allowed()
    {
        Assert.True(RenderedView.IsTyping(new Key('a')));
        Assert.True(RenderedView.IsTyping(new Key('A')));
        Assert.True(RenderedView.IsTyping(new Key('/')));
        Assert.True(RenderedView.IsTyping(Key.Space));
    }

    [Fact]
    public void Editing_and_movement_keys_and_keys_with_Ctrl_or_Alt_do_not_type()
    {
        Assert.False(RenderedView.IsTyping(Key.Enter));
        Assert.False(RenderedView.IsTyping(Key.Tab));
        Assert.False(RenderedView.IsTyping(Key.Backspace));
        Assert.False(RenderedView.IsTyping(Key.CursorLeft));
        Assert.False(RenderedView.IsTyping(new Key('a').WithCtrl));
        Assert.False(RenderedView.IsTyping(Key.F.WithAlt));
        Assert.False(RenderedView.IsTyping(Key.C.WithAlt));
    }

    [Fact]
    public void Ctrl_with_Alt_types_a_printable_non_letter_as_AltGr_does()
    {
        const KeyCode altGr = KeyCode.Q | KeyCode.CtrlMask | KeyCode.AltMask;

        Assert.True(RenderedView.IsTyping(new Key(altGr) { AssociatedText = "@" }));
        Assert.False(RenderedView.IsTyping(new Key(altGr) { AssociatedText = "q" }));
    }
}
