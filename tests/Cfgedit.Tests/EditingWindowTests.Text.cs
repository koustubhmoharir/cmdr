using System.Text;
using Cfgedit.Core;
using Cfgedit.Core.Files;
using Cfgedit.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Cfgedit.Tests;

/// <summary>
/// Text mode, as drawn and as driven through the window: a file that does not parse opens here, F5 and Ctrl+T reparse, Ctrl+T switches both
/// ways with the caret mapped, Ctrl+S asks about text that does not parse, Ctrl+Q asks about unsaved text, and Enter and pastes insert the
/// file's own line breaks, also in files with mixed line endings, which are editable and saved byte for byte.
/// </summary>
public sealed partial class EditingWindowTests
{
    private const string TextHints = "F5 Parse  ^T Tree  ^S Save";

    // A broken file with its error at line 2, column 10: the second string, where a colon is expected.
    private const string BrokenText = "{\n  \"name\" \"web\"\n}";

    // The error's caret: line 2 starts at offset 2, and column 10 is index 9 of that line, the quote that opens "web".
    private const int ErrorCaret = 11;

    private static string OnDisk(OpenedFile file) => Encoding.UTF8.GetString(File.ReadAllBytes(file.Path));

    private static string ViewText(Session session) => session.Window.TextView.Text;

    private static int CaretOffset(Session session) => session.Window.TextView.CaretOffset;

    // The text the controller holds in text mode: what the next save writes.
    private static string ControllerText(Session session) => session.Window.Controller.Text!.Text;

    private static string DetailRow(TerminalScreen screen) => screen.Text(22).Trim();

    private static string ErrorMessageOf(OpenedFile file)
    {
        CfgeditException error = file.File.ParseError ?? throw new InvalidOperationException("The file parses.");
        return error.Message;
    }

    [Fact]
    public void A_file_that_does_not_parse_opens_in_text_mode_with_the_cursor_at_the_error_and_the_message()
    {
        OpenedFile file = Open("settings.json", BrokenText);

        Session session = Run(file, 80, 24, null, null);

        TerminalScreen screen = session.Screens[^1];
        Assert.Contains("[text]", screen.Text(0), StringComparison.Ordinal);
        Assert.Equal("2:10: " + ErrorMessageOf(file), DetailRow(screen));
        Assert.Equal(ErrorCaret, CaretOffset(session));
        AssertHints(screen, TextHints);
        Assert.Equal(BrokenText, ViewText(session));
    }

