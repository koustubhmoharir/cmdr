using System.Text;
using Cfgedit.Tui;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Cfgedit.Tests;

/// <summary>
/// The external-change check as the window shows it: a clean file reloads (quietly when there was no history, with a message and as an undo
/// step when there was), a dirty file asks Keep mine or Reload, an inline editor holds the check off until it closes, a deleted file asks
/// Keep or Close, and a failed check is reported once. The window's check is called directly, as its one-second timer calls it.
/// </summary>
public sealed partial class EditingWindowTests
{
    private const string ChangedJson = "{\n  \"name\": \"api\",\n  \"port\": 9090\n}";
    private const string ChangedOnDiskJson = "{\"changed\": true}";
    private const string EditedJson = "{\n  \"name\": \"gw\",\n  \"port\": 8080\n}";
    private const string ReloadedMessage = "Reloaded: the file changed on disk";

    [Fact]
    public void A_clean_file_changed_on_disk_reloads_quietly_when_there_is_no_history_and_keeps_the_focus()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown),
            (_, w) =>
            {
                File.WriteAllText(file.Path, ChangedJson);
                w.CheckExternalChange();
            });

        Assert.Equal(ChangedJson, Text(session));
        Assert.Contains("settings.json [jsonc]", session.Screens[^1].Text(0), StringComparison.Ordinal);
        // Quiet: the detail line shows the focused key, as it did before the reload.
        Assert.Equal("$.name  key  name", session.Screens[^1].Text(22));
    }

    [Fact]
    public void A_clean_file_changed_on_disk_with_history_reloads_with_a_message_and_Ctrl_Z_returns_to_the_old_content()
    {
        OpenedFile file = Open("settings.json", JsonText);
        string? reloaded = null;

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) =>
            {
                Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace, Key.Backspace, Key.Backspace);
                Type(w, "gw");
                Keys(w, Key.Enter, Key.S.WithCtrl);
            },
            (_, w) =>
            {
                File.WriteAllText(file.Path, ChangedJson);
                w.CheckExternalChange();
            },
            (_, w) => reloaded = TreeText(w),
            (_, w) => Keys(w, Key.Z.WithCtrl));

        Assert.Equal("Saved", session.Screens[1].Text(22));
        Assert.Equal(ReloadedMessage, session.Screens[2].Text(22));
        Assert.Equal(ChangedJson, reloaded);
        Assert.Equal(EditedJson, Text(session));
        Assert.Contains("settings.json* [jsonc]", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void A_dirty_file_changed_on_disk_asks_and_Keep_mine_keeps_the_document_and_the_next_save_does_not_ask()
    {
        OpenedFile file = Open("settings.json", JsonText);
        File.WriteAllText(file.Path, ChangedOnDiskJson);
        string question = string.Empty;

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var prompt = new DialogWatch(
                    app,
                    "the changed-on-disk question",
                    IsDialogTitled("Changed on disk"),
                    v => question = MessageOf(v),
                    "Keep mine");
                Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace, Key.Backspace, Key.Backspace);
                Type(w, "gw");
                Keys(w, Key.Enter);
                w.CheckExternalChange();
                prompt.Finish();
            },
            (app, w) =>
            {
                using var save = new DialogWatch(app, "the save prompt", IsDialogTitled("Save"), null, "Overwrite");
                Keys(w, Key.S.WithCtrl);
                save.Finish(expectOpened: false);
            });

        Assert.Contains("settings.json changed on disk. Reload it or keep your changes?", question, StringComparison.Ordinal);
        Assert.DoesNotContain("discards", question, StringComparison.Ordinal);
        Assert.Equal("Kept your changes; Ctrl+S overwrites the file", session.Screens[1].Text(22));
        Assert.Equal(EditedJson, TreeText(session.Window));
        Assert.Equal(EditedJson, File.ReadAllText(file.Path));
        Assert.Equal("Saved", session.Screens[^1].Text(22));
    }

    [Fact]
    public void Esc_on_the_changed_on_disk_question_keeps_the_document()
    {
        OpenedFile file = Open("settings.json", JsonText);
        File.WriteAllText(file.Path, ChangedOnDiskJson);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var prompt = new DialogWatch(app, "the changed-on-disk question", IsDialogTitled("Changed on disk"), _ => app.Keyboard.RaiseKeyDownEvent(Key.Esc), null);
                Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace, Key.Backspace, Key.Backspace);
                Type(w, "gw");
                Keys(w, Key.Enter);
                w.CheckExternalChange();
                prompt.Finish();
            });

        Assert.Equal("Kept your changes; Ctrl+S overwrites the file", session.Screens[1].Text(22));
        Assert.Equal(EditedJson, TreeText(session.Window));
        Assert.Equal(ChangedOnDiskJson, File.ReadAllText(file.Path));
    }

    [Fact]
    public void A_dirty_file_changed_on_disk_asks_and_Reload_replaces_the_document_with_the_file_as_an_undo_step()
    {
        OpenedFile file = Open("settings.json", JsonText);
        File.WriteAllText(file.Path, ChangedJson);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var prompt = new DialogWatch(app, "the changed-on-disk question", IsDialogTitled("Changed on disk"), null, "Reload");
                Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace, Key.Backspace, Key.Backspace);
                Type(w, "gw");
                Keys(w, Key.Enter);
                w.CheckExternalChange();
                prompt.Finish();
            },
            (_, w) => Keys(w, Key.Z.WithCtrl));

        Assert.Equal(ReloadedMessage, session.Screens[1].Text(22));
        Assert.Equal(EditedJson, Text(session));
        Assert.Equal(ChangedJson, File.ReadAllText(file.Path));
    }

    [Fact]
    public void In_text_mode_the_changed_on_disk_question_says_that_Reload_discards_the_text_typed()
    {
        OpenedFile file = Open("settings.json", JsonText);
        File.WriteAllText(file.Path, ChangedJson);
        string question = string.Empty;

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var prompt = new DialogWatch(
                    app,
                    "the changed-on-disk question",
                    IsDialogTitled("Changed on disk"),
                    v => question = MessageOf(v),
                    "Reload");
                Keys(w, Key.T.WithCtrl);
                Type(w, "x");
                w.CheckExternalChange();
                prompt.Finish();
            });

        Assert.Contains("Reloading discards the text you typed.", question, StringComparison.Ordinal);
        Assert.Equal(ChangedJson, TreeText(session.Window));
    }

    [Fact]
    public void The_check_is_skipped_while_an_inline_editor_is_open_and_the_change_is_handled_after_it_closes()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter),
            (_, w) =>
            {
                File.WriteAllText(file.Path, ChangedJson);
                w.CheckExternalChange();
                // Skipped: the document is still the old one, and the editor is still open.
                Assert.Equal(JsonText, TreeText(w));
                Assert.NotNull(EditorOf(w));
            },
            (_, w) => Keys(w, Key.Esc),
            (_, w) => w.CheckExternalChange());

        Assert.Equal(ChangedJson, Text(session));
        // The cursor was on the value, so the value is focused again after the reload.
        Assert.Equal("$.name  string  api", session.Screens[^1].Text(22));
    }

    [Fact]
    public void A_deleted_file_asks_and_Keep_makes_the_document_unsaved_and_Ctrl_S_recreates_the_file()
    {
        OpenedFile file = Open("settings.json", JsonText);
        File.Delete(file.Path);
        string question = string.Empty;

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var prompt = new DialogWatch(
                    app,
                    "the deleted question",
                    IsDialogTitled("Deleted on disk"),
                    v => question = MessageOf(v),
                    "Keep");
                w.CheckExternalChange();
                prompt.Finish();
            },
            (app, w) =>
            {
                using var save = new DialogWatch(app, "the save prompt", IsDialogTitled("Save"), null, "Overwrite");
                Keys(w, Key.S.WithCtrl);
                save.Finish(expectOpened: false);
            });

        Assert.Contains("settings.json was deleted or moved on disk.", question, StringComparison.Ordinal);
        Assert.DoesNotContain("discards", question, StringComparison.Ordinal);
        Assert.Equal("The file was deleted on disk", session.Screens[1].Text(22));
        Assert.Contains("settings.json* [jsonc]", session.Screens[1].Text(0), StringComparison.Ordinal);
        Assert.Equal("Saved", session.Screens[^1].Text(22));
        Assert.Equal(JsonText, File.ReadAllText(file.Path));
    }

    [Fact]
    public void A_deleted_dirty_file_asks_with_the_discard_warning_and_Close_stops_the_window_for_the_picker()
    {
        OpenedFile file = Open("settings.json", JsonText);
        string question = string.Empty;

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
                File.Delete(file.Path);
                using var prompt = new DialogWatch(
                    app,
                    "the deleted question",
                    IsDialogTitled("Deleted on disk"),
                    v => question = MessageOf(v),
                    "Close");
                w.CheckExternalChange();
                prompt.Finish();
            });

        Assert.Contains("Close discards your unsaved changes.", question, StringComparison.Ordinal);
        Assert.True(session.Window.Stopped);
        Assert.True(session.Window.ReturnToPicker);
        Assert.Null(session.Window.SwitchTo);
    }

    [Fact]
    public void A_failed_check_shows_its_error_once()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) =>
            {
                // The file is replaced by a folder, which the check cannot read as a file.
                File.Delete(file.Path);
                Directory.CreateDirectory(file.Path);
                w.CheckExternalChange();
            },
            (_, w) => Keys(w, Key.CursorDown),
            (_, w) => w.CheckExternalChange());

        Assert.Contains("is a folder, not a file", session.Screens[1].Text(22), StringComparison.Ordinal);
        // The key clears the message, and the second check reports nothing for the same state of the file.
        Assert.DoesNotContain("folder", session.Screens[^1].Text(22), StringComparison.Ordinal);
    }

    // The document's text as the tree holds it.
    private static string TreeText(MainWindow window) => Encoding.UTF8.GetString(window.Tree.Document!.Source.Encode());

    // The message of a question: a message box holds it as the dialog's text.
    private static string MessageOf(View dialog) => dialog.Text;
}
