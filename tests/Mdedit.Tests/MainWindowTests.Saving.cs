using Mdedit.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Mdedit.Tests;

/// <summary>
/// Saving in the window: Ctrl+S in both modes writes the exact bytes of the edited text, the title's dirty mark follows the save, a file
/// that changed on disk asks Overwrite, Reload or Cancel, a failed save shows its error and keeps the document dirty, and Ctrl+Q and Ctrl+O
/// ask Save, Discard or Cancel while the document is dirty. Dialogs are answered by <see cref="DialogWatch"/>; a test that expects no dialog
/// watches for one that must not open, so that a stray question fails the test instead of waiting.
/// </summary>
public sealed partial class MainWindowTests
{
    // A CRLF document with a byte order mark: the mark and the line breaks must survive a save that edits one line.
    private static readonly byte[] CrlfWithBom = [0xEF, 0xBB, 0xBF, .. "# Title\r\n\r\nBody.\r\n"u8.ToArray()];

    [Fact]
    public void Ctrl_S_in_edit_mode_writes_the_edited_text_with_the_BOM_and_the_CRLF_lines_kept_and_clears_the_dirty_mark()
    {
        OpenedFile file = OpenedBytes("notes.md", CrlfWithBom);

        Session session = RunWith(
            file,
            40,
            12,
            null,
            null,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.Editor.CaretOffset = Index(window.Editor.Buffer.Text, "Body"),
            (_, window) => Type(window, "New "),
            (_, window) => window.NewKeyDownEvent(Key.S.WithCtrl));

        byte[] expected = [0xEF, 0xBB, 0xBF, .. "# Title\r\n\r\nNew Body.\r\n"u8.ToArray()];
        Assert.Equal(expected, File.ReadAllBytes(file.Path));
        Assert.StartsWith("Saved", session.Screens[^1].Text(MessageRow), StringComparison.Ordinal);
        Assert.Contains("notes.md [edit]", session.Screens[^1].Text(0), StringComparison.Ordinal);
        Assert.DoesNotContain("*", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Ctrl_S_in_view_mode_writes_the_same_bytes_when_nothing_was_edited()
    {
        OpenedFile file = OpenedBytes("notes.md", CrlfWithBom);

        Session session = RunWith(file, 40, 12, null, null, (_, window) => window.NewKeyDownEvent(Key.S.WithCtrl));

        Assert.Equal(CrlfWithBom, File.ReadAllBytes(file.Path));
        Assert.StartsWith("Saved", session.Screens[^1].Text(MessageRow), StringComparison.Ordinal);
    }

    [Fact]
    public void A_typed_document_shows_the_dirty_mark_until_it_is_saved()
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes("# Title\n"));

        Session session = RunWith(
            file,
            40,
            12,
            null,
            null,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => Type(window, "x"),
            (_, window) => window.NewKeyDownEvent(Key.S.WithCtrl));

        Assert.Contains("notes.md * [edit]", session.Screens[2].Text(0), StringComparison.Ordinal);
        Assert.Contains("notes.md [edit]", session.Screens[^1].Text(0), StringComparison.Ordinal);
        Assert.DoesNotContain("*", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Ctrl_S_on_a_file_changed_on_disk_asks_and_Overwrite_writes_the_text_over_it()
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes("# One\n"));
        string? question = null;

        Session session = RunWith(
            file,
            40,
            12,
            null,
            null,
            (app, window) =>
            {
                using var prompt = new DialogWatch(app, "the save question", IsDialogTitled("Save"), v => question = MessageOf(v), "Overwrite");
                window.NewKeyDownEvent(Key.T.WithCtrl);
                Type(window, "x");
                File.WriteAllText(file.Path, "# Someone else\n");
                window.NewKeyDownEvent(Key.S.WithCtrl);
                prompt.Finish();
            });

        Assert.Contains("changed on disk since it was opened", question, StringComparison.Ordinal);
        Assert.Equal(session.Window.Editor.Buffer.Text, File.ReadAllText(file.Path));
        Assert.StartsWith("Saved", session.Screens[^1].Text(MessageRow), StringComparison.Ordinal);
    }