    [Fact]
    public void Esc_does_nothing_in_text_mode()
    {
        OpenedFile file = Open("settings.json", BrokenText);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.Esc));

        TerminalScreen screen = session.Screens[^1];
        Assert.Contains("[text]", screen.Text(0), StringComparison.Ordinal);
        AssertHints(screen, TextHints);
        Assert.Equal(BrokenText, ViewText(session));
        Assert.False(session.Window.Stopped);
    }

    [Fact]
    public void F5_on_a_text_that_still_does_not_parse_stays_in_text_mode_with_the_error_at_the_cursor()
    {
        OpenedFile file = Open("settings.json", BrokenText);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.F5));

        TerminalScreen screen = session.Screens[^1];
        Assert.Contains("[text]", screen.Text(0), StringComparison.Ordinal);
        Assert.Equal("2:10: " + ErrorMessageOf(file), DetailRow(screen));
        Assert.Equal(ErrorCaret, CaretOffset(session));
    }

    [Fact]
    public void F5_after_a_broken_file_is_fixed_shows_the_tree_and_the_fix_is_not_an_undo_step()
    {
        OpenedFile file = Open("settings.json", BrokenText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Type(w, ":"),
            (_, w) => Keys(w, Key.F5),
            (_, w) => Keys(w, Key.Z.WithCtrl));

        TerminalScreen tree = session.Screens[^2];
        Assert.Contains("name: web", Drawn(tree), StringComparison.Ordinal);
        AssertHints(tree, TreeHints);
        Assert.Equal("Nothing to undo", DetailRow(session.Screens[^1]));
        Assert.Equal(BrokenText, OnDisk(file));
    }

    [Fact]
    public void A_text_edit_that_parses_is_one_undo_step_of_the_tree()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.T.WithCtrl),
            (_, w) => Keys(w, Key.CursorUp, Key.CursorUp, Key.CursorUp, Key.CursorUp, Key.CursorDown, Key.CursorDown, Key.End),
            (_, w) => Type(w, ", \"x\": 1"),
            (_, w) => Keys(w, Key.F5),
            (_, w) => Keys(w, Key.Z.WithCtrl));

        TerminalScreen undone = session.Screens[^1];
        Assert.Equal(JsonText, Text(session));
        Assert.DoesNotContain("*", undone.Text(0), StringComparison.Ordinal);
        AssertHints(undone, TreeHints);
        Assert.Equal(JsonText, OnDisk(file));
    }

    [Fact]
    public void F5_failing_then_succeeding_returns_the_tree_with_the_text_as_it_was_before_the_failure()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.T.WithCtrl),
            (_, w) => Keys(w, Key.CursorUp, Key.CursorUp, Key.CursorUp, Key.CursorUp, Key.CursorDown, Key.CursorDown, Key.End),
            (_, w) => Type(w, ", \"x\" 1"),
            (_, w) => Keys(w, Key.F5),
            (_, w) => Keys(w, Key.End, Key.Backspace, Key.Backspace, Key.Backspace, Key.Backspace, Key.Backspace, Key.Backspace, Key.Backspace),
            (_, w) => Keys(w, Key.F5));

        Assert.StartsWith("3:", DetailRow(session.Screens[4]));
        TerminalScreen tree = session.Screens[^1];
        Assert.Contains("port: 8080", Drawn(tree), StringComparison.Ordinal);
        AssertHints(tree, TreeHints);
        Assert.Equal(JsonText, Text(session));
    }

    [Fact]
    public void Ctrl_T_on_a_file_that_never_parsed_behaves_like_F5()
    {
        OpenedFile file = Open("settings.json", BrokenText);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.T.WithCtrl));

        TerminalScreen screen = session.Screens[^1];
        Assert.Contains("[text]", screen.Text(0), StringComparison.Ordinal);
        Assert.Equal("2:10: " + ErrorMessageOf(file), DetailRow(screen));
    }

    [Fact]
    public void Ctrl_T_in_the_tree_puts_the_focused_part_in_the_text_and_Ctrl_T_back_focuses_the_node_under_the_cursor()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorDown),
            (_, w) => Keys(w, Key.T.WithCtrl),
            (_, w) => Keys(w, Key.CursorUp, Key.CursorUp, Key.CursorUp, Key.CursorUp, Key.CursorDown, Key.CursorDown, Key.Home),
            (_, w) => Keys(w, Key.CursorRight, Key.CursorRight, Key.CursorRight),
            (_, w) => Keys(w, Key.T.WithCtrl));

        Assert.StartsWith("$.port", DetailRow(session.Screens[2]), StringComparison.Ordinal);
        Assert.Contains("[text]", session.Screens[3].Text(0), StringComparison.Ordinal);

        // Line 2 starts at offset 19 (line 1 is 16 characters and its line feed), and column 3 is 3 further on.
        Assert.Equal(22, CaretOffset(session));
        Assert.StartsWith("$.port", DetailRow(session.Screens[^1]), StringComparison.Ordinal);
    }

    [Fact]
    public void Ctrl_T_maps_the_cursor_through_a_CRLF_document_both_ways()
    {
        string text = "{\r\n  \"name\": \"web\",\r\n  \"port\": 8080\r\n}";
        OpenedFile file = Open("settings.json", text);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.T.WithCtrl),
            (_, w) => Keys(w, Key.CursorUp, Key.CursorUp, Key.CursorUp, Key.CursorUp, Key.CursorDown, Key.CursorDown, Key.Home),
            (_, w) => Keys(w, Key.CursorRight, Key.CursorRight, Key.CursorRight),
            (_, w) => Keys(w, Key.T.WithCtrl));

        // Line 2 starts at offset 21 (the first line is 3 characters with its CRLF, the second 18), and column 3 is 3 further on.
        Assert.Equal(24, CaretOffset(session));
        Assert.Equal(text, Text(session));
        Assert.StartsWith("$.port", DetailRow(session.Screens[^1]), StringComparison.Ordinal);
    }

    [Fact]
    public void Ctrl_T_maps_the_caret_past_a_multi_byte_grapheme_both_ways()
    {
        // The name's value holds a surrogate pair and a letter with a combining accent: two grapheme clusters of four UTF-16 units.
        string text = "{\r\n  \"name\": \"\U0001F642é\",\r\n  \"port\": 8080\r\n}";
        OpenedFile file = Open("settings.json", text);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorDown),
            (_, w) => Keys(w, Key.T.WithCtrl),
            (_, w) => Assert.Equal(24, w.TextView.CaretOffset),
            (_, w) => Keys(w, Key.CursorUp, Key.End, Key.CursorLeft, Key.CursorLeft),
            (_, w) => Keys(w, Key.T.WithCtrl));

        // The name's line starts at offset 3 and is 17 characters long, so its end is offset 20, before its CRLF. Two characters before
        // the end, at offset 18, is inside the name's value, after the surrogate pair and the accented letter.
        Assert.Equal(18, CaretOffset(session));
        Assert.StartsWith("$.name", DetailRow(session.Screens[^1]), StringComparison.Ordinal);
        Assert.Equal(text, Text(session));
    }

    [Fact]
    public void Ctrl_T_maps_the_cursor_into_an_XML_document_and_back()
    {
        OpenedFile file = Open("app.config", "<configuration>\n  <note>hello</note>\n</configuration>");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown),
            (_, w) => Keys(w, Key.T.WithCtrl),
            (_, w) => Keys(w, Key.T.WithCtrl));

        Assert.Contains("note", DetailRow(session.Screens[1]), StringComparison.Ordinal);

        // Line 1 starts at offset 16 (the root tag is 15 characters with its line feed). The element's part starts at its name, column 3,
        // after the '<' at column 2.
        Assert.Equal(19, CaretOffset(session));
        Assert.Equal("<configuration>\n  <note>hello</note>\n</configuration>", Text(session));
        Assert.Contains("note", DetailRow(session.Screens[^1]), StringComparison.Ordinal);
    }

    [Fact]
    public void Ctrl_S_on_text_that_does_not_parse_asks_and_Cancel_writes_nothing()
    {
        OpenedFile file = Open("settings.json", "{\"a\" 1}");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var prompt = new DialogWatch(app, "the save prompt", IsDialogTitled("Save"), null, "Cancel");
                Keys(w, Key.S.WithCtrl);
                prompt.Finish();
            });

        Assert.Equal("{\"a\" 1}", OnDisk(file));
        Assert.False(session.Window.Stopped);
        Assert.Contains("[text]", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Ctrl_S_on_text_that_does_not_parse_asks_and_Save_writes_the_text_as_it_is()
    {
        OpenedFile file = Open("settings.json", "{\"a\" 1}");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var prompt = new DialogWatch(app, "the save prompt", IsDialogTitled("Save"), null, "Save");
                Keys(w, Key.S.WithCtrl);
                prompt.Finish();
            });

        Assert.Equal("{\"a\" 1}", OnDisk(file));
        Assert.Contains("[text]", session.Screens[^1].Text(0), StringComparison.Ordinal);
        Assert.StartsWith("Saved, but it does not parse: ", DetailRow(session.Screens[^1]), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Cancel", false, false)]
    [InlineData("Discard", true, false)]
    [InlineData("Save", true, true)]
    public void Ctrl_Q_with_unsaved_text_asks_and_each_answer_is_applied(string answer, bool stopped, bool saved)
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.T.WithCtrl),
            (app, w) =>
            {
                using var prompt = new DialogWatch(app, "the quit prompt", IsDialogTitled("Quit"), null, answer);
                Type(w, " ");
                Keys(w, Key.Q.WithCtrl);
                prompt.Finish();
            });

        Assert.Equal(stopped, session.Window.Stopped);
        Assert.Equal(saved, OnDisk(file) != JsonText);
    }

    [Theory]
    [InlineData("{\n  \"a\": 1\n}\n")]
    [InlineData("{\r\n  \"a\": 1\r\n}")]
    [InlineData("{\r  \"a\": 1\r}\r")]
    [InlineData("{\n\t\"a\": 1\n}")]
    public void Each_single_line_break_style_and_tabs_round_trip_so_the_text_is_editable_and_clean(string text)
    {
        OpenedFile file = Open("settings.json", text);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.T.WithCtrl),
            (app, w) =>
            {
                using var prompt = new DialogWatch(app, "no prompt", IsDialogTitled("Quit"), null, "Cancel");
                Keys(w, Key.Q.WithCtrl);
                prompt.Finish(expectOpened: false);
            });

        Assert.Contains("[text]", session.Screens[1].Text(0), StringComparison.Ordinal);
        Assert.DoesNotContain("read-only", session.Screens[1].Text(0), StringComparison.Ordinal);
        Assert.True(session.Window.Stopped);
        Assert.Equal(text, OnDisk(file));
    }

    [Fact]
    public void Mixed_line_endings_are_editable_and_saving_keeps_every_line_break_as_it_was()
    {
        string text = "{\r\n  \"a\": 1,\n  \"b\": 2\r\n}";
        OpenedFile file = Open("settings.json", text);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.T.WithCtrl),
            (_, w) => Keys(w, Key.CursorDown, Key.End),
            (_, w) => Type(w, " "),
            (_, w) => Keys(w, Key.S.WithCtrl));

        // Only the second line changed: its line feed, and the CRLFs around it, are the bytes the file had.
        Assert.Equal("{\r\n  \"a\": 1, \n  \"b\": 2\r\n}", OnDisk(file));
        Assert.DoesNotContain("read-only", string.Concat(session.Screens.Select(Drawn)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\r\n  \"a\": 1,\r\n  \"b\": 2\r\n}", "{\r\n  \"a\": 1,\r\n\r\n  \"b\": 2\r\n}")]
    [InlineData("{\n  \"a\": 1,\n  \"b\": 2\n}", "{\n  \"a\": 1,\n\n  \"b\": 2\n}")]
    public void Enter_inserts_the_files_own_line_break_and_the_saved_file_has_it(string text, string expected)
    {
        OpenedFile file = Open("settings.json", text);

        Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.T.WithCtrl),
            (_, w) => Keys(w, Key.CursorDown, Key.End),
            (_, w) => Keys(w, Key.Enter),
            (_, w) => Assert.Equal(expected, w.Controller.Text!.Text),
            (_, w) => Keys(w, Key.S.WithCtrl));

        Assert.Equal(expected, OnDisk(file));
    }

    [Fact]
    public void A_terminal_paste_into_a_CRLF_file_inserts_the_pasted_line_breaks_as_CRLF()
    {
        string text = "{\r\n  \"a\": 1,\r\n  \"b\": 2\r\n}";
        OpenedFile file = Open("settings.json", text);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.T.WithCtrl),
            (_, w) => Keys(w, Key.CursorDown, Key.End),
            (app, _) => app.RaisePasteEvent("x\ny"));

        Assert.Equal("{\r\n  \"a\": 1,x\r\ny\r\n  \"b\": 2\r\n}", ControllerText(session));
    }

    [Fact]
    public void A_single_line_paste_from_the_clipboard_and_a_terminal_paste_are_drawn_right_after_their_step()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = RunWithClipboard(
            file,
            80,
            24,
            "abc",
            (_, w) => Keys(w, Key.T.WithCtrl),
            (_, w) => Keys(w, Key.CursorDown, Key.End),
            (_, w) => Keys(w, Key.V.WithCtrl),
            (app, _) => app.RaisePasteEvent("xyz"));

        // The screen after step i is Screens[i + 1], the first one being drawn before the steps: the steps are the switch to text, the
        // move to the end of the line, the Ctrl+V and the terminal paste.
        Assert.DoesNotContain("\"web\",abc", Drawn(session.Screens[2]), StringComparison.Ordinal);
        Assert.Contains("\"web\",abc", Drawn(session.Screens[3]), StringComparison.Ordinal);
        Assert.Contains("\"web\",abcxyz", Drawn(session.Screens[4]), StringComparison.Ordinal);
    }

    // Runs the window with a clipboard that holds the text, so that Ctrl+V pastes it.
    private static Session RunWithClipboard(OpenedFile file, int width, int height, string clipboard, params Action<IApplication, MainWindow>[] steps)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, height);
        app.Driver.Clipboard = new TextClipboard(clipboard);
        using var window = new MainWindow(file, () => (PickOutcome.Back, null), null);
        List<TerminalScreen> screens = TerminalHarness.RunWindow(
            app,
            window,
            width,
            height,
            steps.Select(step => (Action)(() => step(app, window))).ToArray());
        return new Session(screens, window);
    }

    /// <summary>A clipboard that holds a fixed text.</summary>
    private sealed class TextClipboard(string text) : ClipboardBase
    {
        public override bool IsSupported => true;

        protected override string GetClipboardDataImpl() => text;

        protected override void SetClipboardDataImpl(string value)
        {
        }
    }
}
