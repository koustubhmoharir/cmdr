using Mdedit.Tui;
using Shared.Core;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Mdedit.Tests;

/// <summary>
/// The new-file flow: a new file opens in edit mode, empty and dirty until its first save; Enter writes the platform newline; the first
/// successful save creates the file and records it in the recent list, and a failed one records nothing; closing it unsaved creates nothing;
/// and the Create question creates nothing when it is cancelled.
/// </summary>
public sealed partial class MainWindowTests
{
    [Fact]
    public void A_new_file_opens_in_edit_mode_empty_with_the_dirty_mark_and_nothing_is_on_disk()
    {
        string path = Path.Combine(NewFolder(), "new.md");

        Session session = RunWith(NewFile(path), 40, 12, null, null);

        Assert.True(session.Window.InEditMode);
        Assert.Equal(string.Empty, session.Window.Editor.Buffer.Text);
        Assert.Contains("new.md * [edit]", session.Screens[0].Text(0), StringComparison.Ordinal);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Typing_and_Enter_then_Ctrl_S_creates_the_file_with_the_platform_newline_and_the_first_save_runs_once()
    {
        string path = Path.Combine(NewFolder(), "new.md");
        int firstSaves = 0;
        string expected = "# Hi" + Environment.NewLine + "Body";

        Session session = RunWith(
            NewFile(path),
            40,
            12,
            null,
            () => firstSaves++,
            (_, window) => Type(window, "# Hi"),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => Type(window, "Body"),
            (_, window) => window.NewKeyDownEvent(Key.S.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.S.WithCtrl));

        Assert.Equal(Bytes(expected), File.ReadAllBytes(path));
        Assert.Equal(1, firstSaves);
        Assert.StartsWith("Saved", session.Screens[^1].Text(MessageRow), StringComparison.Ordinal);
        Assert.Contains("new.md [edit]", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void The_recent_list_gets_the_new_file_only_after_its_first_successful_save()
    {
        string folder = NewFolder();
        string path = Path.Combine(folder, "new.md");
        string store = Path.Combine(folder, "recent.json");

        Session session = RunWith(
            NewFile(path),
            40,
            12,
            null,
            MdeditHost.RecordOnFirstSave(path, () => RecentFiles.Load(store)),
            (_, _) => Assert.Empty(RecentFiles.Load(store).Files),
            (_, window) => Type(window, "# Hi"),
            (_, window) => window.NewKeyDownEvent(Key.S.WithCtrl),
            (_, _) => Assert.Equal(new[] { Path.GetFullPath(path) }, RecentFiles.Load(store).Files));

        Assert.Equal("# Hi", File.ReadAllText(path));
        Assert.StartsWith("Saved", session.Screens[^1].Text(MessageRow), StringComparison.Ordinal);
    }

    [Fact]
    public void A_save_into_a_missing_folder_shows_the_error_records_nothing_and_stays_dirty()
    {
        string folder = NewFolder();
        string path = Path.Combine(folder, "missing", "new.md");
        string store = Path.Combine(folder, "recent.json");

        Session session = RunWith(
            NewFile(path),
            100,
            12,
            null,
            MdeditHost.RecordOnFirstSave(path, () => RecentFiles.Load(store)),
            (_, window) => Type(window, "# Hi"),
            (_, window) => window.NewKeyDownEvent(Key.S.WithCtrl));

        Assert.False(File.Exists(path));
        Assert.Empty(RecentFiles.Load(store).Files);
        Assert.Contains("does not exist", session.Screens[^1].Text(MessageRow), StringComparison.Ordinal);
        Assert.Contains("new.md * [edit]", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Quitting_an_untouched_new_file_and_discarding_creates_nothing()
    {
        string path = Path.Combine(NewFolder(), "new.md");
        string? question = null;

        Session session = RunWith(
            NewFile(path),
            40,
            12,
            null,
            null,
            (app, window) =>
            {
                using var prompt = new DialogWatch(app, "the unsaved question", IsDialogTitled("Quit"), v => question = MessageOf(v), "Discard");
                window.NewKeyDownEvent(Key.Q.WithCtrl);
                prompt.Finish();
            });

        Assert.Equal("Save changes to new.md?", question);
        Assert.True(session.Window.Stopped);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("Create", true)]
    [InlineData("Cancel", false)]
    public void The_create_question_answers_Create_or_Cancel_and_creates_nothing(string answer, bool create)
    {
        string path = Path.Combine(NewFolder(), "new.md");
        bool? chosen = null;

        RunWith(
            NewFile(Path.Combine(NewFolder(), "existing.md")),
            40,
            12,
            null,
            null,
            (app, _) =>
            {
                using var prompt = new DialogWatch(app, "the create question", IsDialogTitled("New file"), v => Assert.Equal("Create new.md?", MessageOf(v)), answer);
                chosen = MdeditHost.AskNewFile(app, path);
                prompt.Finish();
            });

        Assert.Equal(create, chosen);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_path_that_names_neither_a_file_nor_a_folder_is_missing_and_an_existing_file_is_not()
    {
        string folder = NewFolder();
        string existing = Path.Combine(folder, "here.md");
        File.WriteAllText(existing, "x");

        Assert.True(MdeditHost.Missing(Path.Combine(folder, "gone.md")));
        Assert.False(MdeditHost.Missing(existing));
        Assert.False(MdeditHost.Missing(folder));
    }
}
