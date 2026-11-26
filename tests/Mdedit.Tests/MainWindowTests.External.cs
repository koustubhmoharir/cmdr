using Mdedit.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Mdedit.Tests;

/// <summary>
/// The window's check for a change on disk, called directly as its timer calls it: a clean file reloads (quietly when there was no undo history,
/// with a message and as one undo step when there was) in both modes; a dirty file asks Keep mine (the default) or Reload; a deleted file asks
/// Keep or Close; a check waits while a dialog is open; and a failed check is shown once.
/// </summary>
public sealed partial class MainWindowTests
{
    // The text that replaces the file's text when a test changes the file on disk.
    private const string Changed = "# Two\n\nBeta.\n";

    [Fact]
    public void A_clean_file_changed_on_disk_reloads_in_view_mode_quietly_when_there_was_no_history()
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes(Small));

        Session session = RunWith(
            file,
            40,
            12,
            null,
            null,
            (_, window) =>
            {
                File.WriteAllText(file.Path, Changed);
                window.CheckExternalChange();
            });

        Assert.Equal(Framed("▾ Two"), session.Screens[^1].Text(1));
        Assert.Equal(Status("Two", "All"), session.Screens[^1].Text(MessageRow));
        Assert.Contains("notes.md", session.Screens[^1].Text(0), StringComparison.Ordinal);
        Assert.DoesNotContain("*", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void A_clean_file_with_undo_history_reloads_with_a_message_and_Ctrl_Z_in_view_mode_goes_back_to_the_saved_text()
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes(Small));
        string? edited = null;
        string? reloaded = null;

        Session session = RunWith(
            file,
            40,
            12,
            null,
            null,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) =>
            {
                window.Editor.CaretOffset = window.Editor.Buffer.TextLength;
                Type(window, "Z");
            },
            (_, window) =>
            {
                window.NewKeyDownEvent(Key.S.WithCtrl);
                edited = window.Editor.Buffer.Text;
            },
            (_, window) =>
            {
                File.WriteAllText(file.Path, Changed);
                window.CheckExternalChange();
                reloaded = window.Editor.Buffer.Text;
            },
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl));

        Assert.Equal(Changed, reloaded);
        Assert.StartsWith(MainWindow.ReloadedFromDisk, session.Screens[4].Text(MessageRow), StringComparison.Ordinal);
        Assert.Equal(edited, session.Window.Editor.Buffer.Text);
        Assert.Equal(Changed, File.ReadAllText(file.Path));
    }

    [Fact]
    public void A_clean_file_changed_on_disk_reloads_in_edit_mode_and_the_view_renders_the_new_text_when_shown()
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes(Small));

        Session session = RunWith(
            file,
            40,
            12,
            null,
            null,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) =>
            {
                File.WriteAllText(file.Path, Changed);
                window.CheckExternalChange();
            },
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        Assert.Equal(Changed, session.Window.Editor.Buffer.Text);
        Assert.Equal(Framed("▾ Two"), session.Screens[^1].Text(1));
    }

    [Fact]
    public void A_dirty_file_changed_on_disk_asks_and_Keep_mine_is_the_default_and_the_next_save_writes_without_asking()
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes(Small));
        string? question = null;
        string edited = string.Empty;

        Session session = RunWith(
            file,
            40,
            12,
            null,
            null,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) =>
            {
                window.Editor.CaretOffset = window.Editor.Buffer.TextLength;
                Type(window, "Z");
                edited = window.Editor.Buffer.Text;
            },
            (app, window) =>
            {
                File.WriteAllText(file.Path, Changed);
                using var prompt = new DialogWatch(app, "the changed-on-disk question", IsDialogTitled("Changed on disk"), v => question = MessageOf(v), "Keep mine");
                window.CheckExternalChange();
                prompt.Finish();
            },
            (app, window) =>
            {
                using var save = new DialogWatch(app, "the save question", IsDialogTitled("Save"), null, "Overwrite");
                window.NewKeyDownEvent(Key.S.WithCtrl);
                Assert.False(save.Opened, "Ctrl+S asked about the changed file after Keep mine");
            });

        Assert.Equal($"{Path.GetFileName(file.Path)} changed on disk. Reload it or keep your changes?", question);
        Assert.StartsWith("Kept your changes", session.Screens[3].Text(MessageRow), StringComparison.Ordinal);
        Assert.Contains("notes.md * [edit]", session.Screens[3].Text(0), StringComparison.Ordinal);
        Assert.Equal(edited, session.Window.Editor.Buffer.Text);
        Assert.Equal(edited, File.ReadAllText(file.Path).Replace("\r\n", "\n"));
        Assert.StartsWith("Saved", session.Screens[^1].Text(MessageRow), StringComparison.Ordinal);
    }

    [Fact]
    public void A_dirty_file_changed_on_disk_with_Reload_replaces_the_text_as_one_undo_step()
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes(Small));
        string edited = string.Empty;
        string reloaded = string.Empty;

        Session session = RunWith(
            file,
            40,
            12,
            null,
            null,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) =>
            {
                window.Editor.CaretOffset = window.Editor.Buffer.TextLength;
                Type(window, "Z");
                edited = window.Editor.Buffer.Text;
            },
            (app, window) =>
            {
                File.WriteAllText(file.Path, Changed);
                using var prompt = new DialogWatch(app, "the changed-on-disk question", IsDialogTitled("Changed on disk"), null, "Reload");
                window.CheckExternalChange();
                prompt.Finish();
                reloaded = window.Editor.Buffer.Text;
            },
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl));

        Assert.StartsWith(MainWindow.ReloadedFromDisk, session.Screens[3].Text(MessageRow), StringComparison.Ordinal);
        Assert.Equal(Changed, reloaded);
        Assert.Equal(edited, session.Window.Editor.Buffer.Text);
        Assert.Contains("notes.md *", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void After_Keep_mine_undoing_every_edit_leaves_the_document_dirty_because_the_disk_has_other_text()
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes(Small));

        Session session = RunWith(
            file,
            40,
            12,
            null,
            null,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) =>
            {
                window.Editor.CaretOffset = window.Editor.Buffer.TextLength;
                Type(window, "Z");
            },
            (app, window) =>
            {
                File.WriteAllText(file.Path, Changed);
                using var prompt = new DialogWatch(app, "the changed-on-disk question", IsDialogTitled("Changed on disk"), null, "Keep mine");
                window.CheckExternalChange();
                prompt.Finish();
            },
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl));

        Assert.Equal(Small, session.Window.Editor.Buffer.Text);
        Assert.Contains("notes.md *", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void A_deleted_file_asks_and_Keep_keeps_the_text_as_an_unsaved_file_and_Ctrl_S_creates_it_again()
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes(Small));
        string? question = null;

        Session session = RunWith(
            file,
            40,
            12,
            null,
            null,
            (app, window) =>
            {
                File.Delete(file.Path);
                using var prompt = new DialogWatch(app, "the deleted question", IsDialogTitled("Deleted on disk"), v => question = MessageOf(v), "Keep");
                window.CheckExternalChange();
                prompt.Finish();
            },
            (_, window) => window.NewKeyDownEvent(Key.S.WithCtrl));

        Assert.Equal($"{Path.GetFileName(file.Path)} was deleted or moved on disk.", question);
        Assert.StartsWith(MainWindow.KeptDeleted, session.Screens[1].Text(MessageRow), StringComparison.Ordinal);
        Assert.Contains("notes.md *", session.Screens[1].Text(0), StringComparison.Ordinal);
        Assert.Equal(Small, File.ReadAllText(file.Path).Replace("\r\n", "\n"));
        Assert.StartsWith("Saved", session.Screens[^1].Text(MessageRow), StringComparison.Ordinal);
        Assert.False(session.Window.ReturnToPicker);
    }

    [Fact]
    public void A_deleted_dirty_file_with_Close_stops_the_window_for_the_picker_and_warns_about_the_edits()
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes(Small));
        string? question = null;

        Session session = RunWith(
            file,
            100,
            12,
            null,
            null,
            (app, window) =>
            {
                window.NewKeyDownEvent(Key.T.WithCtrl);
                window.Editor.CaretOffset = 0;
                Type(window, "Z");
                File.Delete(file.Path);
                using var prompt = new DialogWatch(app, "the deleted question", IsDialogTitled("Deleted on disk"), v => question = MessageOf(v), "Close");
                window.CheckExternalChange();
                prompt.Finish();
            });

        Assert.Contains("Close discards your unsaved changes.", question, StringComparison.Ordinal);
        Assert.True(session.Window.ReturnToPicker);
        Assert.True(session.Window.Stopped);
    }

    [Fact]
    public void A_check_waits_while_a_dialog_is_open_and_the_change_is_seen_by_the_next_check_after_it_closes()
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes(Small));
        bool changedDialogOpened = true;

        Session session = RunWith(
            file,
            40,
            12,
            null,
            null,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) =>
            {
                window.Editor.CaretOffset = window.Editor.Buffer.TextLength;
                Type(window, "Z");
            },
            (app, window) =>
            {
                // The question for a changed file must not open while the quit question is up; its watch would press Keep mine if it did.
                using var changed = new DialogWatch(app, "the changed-on-disk question", IsDialogTitled("Changed on disk"), null, "Keep mine");
                using var quit = new DialogWatch(
                    app,
                    "the quit question",
                    IsDialogTitled("Quit"),
                    _ =>
                    {
                        File.WriteAllText(file.Path, Changed);
                        window.CheckExternalChange();
                    },
                    "Cancel");
                window.NewKeyDownEvent(Key.Q.WithCtrl);
                quit.Finish();
                changedDialogOpened = changed.Opened;
            },
            (app, window) =>
            {
                using var reload = new DialogWatch(app, "the changed-on-disk question", IsDialogTitled("Changed on disk"), null, "Reload");
                window.CheckExternalChange();
                reload.Finish();
            });

        Assert.False(changedDialogOpened);
        Assert.False(session.Window.Stopped);
        Assert.Equal(Changed, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_shown_once_and_the_next_check_shows_nothing()
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes(Small));

        Session session = RunWith(
            file,
            100,
            12,
            null,
            null,
            (_, window) =>
            {
                File.Delete(file.Path);
                Directory.CreateDirectory(file.Path);
                window.CheckExternalChange();
            },
            (_, window) => window.NewKeyDownEvent(Key.CursorDown),
            (_, window) => window.CheckExternalChange());

        Assert.Contains("is a folder, not a file", session.Screens[1].Text(MessageRow), StringComparison.Ordinal);
        Assert.DoesNotContain("folder", session.Screens[^1].Text(MessageRow), StringComparison.Ordinal);
        Assert.False(session.Window.Stopped);
    }
}
