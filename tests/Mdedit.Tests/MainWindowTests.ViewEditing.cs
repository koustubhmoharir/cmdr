using Mdedit.Core.Editing;
using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Mdedit.Tests;

/// <summary>
/// Editing in the view, in view mode: typed characters, Backspace and Delete as edits of the text, a selection replaced or deleted, the pending
/// space, the undo steps of typing and of Backspace, the refusals, the dirty mark, and the keys that are text in the view now (a letter types,
/// Alt+F folds, Ctrl+Enter follows a link, Tab moves between cells).
/// </summary>
public sealed partial class MainWindowTests
{
    // A paragraph with bold text: "bold" is at 7 to 10, its closing stars at 11 and 12, the space after the span is at 13, and "word" is at 14.
    private const string Strong = "Some **bold** word here.\n";

    // A paragraph with plain text, for the pending space at its start and at its end.
    private const string Prose = "Some text.\n";

    [Fact]
    public void Typing_before_the_space_after_bold_continues_the_bold()
    {
        OpenedFile file = Opened("notes.md", Strong);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(13, null),
            (_, window) => window.NewKeyDownEvent(new Key('X')));

        // The caret is after the bold text, and the typed character takes its style: it goes in before the closing stars.
        Assert.Equal("Some **boldX** word here.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Typing_in_a_table_cell_writes_the_cell()
    {
        OpenedFile file = Opened("notes.md", Wide);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Wide, "one"), null),
            (_, window) => window.NewKeyDownEvent(new Key('x')));

        // The table is not aligned, so only the cell changes.
        Assert.Equal(Wide.Replace("| one |", "| xone |", StringComparison.Ordinal), session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Backspace_deletes_the_character_before_the_caret()
    {
        OpenedFile file = Opened("notes.md", Strong);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Strong, "word"), null),
            (_, window) => window.NewKeyDownEvent(Key.Backspace));

        Assert.Equal("Some **bold**word here.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Delete_deletes_the_character_after_the_caret()
    {
        OpenedFile file = Opened("notes.md", Strong);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Strong, "word"), null),
            (_, window) => window.NewKeyDownEvent(Key.Delete));

        Assert.Equal("Some **bold** ord here.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Typing_over_a_selection_replaces_it()
    {
        OpenedFile file = Opened("notes.md", Strong);
        int word = Index(Strong, "word");

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.SelectRange(new SourceRange(word, word + 4)),
            (_, window) => window.NewKeyDownEvent(new Key('X')));

        Assert.Equal("Some **bold** X here.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Backspace_over_a_selection_deletes_it()
    {
        OpenedFile file = Opened("notes.md", Strong);
        int word = Index(Strong, "word");

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.SelectRange(new SourceRange(word, word + 4)),
            (_, window) => window.NewKeyDownEvent(Key.Backspace));

        Assert.Equal("Some **bold**  here.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void A_space_at_the_start_of_a_paragraph_is_drawn_as_pending_and_moves_the_line_right()
    {
        OpenedFile file = Opened("notes.md", Prose);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(0, null),
            (_, window) => window.NewKeyDownEvent(Key.Space));

        // Nothing is written, and the line is drawn with the space before it.
        Assert.Equal(Prose, session.Window.Editor.Buffer.Text);
        Assert.Equal(Framed("   Some text."), session.Screens[^1].Text(1));
    }

    [Fact]
    public void The_pending_space_is_written_with_the_next_character()
    {
        OpenedFile file = Opened("notes.md", Prose);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(10, null),
            (_, window) => window.NewKeyDownEvent(Key.Space),
            (_, window) => window.NewKeyDownEvent(new Key('a')));

        Assert.Equal("Some text. a\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Moving_the_caret_drops_the_pending_space()
    {
        OpenedFile file = Opened("notes.md", Prose);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(10, null),
            (_, window) => window.NewKeyDownEvent(Key.Space),
            (_, window) => window.NewKeyDownEvent(Key.CursorLeft),
            (_, window) => window.NewKeyDownEvent(Key.CursorRight),
            (_, window) => window.NewKeyDownEvent(new Key('a')));

        Assert.Equal("Some text.a\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Ctrl_Z_with_a_pending_space_drops_it_and_undoes_nothing()
    {
        OpenedFile file = Opened("notes.md", Prose);
        var texts = new List<string>();

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(10, null),
            (_, window) => window.NewKeyDownEvent(new Key('x')),
            (_, window) => window.NewKeyDownEvent(Key.Space),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl),
            (_, window) => texts.Add(window.Editor.Buffer.Text),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl),
            (_, window) => texts.Add(window.Editor.Buffer.Text));

        // The first undo drops the pending space and keeps the typed x; the second takes the x away.
        Assert.Equal(new[] { "Some text.x\n", Prose }, texts);
        Assert.Equal(Prose, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void A_typed_run_is_one_undo_step()
    {
        OpenedFile file = Opened("notes.md", Strong);
        var texts = new List<string>();

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Strong, "word"), null),
            (_, window) => Type(window, "abc"),
            (_, window) => texts.Add(window.Editor.Buffer.Text),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl),
            (_, window) => texts.Add(window.Editor.Buffer.Text));

        Assert.Equal(new[] { "Some **bold** abcword here.\n", Strong }, texts);
        Assert.Equal(Strong, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void A_word_and_the_space_after_it_are_one_undo_step_and_the_next_word_starts_another()
    {
        OpenedFile file = Opened("notes.md", Strong);
        var texts = new List<string>();

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Strong, "word"), null),
            (_, window) => Type(window, "ab cd"),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl),
            (_, window) => texts.Add(window.Editor.Buffer.Text),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl),
            (_, window) => texts.Add(window.Editor.Buffer.Text));

        // The undo of "cd" leaves "ab " (the word and its following space were one step), and the second undo takes "ab " away.
        Assert.Equal(new[] { "Some **bold** ab word here.\n", Strong }, texts);
        Assert.Equal(Strong, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void A_run_of_Backspaces_is_one_undo_step()
    {
        OpenedFile file = Opened("notes.md", Strong);
        var texts = new List<string>();

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Strong, "word") + 4, null),
            (_, window) => window.NewKeyDownEvent(Key.Backspace),
            (_, window) => window.NewKeyDownEvent(Key.Backspace),
            (_, window) => texts.Add(window.Editor.Buffer.Text),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl),
            (_, window) => texts.Add(window.Editor.Buffer.Text));

        Assert.Equal(new[] { "Some **bold** wo here.\n", Strong }, texts);
        Assert.Equal(Strong, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Typing_and_then_Backspace_are_separate_undo_steps()
    {
        OpenedFile file = Opened("notes.md", Strong);
        var texts = new List<string>();

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Strong, "word"), null),
            (_, window) => window.NewKeyDownEvent(new Key('x')),
            (_, window) => window.NewKeyDownEvent(Key.Backspace),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl),
            (_, window) => texts.Add(window.Editor.Buffer.Text),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl),
            (_, window) => texts.Add(window.Editor.Buffer.Text));

        Assert.Equal(new[] { "Some **bold** xword here.\n", Strong }, texts);
        Assert.Equal(Strong, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void The_title_is_marked_dirty_after_typing_and_clean_after_the_undo()
    {
        OpenedFile file = Opened("notes.md", Strong);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Strong, "word"), null),
            (_, window) => window.NewKeyDownEvent(new Key('x')),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl));

        Assert.Contains("notes.md *", session.Screens[2].Text(0), StringComparison.Ordinal);
        Assert.DoesNotContain("*", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData('y')]
    [InlineData('Y')]
    [InlineData('/')]
    [InlineData('n')]
    [InlineData('N')]
    [InlineData('g')]
    [InlineData('G')]
    [InlineData('o')]
    [InlineData(' ')]
    [InlineData('0')]
    [InlineData('1')]
    [InlineData('<')]
    [InlineData('>')]
    public void A_letter_digit_or_mark_typed_in_the_view_is_text_and_no_longer_a_command(char typed)
    {
        OpenedFile file = Opened("notes.md", Strong);
        const string tail = "word here.\n";

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Strong, "word"), null),
            (_, window) => window.NewKeyDownEvent(new Key(typed)));

        string text = session.Window.Editor.Buffer.Text;
        Assert.StartsWith("Some **bold** ", text, StringComparison.Ordinal);
        Assert.EndsWith(tail, text, StringComparison.Ordinal);
        string inserted = text[14..^tail.Length];
        Assert.Contains(typed.ToString(), inserted, StringComparison.Ordinal);
        Assert.False(session.Window.Search.IsOpen);
    }

    [Fact]
    public void Alt_F_folds_the_unit_under_the_caret_in_view_mode()
    {
        OpenedFile file = Opened("notes.md", Small);

        Session session = Run(file, 40, 12, (_, window) => window.NewKeyDownEvent(RenderedView.ToggleFoldKey));

        Assert.True(session.Window.View.IsFolded(session.Window.View.Units[0]));
    }

    [Fact]
    public void Alt_1_folds_the_sections_below_level_one()
    {
        OpenedFile file = Opened("notes.md", Small);

        Session session = Run(file, 40, 12, (_, window) => window.NewKeyDownEvent(Key.D1.WithAlt));

        Assert.False(session.Window.View.IsFolded(session.Window.View.Units[0]));
        Assert.True(session.Window.View.IsFolded(session.Window.View.Units[1]));
    }

    [Fact]
    public void Ctrl_Enter_follows_the_link_at_the_caret()
    {
        OpenedFile file = Opened("notes.md", Linked);

        Session session = RunLinked(
            file,
            (_, window) => window.View.PlaceCaret(Index(Linked, "two"), null),
            (_, window) => window.NewKeyDownEvent(Key.Enter.WithCtrl));

        Assert.Equal(Index(Linked, "Guide"), session.Window.View.CursorOffset);
    }

    [Fact]
    public void Ctrl_J_follows_the_link_at_the_caret_as_Ctrl_Enter_does()
    {
        OpenedFile file = Opened("notes.md", Linked);

        Session session = RunLinked(
            file,
            (_, window) => window.View.PlaceCaret(Index(Linked, "two"), null),
            (_, window) => window.NewKeyDownEvent(Key.J.WithCtrl));

        Assert.Equal(Index(Linked, "Guide"), session.Window.View.CursorOffset);
    }

    [Fact]
    public void Tab_moves_to_the_next_cell_of_the_table_and_Shift_Tab_to_the_previous_one()
    {
        OpenedFile file = Opened("notes.md", Wide);
        var cells = new List<RenderedView.TableCell?>();

        Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceTableCell(0, 0, 0),
            (_, window) => window.NewKeyDownEvent(Key.Tab),
            (_, window) => cells.Add(window.View.TableCellAtCaret()),
            (_, window) => window.NewKeyDownEvent(Key.Tab.WithShift),
            (_, window) => cells.Add(window.View.TableCellAtCaret()));

        Assert.Equal(new RenderedView.TableCell?[] { new(0, 0, 1), new(0, 0, 0) }, cells);
    }

    [Fact]
    public void Tab_past_the_last_cell_of_a_table_goes_to_the_next_link_and_says_No_links_when_there_is_none()
    {
        OpenedFile file = Opened("notes.md", Wide);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceTableCell(0, 1, 2),
            (_, window) => window.NewKeyDownEvent(Key.Tab));

        Assert.Equal("No links", session.Screens[^1].Text(10));
    }

    [Fact]
    public void A_caret_move_ends_the_typing_run_so_each_run_is_its_own_undo_step()
    {
        OpenedFile file = Opened("notes.md", Strong);
        var texts = new List<string>();

        Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Strong, "word"), null),
            (_, window) => Type(window, "x"),
            (_, window) => window.View.PlaceCaret(Index(Strong, "here"), null),
            (_, window) => Type(window, "y"),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl),
            (_, window) => texts.Add(window.Editor.Buffer.Text),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl),
            (_, window) => texts.Add(window.Editor.Buffer.Text));

        // The first undo takes away the y typed after the caret move, and the second takes away the x.
        Assert.Equal(new[] { "Some **bold** xword here.\n", Strong }, texts);
    }

    [Fact]
    public void Typing_on_a_folded_heading_is_refused_with_its_message_and_works_once_it_is_unfolded()
    {
        OpenedFile file = Opened("notes.md", Small);

        Session session = Run(
            file,
            60,
            12,
            (_, window) => window.NewKeyDownEvent(RenderedView.ToggleFoldKey),
            (_, window) => window.NewKeyDownEvent(new Key('x')),
            (_, window) => window.NewKeyDownEvent(RenderedView.ToggleFoldKey),
            (_, window) => window.NewKeyDownEvent(new Key('x')));

        Assert.StartsWith(MainWindow.FoldedHeadRefusal, session.Screens[2].Text(10), StringComparison.Ordinal);
        Assert.Equal("# xInstall\n\nIntro text.\n\n## Linux\n\n- step one\n- step two\n", session.Window.Editor.Buffer.Text);
    }
}
