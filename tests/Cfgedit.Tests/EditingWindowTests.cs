using System.Runtime.ExceptionServices;
using System.Text;
using Cfgedit.Core.Files;
using Cfgedit.Core.Model;
using Cfgedit.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Cfgedit.Tests;

/// <summary>
/// Editing in the tree, as drawn: the inline field and where it sits, commit, cancel and errors, Tab walks, the multi-line editor, undo and
/// redo, save with its external-change check, quit and switch with the unsaved-changes prompt, and the message line. Keys go through the
/// window. Dialogs are answered by a watch whose timer runs inside the dialog's loop, and message expiry is driven by a fake timer.
/// </summary>
[Collection("Terminal GUI")]
public sealed class EditingWindowTests : IDisposable
{
    private const string JsonText = "{\n  \"name\": \"web\",\n  \"port\": 8080\n}";
    private const string TreeHints = "Enter Edit  Tab Next  ^Z Undo  ^S Save  ^O Open";
    private const string EditingHints = "Enter Commit  Esc Cancel  Tab Next  F4 Multi-line";
    private const string QuitHint = "^Q Quit";

    // The muted red of logvu's error labels, as TreeColors.Error.
    private static readonly Color ErrorColor = new(0xD7, 0x5F, 0x5F);

    private readonly List<string> _folders = [];

    // ----- Inline field -----