    [Fact]
    public void Ctrl_S_on_a_changed_file_with_Reload_replaces_the_text_with_the_file_and_writes_nothing()
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes("# One\n"));

        Session session = RunWith(
            file,
            40,
            12,
            null,
            null,
            (app, window) =>
            {
                using var prompt = new DialogWatch(app, "the save question", IsDialogTitled("Save"), null, "Reload");
                window.NewKeyDownEvent(Key.T.WithCtrl);
                Type(window, "x");
                File.WriteAllText(file.Path, "# Someone else\n");
                window.NewKeyDownEvent(Key.S.WithCtrl);
                prompt.Finish();
            });

        Assert.Equal("# Someone else\n", session.Window.Editor.Buffer.Text);
        Assert.Equal("# Someone else\n", File.ReadAllText(file.Path));
        Assert.StartsWith("Reloaded", session.Screens[^1].Text(MessageRow), StringComparison.Ordinal);
        Assert.DoesNotContain("*", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Ctrl_S_on_a_changed_file_with_Cancel_writes_nothing_and_keeps_the_text_dirty()
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes("# One\n"));

        Session session = RunWith(
            file,
            40,
            12,
            null,
            null,
            (app, window) =>
            {
                using var prompt = new DialogWatch(app, "the save question", IsDialogTitled("Save"), null, "Cancel");
                window.NewKeyDownEvent(Key.T.WithCtrl);
                Type(window, "x");
                File.WriteAllText(file.Path, "# Someone else\n");
                window.NewKeyDownEvent(Key.S.WithCtrl);
                prompt.Finish();
            });

        Assert.Equal("# Someone else\n", File.ReadAllText(file.Path));
        Assert.Contains("notes.md * [edit]", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void A_save_whose_folder_was_removed_asks_then_shows_the_error_and_the_document_stays_dirty()
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes("# One\n"));
        string folder = Path.GetDirectoryName(file.Path)!;

        Session session = RunWith(
            file,
            100,
            12,
            null,
            null,
            (app, window) =>
            {
                using var prompt = new DialogWatch(app, "the save question", IsDialogTitled("Save"), null, "Overwrite");
                window.NewKeyDownEvent(Key.T.WithCtrl);
                Type(window, "x");
                RemoveFolder(folder);
                window.NewKeyDownEvent(Key.S.WithCtrl);
                prompt.Finish();
            });

        Assert.Contains("does not exist", session.Screens[^1].Text(MessageRow), StringComparison.Ordinal);
        Assert.Contains("notes.md * [edit]", session.Screens[^1].Text(0), StringComparison.Ordinal);
        Assert.False(session.Window.Stopped);
    }

    [Theory]
    [InlineData("Save", true, "x# One\n")]
    [InlineData("Discard", true, "# One\n")]
    [InlineData("Cancel", false, "# One\n")]
    public void Ctrl_Q_on_a_dirty_document_asks_Save_Discard_or_Cancel(string answer, bool stops, string fileText)
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes("# One\n"));
        string? question = null;

        Session session = RunWith(
            file,
            40,
            12,
            null,
            null,
            (app, window) =>
            {
                using var prompt = new DialogWatch(app, "the unsaved question", IsDialogTitled("Quit"), v => question = MessageOf(v), answer);
                window.NewKeyDownEvent(Key.T.WithCtrl);
                window.Editor.CaretOffset = 0;
                Type(window, "x");
                window.NewKeyDownEvent(Key.Q.WithCtrl);
                prompt.Finish();
            });

        Assert.Equal("Save changes to notes.md?", question);
        Assert.Equal(stops, session.Window.Stopped);
        Assert.Equal(fileText, File.ReadAllText(file.Path));
    }

    [Fact]
    public void Ctrl_Q_with_Save_on_a_failing_save_keeps_the_window_open_with_the_error()
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes("# One\n"));
        string folder = Path.GetDirectoryName(file.Path)!;

        Session session = RunWith(
            file,
            100,
            12,
            null,
            null,
            (app, window) =>
            {
                using var quit = new DialogWatch(app, "the unsaved question", IsDialogTitled("Quit"), null, "Save");
                using var save = new DialogWatch(app, "the save question", IsDialogTitled("Save"), null, "Overwrite");
                window.NewKeyDownEvent(Key.T.WithCtrl);
                Type(window, "x");
                RemoveFolder(folder);
                window.NewKeyDownEvent(Key.Q.WithCtrl);
                quit.Finish();
                Assert.True(save.Opened, "the save question did not open");
            });

        Assert.False(session.Window.Stopped);
        Assert.Contains("does not exist", session.Screens[^1].Text(MessageRow), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Save", true, "x# One\n")]
    [InlineData("Discard", true, "# One\n")]
    [InlineData("Cancel", false, "# One\n")]
    public void Ctrl_O_on_a_dirty_document_asks_once_a_file_is_chosen_and_Save_or_Discard_switches_to_it(string answer, bool switches, string fileText)
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes("# One\n"));
        OpenedFile second = OpenedBytes("second.md", Bytes("# Two\n"));
        int pickers = 0;

        Session session = RunWith(
            file,
            40,
            12,
            () => (pickers++ == 0 ? PickOutcome.Chosen : PickOutcome.Back, second),
            null,
            (app, window) =>
            {
                using var prompt = new DialogWatch(app, "the unsaved question", IsDialogTitled("Open"), null, answer);
                window.NewKeyDownEvent(Key.T.WithCtrl);
                window.Editor.CaretOffset = 0;
                Type(window, "x");
                window.NewKeyDownEvent(Key.O.WithCtrl);
                prompt.Finish();
            });

        Assert.Equal(1, pickers);
        Assert.Equal(fileText, File.ReadAllText(file.Path));
        if (switches)
        {
            Assert.Same(second, session.Window.SwitchTo);
            Assert.True(session.Window.Stopped);
        }
        else
        {
            Assert.Null(session.Window.SwitchTo);
            Assert.False(session.Window.Stopped);
        }
    }

    // Removes a folder that a test made, and forgets it, so that Dispose does not look for it.
    private void RemoveFolder(string folder)
    {
        Directory.Delete(folder, recursive: true);
        _folders.Remove(folder);
    }
}
