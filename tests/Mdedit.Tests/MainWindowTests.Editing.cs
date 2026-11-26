using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Mdedit.Tests;

/// <summary>
/// Edit mode in the window: Ctrl+T in both directions with the caret and the selection carried, Esc, typing and the re-rendering on the way
/// back, undo and redo in view mode, the title's dirty mark and mode, the hints per mode, Alt+Z, the keys edit mode leaves alone, folds and
/// table scroll offsets that survive an edit, CRLF text, and a large file that opens in edit mode and asks before it renders.
/// </summary>
public sealed partial class MainWindowTests
{
    // A paragraph with emphasis, for the markup landing: the first '*' of "**bold**" is at 4.
    private const string Emphasis = "Say **bold** word.\n";

    // A heading and a paragraph, for typing at the start of the document.
    private const string Heading = "# Title\n\nBody.\n";

    // The table that the view scrolls, after a line of text.
    private const string Wide =
        "| Abcdefghijklmnopqrst | Bcdefghijklmnopqrstu | Cdefghijklmnopqrstuv |\n|---|---|---|\n| one | two | three |\n\nAfter the table.\n";

    [Fact]
    public void Ctrl_T_from_view_puts_the_editor_caret_on_the_source_offset_of_the_view_caret()
    {
        const string text = "Intro line.\n\nSome **bold** word here.\n";
        OpenedFile file = Opened("notes.md", text);
        int caret = -1;

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(text, "word"), null),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => caret = window.Editor.CaretOffset);

        Assert.True(session.Window.InEditMode);
        Assert.Equal(Index(text, "word"), caret);
        Assert.Contains("notes.md [edit]", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void The_edit_mode_status_shows_the_carets_line_and_column_and_follows_the_caret()
    {
        const string text = "Intro line.\n\nSome **bold** word here.\n";
        OpenedFile file = Opened("notes.md", text);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(text, "word"), null),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.CursorRight));

        // Row 10 is the message line at 40x12: "word" is line 3, column 15; one step right is column 16.
        Assert.EndsWith("3:15", session.Screens[^2].Text(10).TrimEnd(), StringComparison.Ordinal);
        Assert.EndsWith("3:16", session.Screens[^1].Text(10).TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_view_selection_becomes_the_editor_selection_and_comes_back_as_the_view_selection()
    {
        const string text = "Intro line.\n\nSome **bold** word here.\n";
        int start = Index(text, "word");
        OpenedFile file = Opened("notes.md", text);
        (int Start, int End, int Caret) inEditor = default;
        (int? Anchor, SourceRange? Range, int Cursor) inView = default;
        // The view's caret is at the end of the selection: the editor's selection can hold the same range with its caret there.

        Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(start + 4, start),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => inEditor = (window.Editor.SelectionStart, window.Editor.SelectionEnd, window.Editor.CaretOffset),
            (_, window) => window.NewKeyDownEvent(Key.Esc),
            (_, window) => inView = (window.View.SelectionAnchor, window.View.SelectionRange, window.View.CursorOffset));

        Assert.Equal((start, start + 4, start + 4), inEditor);
        Assert.Equal((start, new SourceRange(start, start + 4), start + 4), inView);
    }

    [Fact]
    public void Typing_in_edit_mode_then_Ctrl_T_shows_the_new_text_rendered_with_the_caret_after_it()
    {
        OpenedFile file = Opened("notes.md", Heading);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => Type(window, "Big "),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        const string edited = "# Big Title\n\nBody.\n";
        Assert.Equal(edited, session.Window.Editor.Buffer.Text);
        Assert.Equal(Framed("▾ Big Title"), session.Screens[^1].Text(1));
        Assert.Equal(Index(edited, "Title"), session.Window.View.CursorOffset);
    }

    [Fact]
    public void Edit_to_view_with_the_caret_on_markup_lands_on_the_next_visible_character()
    {
        OpenedFile file = Opened("notes.md", Emphasis);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.Editor.CaretOffset = Index(Emphasis, "**"),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        Assert.Equal(Index(Emphasis, "bold"), session.Window.View.CursorOffset);
    }

    [Fact]
    public void Edit_to_view_inside_a_folded_section_unfolds_it_and_puts_the_caret_in_it()
    {
        OpenedFile file = Opened("notes.md", Sectioned);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.ToggleFold(window.View.Units[0]),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.Editor.CaretOffset = Index(Sectioned, "First"),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        Assert.False(session.Window.View.IsFolded(session.Window.View.Units[0]));
        Assert.Equal(Framed("▾ Title"), session.Screens[^1].Text(1));
        Assert.Equal(Index(Sectioned, "First"), session.Window.View.CursorOffset);
    }

    [Fact]
    public void Esc_in_edit_mode_returns_to_view_mode()
    {
        OpenedFile file = Opened("notes.md", Small);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.Esc));

        Assert.False(session.Window.InEditMode);
        Assert.False(session.Window.Stopped);
        Assert.DoesNotContain("[edit]", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Undo_in_view_mode_after_an_edit_restores_the_old_rendering_and_puts_the_caret_at_the_change()
    {
        OpenedFile file = Opened("notes.md", Heading);

        var afterUndo = new List<int>();
        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => Type(window, "X"),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl),
            (_, window) => afterUndo.Add(window.View.CursorOffset),
            (_, window) => window.NewKeyDownEvent(Key.Y.WithCtrl),
            (_, window) => afterUndo.Add(window.View.CursorOffset));

        Assert.Equal(Framed("▾ XTitle"), session.Screens[3].Text(1));
        Assert.Equal(Framed("▾ Title"), session.Screens[4].Text(1));
        Assert.Equal(Framed("▾ XTitle"), session.Screens[^1].Text(1));
        // Undo puts the caret where the removed X was; redo puts it after the restored X.
        Assert.Equal([2, 3], afterUndo);
    }

    [Fact]
    public void Ctrl_Z_in_view_mode_with_nothing_to_undo_changes_nothing()
    {
        OpenedFile file = Opened("notes.md", Heading);

        Session session = Run(file, 40, 12, (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl));

        Assert.Equal(session.Screens[0].Text(1), session.Screens[^1].Text(1));
        Assert.Equal(Heading, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void The_title_shows_a_star_while_the_text_differs_from_the_file_and_loses_it_when_undo_restores_the_file()
    {
        OpenedFile file = Opened("notes.md", Heading);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => Type(window, "X"),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl));

        Assert.Contains("notes.md * [edit]", session.Screens[2].Text(0), StringComparison.Ordinal);
        Assert.Contains("notes.md *", session.Screens[3].Text(0), StringComparison.Ordinal);
        Assert.DoesNotContain("[edit]", session.Screens[3].Text(0), StringComparison.Ordinal);
        Assert.Contains("notes.md", session.Screens[^1].Text(0), StringComparison.Ordinal);
        Assert.DoesNotContain("*", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void The_hints_are_the_view_set_and_the_edit_set_with_Quit_at_the_right()
    {
        OpenedFile file = Opened("notes.md", Small);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        Assert.Equal(Status("^T View  ^S Save  Alt+Z Wrap", "^Q Quit"), session.Screens[1].Text(11));
        Assert.Equal(Status("^T Edit  ^S Save  / Find  y Copy…", "^Q Quit"), session.Screens[^1].Text(11));
    }

    [Fact]
    public void Alt_Z_in_edit_mode_toggles_word_wrap_and_types_nothing()
    {
        OpenedFile file = Opened("notes.md", Small);
        var wrap = new List<bool>();

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithAlt),
            (_, window) => wrap.Add(window.Editor.WordWrap),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithAlt),
            (_, window) => wrap.Add(window.Editor.WordWrap));

        Assert.Equal([false, true], wrap);
        Assert.Equal(Small, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void A_slash_types_in_edit_mode_and_Ctrl_F_opens_nothing()
    {
        OpenedFile file = Opened("notes.md", "Body.\n");

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(new Key('/')),
            (_, window) => window.NewKeyDownEvent(Key.F.WithCtrl));

        Assert.Equal("/Body.\n", session.Window.Editor.Buffer.Text);
        Assert.False(session.Window.Search.IsOpen);
        Assert.False(session.Window.Search.Bar.Visible);
    }

    [Fact]
    public void Ctrl_Z_in_edit_mode_is_the_editor_undo_and_leaves_the_view_alone()
    {
        OpenedFile file = Opened("notes.md", Heading);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => Type(window, "X"),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl));

        Assert.Equal(Heading, session.Window.Editor.Buffer.Text);
        Assert.Contains("notes.md [edit]", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void A_fold_survives_a_paragraph_inserted_above_its_section_and_is_still_folded_at_its_moved_place()
    {
        OpenedFile file = Opened("notes.md", Sectioned);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.ToggleFold(window.View.Units[0]),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.Editor.CaretOffset = 0,
            (_, window) =>
            {
                Type(window, "New paragraph.");
                window.NewKeyDownEvent(Key.Enter);
                window.NewKeyDownEvent(Key.Enter);
            },
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        const string edited = "New paragraph.\n\n" + Sectioned;
        RenderedView view = session.Window.View;
        Assert.True(view.IsFolded(view.Units[0]));
        Assert.Equal(Index(edited, "# Title"), view.Units[0].Start);
        Assert.Equal(Framed("New paragraph."), session.Screens[^1].Text(1));
        Assert.Equal(Framed("▸ Title … 6 lines"), session.Screens[^1].Text(3));
    }

    [Fact]
    public void A_table_scroll_offset_survives_text_inserted_above_the_table()
    {
        OpenedFile file = Opened("notes.md", Wide);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Wide, "Bcdefghij"), null),
            (_, window) => window.NewKeyDownEvent((Key)'>'),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.Editor.CaretOffset = 0,
            (_, window) =>
            {
                Type(window, "Extra");
                window.NewKeyDownEvent(Key.Enter);
                window.NewKeyDownEvent(Key.Enter);

                // The caret goes after the table: a caret on its first cell would scroll it into view.
                window.Editor.CaretOffset = window.Editor.Buffer.TextLength;
            },
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        string before = session.Screens[2].Text(session.Screens[2].RowOf("◂"));
        string after = session.Screens[^1].Text(session.Screens[^1].RowOf("◂"));
        Assert.Contains("◂", before, StringComparison.Ordinal);
        Assert.Equal(before, after);
    }

    [Fact]
    public void A_CRLF_file_opens_with_its_text_exact_and_Enter_inserts_CRLF_and_the_text_is_kept_exactly()
    {
        const string crlf = "# Title\r\n\r\nBody.\r\n";
        OpenedFile file = Opened("notes.md", crlf);
        string opened = string.Empty;

        Session session = Run(
            file,
            40,
            12,
            (_, window) => opened = window.Editor.Buffer.Text,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.Editor.CaretOffset = 7,
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        Assert.Equal(crlf, opened);
        Assert.Equal("# Title\r\n\r\n\r\nBody.\r\n", session.Window.Editor.Buffer.Text);
        Assert.Equal(Framed("▾ Title"), session.Screens[^1].Text(1));
    }

    [Fact]
    public void A_large_file_opens_in_edit_mode_with_the_message_and_Ctrl_T_asks_before_it_renders()
    {
        OpenedFile file = Opened("big.md", Heading);
        var question = new List<string>();

        Session session = RunLarge(
            file,
            limit: 4,
            (app, window) =>
            {
                using var prompt = new DialogWatch(app, "the large-file question", IsDialogTitled("Large file"), v => question.Add(MessageOf(v)), "Render");
                window.NewKeyDownEvent(Key.T.WithCtrl);
                prompt.Finish();
            });

        Assert.StartsWith(MainWindow.LargeFileOpened, session.Screens[0].Text(10), StringComparison.Ordinal);
        Assert.Contains("[edit]", session.Screens[0].Text(0), StringComparison.Ordinal);
        Assert.Equal(["Render 0 MB?"], question);
        Assert.False(session.Window.InEditMode);
        Assert.Equal(Framed("▾ Title"), session.Screens[^1].Text(1));
    }

    [Fact]
    public void Cancelling_the_large_file_question_keeps_edit_mode()
    {
        OpenedFile file = Opened("big.md", Heading);

        Session session = RunLarge(
            file,
            limit: 4,
            (app, window) =>
            {
                using var prompt = new DialogWatch(app, "the large-file question", IsDialogTitled("Large file"), null, "Cancel");
                window.NewKeyDownEvent(Key.T.WithCtrl);
                prompt.Finish();
            });

        Assert.True(session.Window.InEditMode);
        Assert.Contains("[edit]", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void A_caret_far_below_the_top_is_on_screen_after_Ctrl_T_and_after_the_way_back()
    {
        OpenedFile file = Opened("notes.md", Long);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Long, "item f"), null),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.Editor.CaretOffset = Index(Long, "item a"),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        // The editor scrolls to the caret when it is set, so the caret's line is drawn; the view shows the caret again after the way back.
        Assert.Contains("item f", Body(session.Screens[2]), StringComparison.Ordinal);
        Assert.Contains("item a", Body(session.Screens[^1]), StringComparison.Ordinal);
        Assert.Equal(Index(Long, "item a"), session.Window.View.CursorOffset);
    }

    [Fact]
    public void The_large_file_limit_is_two_megabytes_on_disk()
    {
        Assert.Equal(2L * 1024 * 1024, MainWindow.LargeFileBytes);
    }

    // ----- Helpers -----

    private static int Index(string text, string find)
    {
        int index = text.IndexOf(find, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new ArgumentException($"'{find}' is not in the text", nameof(find));
        }

        return index;
    }

    private static Func<View, bool> IsDialogTitled(string title) => view => view is Dialog<int> dialog && dialog.Title == title;

    // The message of a question: the dialog's text, as cfgedit's tests read it.
    private static string MessageOf(View dialog) => dialog.Text;

    // The text rows of a 40 by 12 window: the rows between the frame's border and the message line.
    private static string Body(TerminalScreen screen) => string.Concat(Enumerable.Range(1, 9).Select(row => screen.Text(row)));

    // Runs the window with a lowered size limit, so that a small file counts as large.
    private static Session RunLarge(OpenedFile file, long limit, params Action<IApplication, MainWindow>[] steps)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(40, 12);
        using var window = new MainWindow(file, () => (PickOutcome.Back, null), (_, _) => () => { }, limit);
        List<TerminalScreen> screens = TerminalHarness.RunWindow(
            app,
            window,
            40,
            12,
            steps.Select(step => (Action)(() => step(app, window))).ToArray());
        return new Session(screens, window);
    }

    // Watches for a dialog that matches: when it opens, it runs the action, then presses the button when one is given.
    private sealed class DialogWatch : IDisposable
    {
        private readonly IApplication _app;
        private readonly object? _timer;
        private readonly string _waitingFor;
        private bool _opened;
        private System.Runtime.ExceptionServices.ExceptionDispatchInfo? _failure;

        /// <summary>True once the dialog matched and opened.</summary>
        public bool Opened => _opened;

        public DialogWatch(IApplication app, string waitingFor, Func<View, bool> matches, Action<View>? whileOpen, string? button)
        {
            _app = app;
            _waitingFor = waitingFor;
            _timer = app.AddTimeout(TimeSpan.FromMilliseconds(20), () =>
            {
                if (_opened || app.TopRunnableView is not { } top || !matches(top))
                {
                    return true;
                }

                _opened = true;
                try
                {
                    whileOpen?.Invoke(top);
                }
                catch (Exception ex)
                {
                    _failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
                }

                if (button is not null)
                {
                    ButtonsOf(top).Single(b => b.Text.TrimStart('_') == button).InvokeCommand(Command.Accept);
                }

                return false;
            });
        }

        /// <summary>Stops the watch, rethrows what it failed with, and asserts that the dialog opened.</summary>
        public void Finish()
        {
            Dispose();
            _failure?.Throw();
            Assert.True(_opened, $"{_waitingFor} did not open");
        }

        public void Dispose()
        {
            if (_timer is not null)
            {
                _app.RemoveTimeout(_timer);
            }
        }

        private static IEnumerable<Button> ButtonsOf(View view) => view switch
        {
            Dialog<int> dialog => dialog.Buttons,
            _ => [],
        };
    }
}