    [Fact]
    public void Enter_on_a_json_value_opens_the_field_over_the_value_and_runs_to_the_right_edge()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("    name: web", Inside(screen, 2));
        Assert.Equal("w", screen.Cells[2][11].Grapheme);
        Assert.Equal(" ", screen.Cells[2][10].Grapheme);
        AssertHints(screen, EditingHints);
    }

    [Fact]
    public void Enter_on_a_json_key_opens_the_field_over_the_key()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.Enter));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("n", screen.Cells[2][5].Grapheme);
        Assert.Equal("    name", Inside(screen, 2));
    }

    [Fact]
    public void Enter_on_an_attribute_value_on_a_wrapped_continuation_line_opens_the_field_on_that_line()
    {
        string text = "<configuration>\n  <add key=\"a\" note=\"" + new string('b', 22) + "\"/>\n</configuration>";
        OpenedFile file = Open("app.config", text);

        Session session = Run(
            file,
            40,
            12,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.CursorRight, Key.CursorRight, Key.CursorRight, Key.Enter));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal(new string(' ', 9) + "note=\"" + new string('b', 22), Inside(screen, 3));
        Assert.Equal("=", screen.Cells[3][14].Grapheme);
        Assert.Equal("b", screen.Cells[3][16].Grapheme);
    }

    [Fact]
    public void Enter_on_the_text_of_a_text_only_element_opens_the_field_over_the_text()
    {
        OpenedFile file = Open("app.config", "<configuration>\n  <note>hello</note>\n</configuration>");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("h", screen.Cells[2][11].Grapheme);
        Assert.Equal("    <note>hello", Inside(screen, 2));
    }

    [Fact]
    public void Typing_and_Enter_changes_the_document_and_the_cursor_stays_on_the_part()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace, Key.Backspace, Key.Backspace),
            (_, w) => Type(w, "gateway"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("{\n  \"name\": \"gateway\",\n  \"port\": 8080\n}", Text(session));
        Assert.Equal(1, session.Window.Tree.CursorRow);
        Assert.Equal(1, session.Window.Tree.CursorPart);
        Assert.Equal("    name: gateway", Inside(session.Screens[^1], 2));
        Assert.Equal("$.name  string  gateway", session.Screens[^1].Text(22));
    }

    [Fact]
    public void Esc_cancels_the_field_and_leaves_the_document_as_it_was()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter),
            (_, w) => Type(w, "xyz"),
            (_, w) => Keys(w, Key.Esc));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal(JsonText, Text(session));
        Assert.Equal("    name: web", Inside(screen, 2));
        Assert.Contains("settings.json [jsonc]", screen.Text(0), StringComparison.Ordinal);
        AssertHints(screen, TreeHints);
    }

    [Fact]
    public void Committing_unchanged_text_is_a_no_op_with_no_undo_entry()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.Enter),
            (_, w) => Keys(w, Key.Z.WithCtrl));

        Assert.Equal(JsonText, Text(session));
        Assert.Equal("Nothing to undo", session.Screens[^1].Text(22));
    }

    [Fact]
    public void An_error_keeps_the_field_open_with_the_typed_text_and_shows_the_message()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End),
            (_, w) => Type(w, "x"),
            (_, w) => Keys(w, Key.Enter));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal(JsonText, Text(session));
        Assert.Equal("'8080x' is not a valid number", screen.Text(22));
        Assert.Equal("8", screen.Cells[3][11].Grapheme);
        AssertHints(screen, EditingHints);
    }

    [Fact]
    public void A_duplicate_key_keeps_the_field_open_and_shows_the_message()
    {
        OpenedFile file = Open("settings.json", "{\"a\": 1, \"b\": 2}");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorDown, Key.Enter),
            (_, w) => Keys(w, Key.Backspace),
            (_, w) => Type(w, "a"),
            (_, w) => Keys(w, Key.Enter));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("{\"a\": 1, \"b\": 2}", Text(session));
        Assert.Equal("a member named 'a' already exists", screen.Text(22));
        Assert.Equal("    a", Inside(screen, 3));
    }

    // ----- Tab and Shift+Tab -----

    [Fact]
    public void Tab_commits_and_opens_the_next_editable_part_and_Shift_Tab_goes_back()
    {
        OpenedFile file = Open("settings.json", "{\n  \"a\": \"x\",\n  \"b\": \"y\",\n  \"c\": \"z\"\n}");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End),
            (_, w) => Type(w, "1"),
            (_, w) => Keys(w, Key.Tab),
            (_, w) => Keys(w, Key.End),
            (_, w) => Type(w, "2"),
            (_, w) => Keys(w, Key.Tab.WithShift));

        Assert.Equal("{\n  \"a\": \"x1\",\n  \"b2\": \"y\",\n  \"c\": \"z\"\n}", Text(session));
        Assert.Equal(1, session.Window.Tree.CursorRow);
        Assert.Equal(1, session.Window.Tree.CursorPart);
        Assert.Equal("    a: x1", Inside(session.Screens[^1], 2));
    }

    [Fact]
    public void Tab_skips_a_part_that_has_no_editor()
    {
        OpenedFile file = Open("settings.json", "{\n  \"b\": \"a\\rb\",\n  \"c\": \"z\"\n}");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.Enter),
            (_, w) => Keys(w, Key.Tab));

        Assert.Equal(2, session.Window.Tree.CursorRow);
        Assert.Equal(0, session.Window.Tree.CursorPart);
        Assert.Equal("    c", Inside(session.Screens[^1], 3));
    }

    [Fact]
    public void Tab_in_the_tree_moves_the_cursor_without_opening_an_editor()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.Tab));

        Assert.Equal(1, session.Window.Tree.CursorRow);
        Assert.Equal(0, session.Window.Tree.CursorPart);
        Assert.Equal("    name: web", Inside(session.Screens[^1], 2));
    }

    // ----- Flip, null, no editor -----

    [Fact]
    public void Enter_on_a_json_boolean_flips_it()
    {
        OpenedFile file = Open("settings.json", "{\n  \"on\": true\n}");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter));

        Assert.Equal("{\n  \"on\": false\n}", Text(session));
    }

    [Fact]
    public void Enter_on_a_json_null_says_to_use_the_type_key()
    {
        OpenedFile file = Open("settings.json", "{\n  \"none\": null\n}");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter));

        Assert.Equal("{\n  \"none\": null\n}", Text(session));
        Assert.Equal("use t to change the type", session.Screens[^1].Text(22));
    }

    [Fact]
    public void Enter_on_a_value_with_a_lone_carriage_return_says_it_can_only_be_edited_in_text_mode()
    {
        OpenedFile file = Open("settings.json", "{\n  \"cr\": \"a\\rb\"\n}");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter));

        Assert.Equal("{\n  \"cr\": \"a\\rb\"\n}", Text(session));
        Assert.Equal("this value can only be edited in text mode (Ctrl+T)", session.Screens[^1].Text(22));
    }

    [Fact]
    public void Enter_on_the_summary_of_a_container_says_the_part_cannot_be_edited()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.Enter));

        Assert.Equal("this part cannot be edited", session.Screens[^1].Text(22));
    }

    [Fact]
    public void F2_on_the_summary_of_a_container_says_the_part_cannot_be_edited()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.F2));

        Assert.Equal("this part cannot be edited", session.Screens[^1].Text(22));
    }

    // ----- Multi-line editor -----

    [Fact]
    public void F4_in_the_field_opens_the_multi_line_editor_and_OK_commits_its_text()
    {
        OpenedFile file = Open("settings.json", JsonText);
        TerminalScreen? shown = null;

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(
                    app,
                    "the multi-line editor",
                    v => v is MultilineEditor,
                    v =>
                    {
                        shown = Snap(app, v, 80, 24);
                        ((MultilineEditor)v).Area.Text = "a\nb";
                    },
                    "OK");
                Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.F4);
                dialog.Finish();
            });

        Assert.NotNull(shown);
        Assert.Contains("Edit string $.name", shown!.Text(0), StringComparison.Ordinal);
        Assert.Equal("{\n  \"name\": \"a\\nb\",\n  \"port\": 8080\n}", Text(session));
        Assert.Equal("$.name  string  a\x23CEb", session.Screens[^1].Text(22));
    }

    [Fact]
    public void A_multi_line_value_shows_every_line_above_the_buttons_at_80_by_24()
    {
        OpenedFile file = Open("settings.json", "{\n  \"text\": \"l1\\nl2\\nl3\\nl4\\nl5\"\n}");

        TerminalScreen shown = OpenMultiLine(file, 80, 24);

        AssertEveryLineVisible(shown);
    }

    [Fact]
    public void A_multi_line_value_shows_every_line_above_the_buttons_at_40_by_12()
    {
        OpenedFile file = Open("settings.json", "{\n  \"text\": \"l1\\nl2\\nl3\\nl4\\nl5\"\n}");

        TerminalScreen shown = OpenMultiLine(file, 40, 12);

        AssertEveryLineVisible(shown);
    }

    [Theory]
    [InlineData(80, 24)]
    [InlineData(40, 12)]
    public void A_multi_line_value_that_fails_to_apply_keeps_the_dialog_open_and_shows_the_error_above_the_buttons(int width, int height)
    {
        OpenedFile file = Open("settings.json", JsonText);
        TerminalScreen? shown = null;
        bool stillOpen = false;

        Session session = Run(
            file,
            width,
            height,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(
                    app,
                    "the multi-line editor",
                    v => v is MultilineEditor,
                    v =>
                    {
                        var editor = (MultilineEditor)v;
                        editor.Area.Text = "80x";
                        editor.NewKeyDownEvent(Key.Enter.WithCtrl);
                        stillOpen = app.TopRunnableView is MultilineEditor;
                        shown = Snap(app, v, width, height);
                    },
                    "Cancel");
                Keys(w, Key.CursorDown, Key.CursorDown, Key.CursorRight, Key.Enter.WithCtrl);
                dialog.Finish();
            });

        Assert.True(stillOpen, "the dialog closed on the error");
        Assert.NotNull(shown);
        int label = shown!.RowOf("'80x' is not a valid number");
        Assert.True(label < shown.RowOf("OK"), Drawn(shown));
        Assert.True(label < shown.RowOf("Cancel"), Drawn(shown));
        Assert.Equal(JsonText, Text(session));
    }

    [Fact]
    public void F2_in_the_multi_line_editor_commits_the_text()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(
                    app,
                    "the multi-line editor",
                    v => v is MultilineEditor,
                    v =>
                    {
                        ((MultilineEditor)v).Area.Text = "a\nb";
                        v.NewKeyDownEvent(Key.F2);
                    },
                    button: null);
                Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter.WithCtrl);
                dialog.Finish();
            });

        Assert.Equal("{\n  \"name\": \"a\\nb\",\n  \"port\": 8080\n}", Text(session));
    }

    [Fact]
    public void Esc_in_the_multi_line_editor_cancels_and_leaves_the_value()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(
                    app,
                    "the multi-line editor",
                    v => v is MultilineEditor,
                    v =>
                    {
                        ((MultilineEditor)v).Area.Text = "a\nb";
                        v.NewKeyDownEvent(Key.Esc);
                    },
                    button: null);
                Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter.WithCtrl);
                dialog.Finish();
            });

        Assert.Equal(JsonText, Text(session));
    }

    // ----- Undo and redo -----

    [Fact]
    public void Ctrl_Z_undoes_the_edit_and_puts_the_focus_back_on_its_node_and_Ctrl_Y_redoes_it()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace, Key.Backspace, Key.Backspace, Key.Backspace),
            (_, w) => Type(w, "9999"),
            (_, w) => Keys(w, Key.Enter, Key.CursorUp, Key.Z.WithCtrl),
            (_, w) => Keys(w, Key.Y.WithCtrl));

        Assert.Equal("$.port  number  8080", session.Screens[3].Text(22));
        Assert.Equal("{\n  \"name\": \"web\",\n  \"port\": 9999\n}", Text(session));
        Assert.Equal(2, session.Window.Tree.CursorRow);
    }

    [Fact]
    public void The_u_key_undoes_and_Ctrl_R_redoes()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace, Key.Backspace, Key.Backspace, Key.Backspace),
            (_, w) => Type(w, "gw"),
            (_, w) => Keys(w, Key.Enter),
            (_, w) => Keys(w, new Key('u')),
            (_, w) => Keys(w, Key.R.WithCtrl));

        Assert.Equal("{\n  \"name\": \"gw\",\n  \"port\": 8080\n}", Text(session));
    }

    [Fact]
    public void Redo_with_nothing_undone_says_so()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.Y.WithCtrl));

        Assert.Equal("Nothing to redo", session.Screens[^1].Text(22));
    }

    // ----- Save -----

    [Fact]
    public void Ctrl_S_on_a_clean_document_shows_Saved_and_keeps_the_title_without_a_marker()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.S.WithCtrl));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("Saved", screen.Text(22));
        Assert.Contains("settings.json [jsonc]", screen.Text(0), StringComparison.Ordinal);
        Assert.Equal(JsonText, File.ReadAllText(file.Path));
    }

    [Fact]
    public void Ctrl_S_after_an_edit_saves_shows_Saved_and_clears_the_title_marker()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace, Key.Backspace, Key.Backspace),
            (_, w) => Type(w, "gw"),
            (_, w) => Keys(w, Key.Enter, Key.S.WithCtrl));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("Saved", screen.Text(22));
        Assert.Contains("settings.json [jsonc]", screen.Text(0), StringComparison.Ordinal);
        Assert.Equal("{\n  \"name\": \"gw\",\n  \"port\": 8080\n}", File.ReadAllText(file.Path));
        Assert.Equal(screen.Normal.Foreground, screen.At("Saved").Foreground);
    }

    [Fact]
    public void The_title_has_a_star_while_the_document_differs_from_the_file()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace, Key.Backspace, Key.Backspace),
            (_, w) => Type(w, "gw"),
            (_, w) => Keys(w, Key.Enter),
            (_, w) => Keys(w, Key.Z.WithCtrl),
            (_, w) => Keys(w, Key.S.WithCtrl));

        Assert.Contains("settings.json* [jsonc]", session.Screens[3].Text(0), StringComparison.Ordinal);
        Assert.Contains("settings.json [jsonc]", session.Screens[4].Text(0), StringComparison.Ordinal);
        Assert.Contains("settings.json [jsonc]", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Ctrl_S_while_the_field_is_open_commits_the_field_first()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End),
            (_, w) => Type(w, "!"),
            (_, w) => Keys(w, Key.S.WithCtrl));

        Assert.Equal("{\n  \"name\": \"web!\",\n  \"port\": 8080\n}", File.ReadAllText(file.Path));
        Assert.Equal("Saved", session.Screens[^1].Text(22));
    }

    [Fact]
    public void Saving_over_a_file_changed_on_disk_asks_and_Overwrite_writes_the_document()
    {
        OpenedFile file = Open("settings.json", JsonText);
        File.WriteAllText(file.Path, "{\"changed\": true}");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var prompt = new DialogWatch(app, "the save prompt", IsDialogTitled("Save"), null, "Overwrite");
                Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace, Key.Backspace, Key.Backspace);
                Type(w, "gw");
                Keys(w, Key.Enter, Key.S.WithCtrl);
                prompt.Finish();
            });

        Assert.Equal("{\n  \"name\": \"gw\",\n  \"port\": 8080\n}", File.ReadAllText(file.Path));
        Assert.Equal("Saved", session.Screens[^1].Text(22));
    }

    [Fact]
    public void Reload_on_a_changed_file_asks_again_and_then_replaces_the_document_with_the_file()
    {
        OpenedFile file = Open("settings.json", JsonText);
        File.WriteAllText(file.Path, "{\"changed\": true}");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var save = new DialogWatch(app, "the save prompt", IsDialogTitled("Save"), null, "Reload");
                using var reload = new DialogWatch(app, "the reload prompt", IsDialogTitled("Reload"), null, "Reload");
                Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace, Key.Backspace, Key.Backspace);
                Type(w, "gw");
                Keys(w, Key.Enter, Key.S.WithCtrl);
                save.Finish();
                reload.Finish();
            });

        Assert.Equal("{\"changed\": true}", Text(session));
        Assert.Equal("{\"changed\": true}", File.ReadAllText(file.Path));
        Assert.Equal("Reloaded", session.Screens[^1].Text(22));
        Assert.Contains("settings.json [jsonc]", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Cancelling_the_reload_confirmation_keeps_the_document_and_the_file()
    {
        OpenedFile file = Open("settings.json", JsonText);
        File.WriteAllText(file.Path, "{\"changed\": true}");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var save = new DialogWatch(app, "the save prompt", IsDialogTitled("Save"), null, "Reload");
                using var reload = new DialogWatch(app, "the reload prompt", IsDialogTitled("Reload"), null, "Cancel");
                Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace, Key.Backspace, Key.Backspace);
                Type(w, "gw");
                Keys(w, Key.Enter, Key.S.WithCtrl);
                save.Finish();
                reload.Finish();
            });

        Assert.Equal("{\"changed\": true}", File.ReadAllText(file.Path));
        Assert.Contains("\"gw\"", Text(session), StringComparison.Ordinal);
        Assert.Contains("settings.json* [jsonc]", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Cancelling_the_external_change_prompt_writes_nothing()
    {
        OpenedFile file = Open("settings.json", JsonText);
        File.WriteAllText(file.Path, "{\"changed\": true}");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var prompt = new DialogWatch(app, "the save prompt", IsDialogTitled("Save"), null, "Cancel");
                Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace, Key.Backspace, Key.Backspace);
                Type(w, "gw");
                Keys(w, Key.Enter, Key.S.WithCtrl);
                prompt.Finish();
            });

        Assert.Equal("{\"changed\": true}", File.ReadAllText(file.Path));
        Assert.Contains("settings.json* [jsonc]", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void A_save_that_fails_shows_the_error_in_the_error_colour_and_keeps_the_window_open()
    {
        OpenedFile file = Open("settings.json", JsonText);
        string folder = Path.GetDirectoryName(file.Path)!;

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                Directory.Delete(folder, recursive: true);
                using var prompt = new DialogWatch(app, "the save prompt", IsDialogTitled("Save"), null, "Overwrite");
                Keys(w, Key.S.WithCtrl);
                prompt.Finish();
            });

        TerminalScreen screen = session.Screens[^1];
        Assert.Contains("does not exist", screen.Text(22), StringComparison.Ordinal);
        Assert.Equal(ErrorColor, screen.At("does not exist").Foreground);
        Assert.False(session.Window.Stopped);
    }

    // ----- Quit and switch -----

    [Fact]
    public void Ctrl_Q_on_a_clean_document_stops_without_a_prompt()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var prompt = new DialogWatch(app, "no prompt", IsDialogTitled("Quit"), null, "Cancel");
                Keys(w, Key.Q.WithCtrl);
                prompt.Finish(expectOpened: false);
            });

        Assert.True(session.Window.Stopped);
    }

    [Theory]
    [InlineData("Cancel", false, false)]
    [InlineData("Discard", true, false)]
    [InlineData("Save", true, true)]
    public void Ctrl_Q_with_unsaved_changes_asks_and_each_answer_is_applied(string answer, bool stopped, bool saved)
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var prompt = new DialogWatch(app, "the quit prompt", IsDialogTitled("Quit"), null, answer);
                Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace, Key.Backspace, Key.Backspace);
                Type(w, "gw");
                Keys(w, Key.Enter, Key.Q.WithCtrl);
                prompt.Finish();
            });

        Assert.Equal(stopped, session.Window.Stopped);
        string expected = saved ? "{\n  \"name\": \"gw\",\n  \"port\": 8080\n}" : JsonText;
        Assert.Equal(expected, File.ReadAllText(file.Path));
    }

    [Fact]
    public void Ctrl_Q_with_a_failing_save_keeps_the_window_open_with_the_error()
    {
        OpenedFile file = Open("settings.json", JsonText);
        string folder = Path.GetDirectoryName(file.Path)!;

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace, Key.Backspace, Key.Backspace);
                Type(w, "gw");
                Keys(w, Key.Enter);
                Directory.Delete(folder, recursive: true);
                using var quit = new DialogWatch(app, "the quit prompt", IsDialogTitled("Quit"), null, "Save");
                using var save = new DialogWatch(app, "the save prompt", IsDialogTitled("Save"), null, "Overwrite");
                Keys(w, Key.Q.WithCtrl);
                quit.Finish();
                save.Finish();
            });

        Assert.False(session.Window.Stopped);
        Assert.Contains("does not exist", session.Screens[^1].Text(22), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Cancel", false)]
    [InlineData("Discard", true)]
    public void Ctrl_O_with_unsaved_changes_asks_only_after_a_file_is_chosen(string answer, bool switched)
    {
        OpenedFile file = Open("settings.json", JsonText);
        OpenedFile second = Open("other.json", "{}");

        Session session = Run(
            file,
            80,
            24,
            () => (PickOutcome.Chosen, second),
            null,
            (app, w) =>
            {
                using var prompt = new DialogWatch(app, "the switch prompt", IsDialogTitled("Open"), null, answer);
                Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace, Key.Backspace, Key.Backspace);
                Type(w, "gw");
                Keys(w, Key.Enter, Key.O.WithCtrl);
                prompt.Finish();
            });

        Assert.Equal(switched ? second : null, session.Window.SwitchTo);
        Assert.Equal(switched, session.Window.Stopped);
    }

    [Fact]
    public void Ctrl_O_with_the_picker_left_with_Esc_asks_nothing()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            () => (PickOutcome.Back, null),
            null,
            (app, w) =>
            {
                using var prompt = new DialogWatch(app, "no prompt", IsDialogTitled("Open"), null, "Cancel");
                Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace, Key.Backspace, Key.Backspace);
                Type(w, "gw");
                Keys(w, Key.Enter, Key.O.WithCtrl);
                prompt.Finish(expectOpened: false);
            });

        Assert.Null(session.Window.SwitchTo);
        Assert.False(session.Window.Stopped);
    }

    // ----- Messages -----

    [Fact]
    public void Any_key_clears_a_message_and_the_detail_line_comes_back()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.Z.WithCtrl), (_, w) => Keys(w, Key.CursorDown));

        Assert.Equal("Nothing to undo", session.Screens[1].Text(22));
        Assert.Equal("$.name  key  name", session.Screens[2].Text(22));
    }

    [Fact]
    public void A_message_expires_after_four_seconds_and_restores_the_detail_line_and_the_hints()
    {
        OpenedFile file = Open("settings.json", JsonText);
        var expiries = new List<Action>();

        Session session = Run(
            file,
            80,
            24,
            null,
            (delay, expire) =>
            {
                Assert.Equal(TimeSpan.FromSeconds(4), delay);
                expiries.Add(expire);
                return () => { };
            },
            (_, w) => Keys(w, Key.Z.WithCtrl),
            (_, _) => expiries[^1]());

        Assert.Equal("Nothing to undo", session.Screens[1].Text(22));
        Assert.Equal("$  object  2", session.Screens[2].Text(22));
        AssertHints(session.Screens[2], TreeHints);
    }

    [Fact]
    public void An_expired_message_while_the_field_is_open_brings_back_the_editing_hints()
    {
        OpenedFile file = Open("settings.json", JsonText);
        var expiries = new List<Action>();

        Session session = Run(
            file,
            80,
            24,
            null,
            (_, expire) =>
            {
                expiries.Add(expire);
                return () => { };
            },
            (_, w) => Keys(w, Key.CursorDown, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End),
            (_, w) => Type(w, "x"),
            (_, w) => Keys(w, Key.Enter),
            (_, _) => expiries[^1]());

        Assert.Equal("$.port  number  8080", session.Screens[^1].Text(22));
        AssertHints(session.Screens[^1], EditingHints);
    }

    [Fact]
    public void An_information_message_is_drawn_in_the_normal_colour()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.Z.WithCtrl));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal(screen.Normal.Foreground, screen.At("Nothing to undo").Foreground);
    }

    [Fact]
    public void The_hints_name_the_tree_keys_and_the_quit_key_at_the_right_edge()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(file, 80, 24, null, null);

        AssertHints(session.Screens[0], TreeHints);
    }

    [Fact]
    public void At_40_by_12_the_quit_key_stays_on_the_hints_row()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(file, 40, 12, null, null);

        string row = session.Screens[0].Text(11);
        Assert.StartsWith("Enter Edit", row, StringComparison.Ordinal);
        Assert.EndsWith(QuitHint, row, StringComparison.Ordinal);
    }

    // ----- Helpers -----

    private static TerminalScreen Snap(IApplication app, View dialog, int width, int height) =>
        TerminalHarness.Snapshot(app, dialog, width, height);

    private static void AssertEveryLineVisible(TerminalScreen shown)
    {
        string drawn = Drawn(shown);
        Assert.Contains("Edit string $.text", shown.Text(0), StringComparison.Ordinal);
        int previous = -1;
        for (int i = 1; i <= 5; i++)
        {
            int row = shown.RowOf("l" + i);
            Assert.True(row > previous, $"line l{i} is not below the one above it:\n{drawn}");
            previous = row;
        }

        Assert.True(shown.RowOf("Cancel") > previous, $"Cancel is drawn above the last line:\n{drawn}");
        Assert.True(shown.RowOf("OK") > previous, $"OK is drawn above the last line:\n{drawn}");
    }

    private static string Drawn(TerminalScreen screen) => string.Join("\n", Enumerable.Range(0, screen.Cells.Count).Select(screen.Text));

    private TerminalScreen OpenMultiLine(OpenedFile file, int width, int height)
    {
        TerminalScreen? shown = null;
        Run(
            file,
            width,
            height,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(app, "the multi-line editor", v => v is MultilineEditor, v => shown = Snap(app, v, width, height), "OK");
                Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter.WithCtrl);
                dialog.Finish();
            });
        return shown ?? throw new InvalidOperationException("The multi-line editor was not shown.");
    }

    /// <summary>A row of the tree as drawn inside its frame: the cells between the two border columns, trailing spaces trimmed.</summary>
    private static string Inside(TerminalScreen screen, int row)
    {
        int width = screen.Cells[row].Length;
        return string.Concat(screen.Cells[row].Skip(1).Take(width - 2).Select(cell => cell.Grapheme)).TrimEnd();
    }

    private static void AssertHints(TerminalScreen screen, string left)
    {
        string row = screen.Text(23);
        Assert.StartsWith(left, row, StringComparison.Ordinal);
        Assert.EndsWith(QuitHint, row, StringComparison.Ordinal);
    }

    private static string Text(Session session) => Encoding.UTF8.GetString(session.Window.Tree.Document!.Source.Encode());

    private static Func<View, bool> IsDialogTitled(string title) => view => view is Dialog<int> dialog && dialog.Title == title;

    private static void Keys(MainWindow window, params Key[] keys)
    {
        foreach (Key key in keys)
        {
            window.NewKeyDownEvent(key);
        }
    }

    private static void Type(MainWindow window, string text)
    {
        foreach (char c in text)
        {
            window.NewKeyDownEvent(new Key(c));
        }
    }

    private static Session Run(
        OpenedFile file,
        int width,
        int height,
        Func<(PickOutcome Outcome, OpenedFile? File)>? pick,
        Func<TimeSpan, Action, Action>? schedule,
        params Action<IApplication, MainWindow>[] steps)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, height);
        using var window = new MainWindow(file, pick ?? (() => (PickOutcome.Back, null)), schedule);
        List<TerminalScreen> screens = TerminalHarness.RunWindow(
            app,
            window,
            width,
            height,
            steps.Select(step => (Action)(() => step(app, window))).ToArray());
        return new Session(screens, window);
    }

    private OpenedFile Open(string name, string text)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid():N}"[..16]);
        Directory.CreateDirectory(dir);
        _folders.Add(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return new OpenedFile(path, DocumentFile.Open(path, Formats.ForPath(name)!));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (string dir in _folders)
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private sealed record Session(List<TerminalScreen> Screens, MainWindow Window);

    private static IEnumerable<Button> ButtonsOf(View view) => view switch
    {
        Dialog<bool> dialog => dialog.Buttons,
        Dialog<int> dialog => dialog.Buttons,
        _ => [],
    };

    /// <summary>
    /// Watches for a dialog that <paramref name="matches"/>. The watch runs inside the dialog's loop (its timer fires there): it runs
    /// <paramref name="whileOpen"/> on the UI thread, then presses the button labelled <paramref name="button"/> when one is given (a
    /// dialog that the action closes itself needs none). <see cref="Finish"/>
    /// rethrows what the watch failed with, and asserts that the dialog opened, or with <c>expectOpened: false</c>, that it did not.
    /// </summary>
    private sealed class DialogWatch : IDisposable
    {
        private readonly IApplication _app;
        private readonly object? _timer;
        private readonly string _waitingFor;
        private ExceptionDispatchInfo? _failure;

        public DialogWatch(IApplication app, string waitingFor, Func<View, bool> matches, Action<View>? whileOpen, string? button)
        {
            _app = app;
            _waitingFor = waitingFor;
            _timer = app.AddTimeout(TimeSpan.FromMilliseconds(20), () =>
            {
                if (Opened || app.TopRunnableView is not { } top || !matches(top))
                {
                    return true;
                }

                Opened = true;
                try
                {
                    whileOpen?.Invoke(top);
                }
                catch (Exception ex)
                {
                    _failure = ExceptionDispatchInfo.Capture(ex);
                }

                if (button is not null)
                {
                    ButtonsOf(top).Single(b => b.Text.TrimStart('_') == button).InvokeCommand(Command.Accept);
                }

                return false;
            });
        }

        /// <summary>True once the dialog was seen and answered.</summary>
        public bool Opened { get; private set; }

        /// <summary>Stops the watch, rethrows what it failed with, and asserts whether the dialog opened.</summary>
        public void Finish(bool expectOpened = true)
        {
            Dispose();
            _failure?.Throw();
            Assert.True(Opened == expectOpened, expectOpened ? $"{_waitingFor} did not open" : $"{_waitingFor} opened");
        }

        public void Dispose()
        {
            if (_timer is not null)
            {
                _app.RemoveTimeout(_timer);
            }
        }
    }
}
