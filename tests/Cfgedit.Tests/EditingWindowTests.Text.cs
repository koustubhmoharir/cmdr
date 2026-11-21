using System.Text;
using Cfgedit.Core;
using Cfgedit.Core.Files;
using Cfgedit.Tui;
using Terminal.Gui.Input;

namespace Cfgedit.Tests;

/// <summary>
/// Text mode, as drawn and as driven through the window: a file that does not parse opens here, F5 and Ctrl+T reparse, Ctrl+T switches both
/// ways with the cursor mapped, Ctrl+S asks about text that does not parse, Ctrl+Q asks about unsaved text, and mixed line endings make the
/// text read-only. The text view's caret is moved to the top before the tests move it, so that each starts at a known row.
/// </summary>
public sealed partial class EditingWindowTests
{
    private const string TextHints = "F5 Parse  ^T Tree  ^S Save";

    // A broken file with its error at line 2, column 10: the second string, where a colon is expected.
    private const string BrokenText = "{\n  \"name\" \"web\"\n}";

    private static string OnDisk(OpenedFile file) => Encoding.UTF8.GetString(File.ReadAllBytes(file.Path));

    private static string ViewText(Session session) => session.Window.TextView.Text;

    private static System.Drawing.Point Caret(Session session) => session.Window.TextView.InsertionPoint;

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
        Assert.Equal(9, Caret(session).X);
        Assert.Equal(1, Caret(session).Y);
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
        Assert.Equal(9, Caret(session).X);
        Assert.Equal(1, Caret(session).Y);
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
        Assert.Equal(2, Caret(session).Y);
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

        Assert.Equal(2, Caret(session).Y);
        Assert.Equal(text, Text(session));
        Assert.StartsWith("$.port", DetailRow(session.Screens[^1]), StringComparison.Ordinal);
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
        Assert.Equal(1, Caret(session).Y);
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
    public void Mixed_line_endings_make_the_text_read_only_and_say_so_and_typing_changes_nothing()
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
            (_, w) => Type(w, "x"),
            (app, w) =>
            {
                using var prompt = new DialogWatch(app, "no prompt", IsDialogTitled("Quit"), null, "Cancel");
                Keys(w, Key.Q.WithCtrl);
                prompt.Finish(expectOpened: false);
            });

        Assert.Contains("[text, read-only]", session.Screens[1].Text(0), StringComparison.Ordinal);
        Assert.StartsWith("text mode is read-only", DetailRow(session.Screens[1]), StringComparison.Ordinal);
        Assert.True(session.Window.TextView.ReadOnly);
        Assert.True(session.Window.Stopped);
        Assert.Equal(text, OnDisk(file));
    }
}
