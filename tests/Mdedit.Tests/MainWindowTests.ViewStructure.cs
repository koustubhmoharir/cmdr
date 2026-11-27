using Mdedit.Core.Editing;
using Mdedit.Tui;
using Terminal.Gui.Input;

namespace Mdedit.Tests;

/// <summary>
/// The structure edits of the view, in view mode: Enter splits a block or holds a new one below or above it (drawn on a virtual line), Backspace and
/// Delete join or unwrap blocks at their edges, the undo steps of these edits, and the empty document's caret.
/// </summary>
public sealed partial class MainWindowTests
{
    // A paragraph and a list, for splitting and for the virtual line that shows a new item's marker.
    private const string Two = "Some text here.\n";
    private const string Items = "- one\n- two\n";

    // A heading, and two paragraphs with a blank line between them, for the joins and the unwrap.
    private const string HeadingDoc = "# Title\n\nText.\n";
    private const string Paragraphs = "One.\n\nTwo.\n";

    [Fact]
    public void Enter_at_a_blocks_end_draws_the_line_below_and_a_caret_move_takes_it_away()
    {
        OpenedFile file = Opened("notes.md", Paragraphs);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Paragraphs, "One.") + 4, null),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => window.NewKeyDownEvent(Key.CursorLeft));

        // Once the caret moves, the virtual line is gone and the rows are as they were before Enter.
        Assert.Equal(Paragraphs, session.Window.Editor.Buffer.Text);
        Assert.Equal(Framed("  One."), session.Screens[^1].Text(1));
        Assert.Equal(Framed(string.Empty), session.Screens[^1].Text(2));
        Assert.Equal(Framed("  Two."), session.Screens[^1].Text(3));
    }

    [Fact]
    public void Typing_into_an_empty_document_writes_a_paragraph_and_Enter_does_nothing_there()
    {
        OpenedFile file = Opened("notes.md", string.Empty);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => window.NewKeyDownEvent(new Key('H')),
            (_, window) => window.NewKeyDownEvent(new Key('i')));

        Assert.Equal("Hi", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Enter_in_the_middle_of_a_paragraph_splits_it_into_two_and_the_caret_starts_the_second()
    {
        OpenedFile file = Opened("notes.md", Two);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Two, "here"), null),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => window.NewKeyDownEvent(new Key('X')));

        // The space before the caret stays in the source, where Markdown ignores it (the rendered text has none).
        Assert.Equal("Some text\n\nXhere.\n", session.Window.Editor.Buffer.Text);
        Assert.Equal(Framed("  Some text "), session.Screens[2].Text(1));
        Assert.Equal(Framed("  here."), session.Screens[2].Text(3));
        Assert.Equal(Framed("  Xhere."), session.Screens[^1].Text(3));
    }

    [Fact]
    public void Enter_at_the_end_of_a_list_item_draws_a_bullet_on_a_virtual_line_and_typing_writes_the_item()
    {
        OpenedFile file = Opened("notes.md", Items);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Items, "two") + 3, null),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => window.NewKeyDownEvent(new Key('x')));

        Assert.Equal("- one\n- two\n- x\n", session.Window.Editor.Buffer.Text);
        Assert.Equal(Framed("  • x"), session.Screens[^1].Text(3));
    }

    [Fact]
    public void Backspace_after_Enter_takes_back_the_new_item_and_deletes_nothing()
    {
        OpenedFile file = Opened("notes.md", Items);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Items, "two") + 3, null),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => window.NewKeyDownEvent(Key.Backspace),
            (_, window) => window.NewKeyDownEvent(new Key('x')));

        // The new item was never written, so Backspace only drops it: the text before it stays, and typing goes on where the caret was.
        Assert.Null(session.Window.View.Pending);
        Assert.Equal("- one\n- twox\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Enter_twice_at_the_end_of_a_list_item_ends_the_list_and_typing_starts_a_paragraph()
    {
        OpenedFile file = Opened("notes.md", Items);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Items, "two") + 3, null),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => window.NewKeyDownEvent(new Key('x')));

        Assert.Equal("- one\n- two\n\nx\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Enter_at_a_blocks_start_draws_the_new_line_above_and_typing_writes_a_paragraph_before_it()
    {
        OpenedFile file = Opened("notes.md", Prose);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(0, null),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => window.NewKeyDownEvent(new Key('x')));

        Assert.Equal("x\n\nSome text.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Backspace_at_a_headings_start_unwraps_it_into_a_paragraph()
    {
        OpenedFile file = Opened("notes.md", HeadingDoc);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(HeadingDoc, "Title"), null),
            (_, window) => window.NewKeyDownEvent(Key.Backspace));

        Assert.Equal("Title\n\nText.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Backspace_at_a_paragraphs_start_joins_it_to_the_paragraph_before()
    {
        OpenedFile file = Opened("notes.md", Paragraphs);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Paragraphs, "Two"), null),
            (_, window) => window.NewKeyDownEvent(Key.Backspace));

        Assert.Equal("One.Two.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Delete_at_a_paragraphs_end_joins_the_next_paragraph()
    {
        OpenedFile file = Opened("notes.md", Paragraphs);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Paragraphs, "One.") + 4, null),
            (_, window) => window.NewKeyDownEvent(Key.Delete));

        Assert.Equal("One.Two.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Delete_at_a_hard_break_line_end_joins_the_two_lines()
    {
        const string text = "First\\\nsecond.\n";
        OpenedFile file = Opened("notes.md", text);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(text, "\\"), null),
            (_, window) => window.NewKeyDownEvent(Key.Delete));

        Assert.Equal("Firstsecond.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Backspace_at_the_document_start_does_nothing_and_shows_no_message()
    {
        OpenedFile file = Opened("notes.md", Prose);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(0, null),
            (_, window) => window.NewKeyDownEvent(Key.Backspace));

        Assert.Equal(Prose, session.Window.Editor.Buffer.Text);
        // The message line is the same as before the key: an empty refusal shows nothing.
        Assert.Equal(session.Screens[^2].Text(10), session.Screens[^1].Text(10));
    }

    [Fact]
    public void Deleting_the_only_character_of_a_document_then_typing_writes_it_back()
    {
        OpenedFile file = Opened("notes.md", "a\n");

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(0, null),
            (_, window) => window.NewKeyDownEvent(Key.Delete),
            (_, window) => window.NewKeyDownEvent(new Key('b')));

        Assert.Equal("b\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Deleting_the_only_character_of_a_heading_then_typing_writes_the_heading_back()
    {
        OpenedFile file = Opened("notes.md", "# a\n");

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index("# a\n", "a") + 1, null),
            (_, window) => window.NewKeyDownEvent(Key.Backspace),
            (_, window) => window.NewKeyDownEvent(new Key('b')));

        // The removed heading keeps its marker in the pending block, so the typed text is a heading again.
        Assert.Equal("# b\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Enter_is_one_undo_step()
    {
        OpenedFile file = Opened("notes.md", Two);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Two, "here"), null),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl));

        Assert.Equal(Two, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Enter_on_a_link_does_not_follow_it()
    {
        OpenedFile file = Opened("notes.md", Linked);

        Session session = RunLinked(
            file,
            (_, window) => window.View.PlaceCaret(Index(Linked, "two") + 1, null),
            (_, window) => window.NewKeyDownEvent(Key.Enter));

        // The engine refuses a split inside a link's text (see the report of M6b), so Enter changes nothing and the link is not followed.
        Assert.Null(session.Window.SwitchTo);
        Assert.Equal(Index(Linked, "two") + 1, session.Window.View.CursorOffset);
        Assert.Equal(Linked, session.Window.Editor.Buffer.Text);
        Assert.StartsWith("Can't edit this here", session.Screens[^1].Text(10), StringComparison.Ordinal);
    }

    [Fact]
    public void Enter_in_a_table_cell_still_opens_the_field()
    {
        const string empty = "| a |  |\n|---|---|\n| 1 | 2 |\n\nAfter.\n";
        Session session = RunTable(
            Opened("notes.md", empty),
            (_, window) => Assert.True(window.View.PlaceTableCell(0, 0, 1)),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => Type(window, "Z"),
            (_, window) => window.NewKeyDownEvent(Key.Enter));

        Assert.StartsWith("| a | Z", session.Window.Editor.Buffer.Text.Split('\n')[0], StringComparison.Ordinal);
    }
}
