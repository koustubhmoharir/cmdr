using Cfgedit.Core.Files;
using Cfgedit.Core.Model;
using Cfgedit.Tui;

namespace Cfgedit.Tests;

/// <summary>
/// <see cref="EditController.CheckExternal"/>: a change to the file on disk while it is open. A clean document reloads without asking, a
/// dirty one asks Reload or Keep mine, a deleted file asks Keep or Close, and a failed read is reported once. Results are read from the
/// file system and the document, not through the controller's own state where a file oracle exists.
/// </summary>
public sealed class EditControllerExternalTests
{
    private static readonly IConfigFormat Json = Formats.FromName("json")!;
    private static readonly NodePath RootValue = NodePath.Empty.Append(new ItemStep(0));

    // ----- No change, and a clean document -----

    [Fact]
    public void Without_a_change_the_check_returns_None_and_asks_nothing()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        var asks = new Asks();

        ExternalResult result = controller.CheckExternal(null, asks.Reload(true), asks.Confirm(true));

        Assert.Equal(ExternalOutcome.None, result.Outcome);
        Assert.Equal(0, asks.Count);
    }

    [Fact]
    public void A_clean_document_reloads_without_asking_and_is_quiet_when_there_was_no_history()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        var asks = new Asks();
        File.WriteAllText(controller.File.Path, "{\"a\": 22}\n");

        ExternalResult result = controller.CheckExternal(null, asks.Reload(false), asks.Confirm(true));

        Assert.Equal(ExternalOutcome.Reloaded, result.Outcome);
        Assert.True(result.Quiet);
        Assert.False(result.DiscardedText);
        Assert.Equal(0, asks.Count);
        Assert.Equal("{\"a\": 22}\n", controller.Document.Source.Text);
        Assert.False(controller.IsDirty);
        Assert.Null(controller.Message);
    }

    [Fact]
    public void A_clean_document_with_history_reloads_without_asking_and_says_so_and_undo_restores_the_old_text()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        Assert.Null(controller.Save());
        File.WriteAllText(controller.File.Path, "{\"a\": 22}\n");
        var asks = new Asks();

        ExternalResult result = controller.CheckExternal(null, asks.Reload(false), asks.Confirm(true));

        Assert.Equal(ExternalOutcome.Reloaded, result.Outcome);
        Assert.False(result.Quiet);
        Assert.Equal(0, asks.Count);
        Assert.Equal("Reloaded: the file changed on disk", controller.Message);
        Assert.False(controller.IsDirty);

        controller.Undo();

        Assert.Equal("{\"a\": 2}\n", controller.Document.Source.Text);
        Assert.True(controller.IsDirty);
    }

    // ----- A dirty document -----

    [Fact]
    public void A_dirty_document_asks_and_Reload_is_an_undo_step_so_undo_restores_the_edited_version()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        File.WriteAllText(controller.File.Path, "{\"a\": 22}\n");
        var asks = new Asks();
        bool discardsArgument = true;

        ExternalResult result = controller.CheckExternal(
            null,
            discards =>
            {
                discardsArgument = discards;
                return asks.Reload(true)(discards);
            },
            asks.Confirm(true));

        Assert.Equal(ExternalOutcome.Reloaded, result.Outcome);
        Assert.False(result.Quiet);
        Assert.False(result.DiscardedText);
        Assert.False(discardsArgument); // tree mode: the reload is an undo step, so nothing is discarded
        Assert.Equal("{\"a\": 22}\n", controller.Document.Source.Text);
        Assert.False(controller.IsDirty);

        controller.Undo();

        Assert.Equal("{\"a\": 2}\n", controller.Document.Source.Text);
        Assert.True(controller.IsDirty);
    }

    [Fact]
    public void A_dirty_document_with_Keep_mine_keeps_the_edits_and_the_next_save_writes_without_asking()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        File.WriteAllText(controller.File.Path, "{\"a\": 22}\n");
        var asks = new Asks();

        ExternalResult result = controller.CheckExternal(null, asks.Reload(false), asks.Confirm(true));

        Assert.Equal(ExternalOutcome.KeptMine, result.Outcome);
        Assert.Equal(1, asks.Count);
        Assert.Equal("{\"a\": 2}\n", controller.Document.Source.Text);
        Assert.True(controller.IsDirty);

        var saveAsks = new Asks();
        SaveResult save = controller.SaveFlow(saveAsks.External(ExternalChangeChoice.Cancel), saveAsks.Confirm(true));

        Assert.Equal(new SaveResult(SaveOutcome.Saved, null), save);
        Assert.Equal(0, saveAsks.Count);
        Assert.Equal("{\"a\": 2}\n", File.ReadAllText(controller.File.Path));
    }

    [Fact]
    public void After_Keep_mine_undoing_every_edit_leaves_the_document_unsaved_because_the_disk_has_other_content()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        File.WriteAllText(controller.File.Path, "{\"a\": 22}\n");
        controller.CheckExternal(null, new Asks().Reload(false), new Asks().Confirm(true));

        Assert.NotNull(controller.Undo());

        // The document is the text first opened, which is no longer what the file holds.
        Assert.Equal("{\"a\": 1}\n", controller.Document.Source.Text);
        Assert.Equal("{\"a\": 22}\n", File.ReadAllText(controller.File.Path));
        Assert.True(controller.IsDirty);
    }

    [Fact]
    public void After_Keep_mine_a_document_equal_to_the_disk_content_is_saved()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "22")));
        File.WriteAllText(controller.File.Path, "{\"a\": 22}\n");

        controller.CheckExternal(null, new Asks().Reload(false), new Asks().Confirm(true));

        Assert.False(controller.IsDirty);
    }

    [Fact]
    public void After_Keep_mine_another_change_asks_again()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        File.WriteAllText(controller.File.Path, "{\"a\": 22}\n");
        var asks = new Asks();
        controller.CheckExternal(null, asks.Reload(false), asks.Confirm(true));

        File.WriteAllText(controller.File.Path, "{\"a\": 333}\n");
        ExternalResult result = controller.CheckExternal(null, asks.Reload(false), asks.Confirm(true));

        Assert.Equal(ExternalOutcome.KeptMine, result.Outcome);
        Assert.Equal(2, asks.Count);

        // The second change was kept too, so its content is accepted and a save does not ask about it.
        var saveAsks = new Asks();
        SaveResult save = controller.SaveFlow(saveAsks.External(ExternalChangeChoice.Cancel), saveAsks.Confirm(true));
        Assert.Equal(new SaveResult(SaveOutcome.Saved, null), save);
        Assert.Equal(0, saveAsks.Count);

        // A change after that is not accepted, so the next save asks again.
        File.WriteAllText(controller.File.Path, "{\"a\": 4444, \"b\": 4}\n");
        SaveResult again = controller.SaveFlow(saveAsks.External(ExternalChangeChoice.Cancel), saveAsks.Confirm(true));

        Assert.Equal(SaveOutcome.Cancelled, again.Outcome);
        Assert.Equal(1, saveAsks.Count);
        Assert.Equal("{\"a\": 4444, \"b\": 4}\n", File.ReadAllText(controller.File.Path));
    }

    [Fact]
    public void A_change_that_is_reported_once_is_not_reported_again_by_the_next_check()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        File.WriteAllText(controller.File.Path, "{\"a\": 22}\n");
        var asks = new Asks();
        controller.CheckExternal(null, asks.Reload(false), asks.Confirm(true));

        ExternalResult again = controller.CheckExternal(null, asks.Reload(false), asks.Confirm(true));

        Assert.Equal(ExternalOutcome.None, again.Outcome);
        Assert.Equal(1, asks.Count);
    }

    [Fact]
    public void Reload_restores_the_focus_when_the_path_still_resolves_and_gives_null_when_it_does_not()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1, \"b\": 2}\n");
        NodePath focus = RootValue.Append(new MemberStep("a", 0));
        File.WriteAllText(controller.File.Path, "{\"a\": 55, \"b\": 2}\n");

        ExternalResult reloaded = controller.CheckExternal(focus, _ => true, () => true);

        Assert.Equal(focus, reloaded.Focus);

        var other = Open(dir, "{\"a\": 1}\n", "other.json");
        File.WriteAllText(other.File.Path, "{\"zz\": 1, \"yy\": 2}\n");

        ExternalResult missing = other.CheckExternal(focus, _ => true, () => true);

        Assert.Equal(ExternalOutcome.Reloaded, missing.Outcome);
        Assert.Null(missing.Focus);
    }

    // ----- A deleted file -----

    [Fact]
    public void A_deleted_file_with_Keep_becomes_an_unsaved_new_file_and_the_next_save_recreates_it()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        File.Delete(controller.File.Path);
        var asks = new Asks();

        ExternalResult result = controller.CheckExternal(null, asks.Reload(true), asks.Confirm(true));

        Assert.Equal(ExternalOutcome.KeptDeleted, result.Outcome);
        Assert.Equal(1, asks.Count);
        Assert.False(controller.File.Exists);
        Assert.True(controller.IsDirty);
        Assert.Equal("The file was deleted on disk", controller.Message);

        var saveAsks = new Asks();
        SaveResult save = controller.SaveFlow(saveAsks.External(ExternalChangeChoice.Cancel), saveAsks.Confirm(true));

        Assert.Equal(new SaveResult(SaveOutcome.Saved, null), save);
        Assert.Equal(0, saveAsks.Count);
        Assert.True(File.Exists(controller.File.Path));
        Assert.Equal("{\"a\": 1}\n", File.ReadAllText(controller.File.Path));
    }

    [Fact]
    public void A_deleted_file_with_Close_changes_nothing_and_is_reported_once()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        File.Delete(controller.File.Path);
        var asks = new Asks();

        ExternalResult result = controller.CheckExternal(null, asks.Reload(true), asks.Confirm(false));

        Assert.Equal(ExternalOutcome.Close, result.Outcome);
        Assert.True(controller.File.Exists);
        Assert.False(controller.IsDirty);
        Assert.Equal("{\"a\": 1}\n", controller.Document.Source.Text);
        Assert.Null(controller.Message);
        Assert.Equal(ExternalOutcome.None, controller.CheckExternal(null, asks.Reload(true), asks.Confirm(false)).Outcome);
        Assert.Equal(1, asks.Count);
    }

    [Fact]
    public void A_new_file_that_appears_on_disk_counts_as_a_change_and_asks_for_a_dirty_document()
    {
        using var dir = new Scratch();
        var controller = new EditController(DocumentFile.CreateNew(dir.File("new.json"), Json, null));
        File.WriteAllText(controller.File.Path, "{\"x\": 1}\n");
        var asks = new Asks();

        ExternalResult result = controller.CheckExternal(null, asks.Reload(true), asks.Confirm(true));

        Assert.Equal(ExternalOutcome.Reloaded, result.Outcome);
        Assert.Equal(1, asks.Count);
        Assert.True(controller.File.Exists);
        Assert.Equal("{\"x\": 1}\n", controller.Document.Source.Text);
        Assert.False(controller.IsDirty);
    }

    // ----- Text mode -----

    [Fact]
    public void Text_mode_reload_to_a_parsing_text_goes_to_tree_mode_without_asking_when_the_text_was_clean()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\" 1}\n");
        Assert.Equal(EditMode.Text, controller.Mode);
        File.WriteAllText(controller.File.Path, "{\"a\": 2}\n");
        var asks = new Asks();

        ExternalResult result = controller.CheckExternal(null, asks.Reload(true), asks.Confirm(true));

        Assert.Equal(ExternalOutcome.Reloaded, result.Outcome);
        Assert.True(result.Quiet);
        Assert.False(result.DiscardedText);
        Assert.Equal(0, asks.Count);
        Assert.Equal(EditMode.Tree, controller.Mode);
        Assert.Equal("{\"a\": 2}\n", controller.Document.Source.Text);
    }

    [Fact]
    public void Text_mode_dirty_reload_says_it_discards_the_typed_text_and_asks_with_that_flag()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\" 1}\n");
        controller.EnterText(0);
        controller.SetText("{\"a\" 3}\n", 0);
        File.WriteAllText(controller.File.Path, "{\"a\": 2}\n");
        bool discardsArgument = false;

        ExternalResult result = controller.CheckExternal(
            null,
            discards =>
            {
                discardsArgument = discards;
                return true;
            },
            () => true);

        Assert.True(discardsArgument);
        Assert.Equal(ExternalOutcome.Reloaded, result.Outcome);
        Assert.True(result.DiscardedText);
        Assert.Equal(EditMode.Tree, controller.Mode);
        Assert.Equal("{\"a\": 2}\n", controller.Document.Source.Text);
    }

    [Fact]
    public void Text_mode_reload_to_a_text_that_does_not_parse_stays_in_text_mode_and_keeps_the_history()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        controller.EnterText(0);
        File.WriteAllText(controller.File.Path, "{\"a\" 9\n");
        var asks = new Asks();

        ExternalResult result = controller.CheckExternal(null, asks.Reload(true), asks.Confirm(true));

        Assert.Equal(ExternalOutcome.Reloaded, result.Outcome);
        Assert.Equal(EditMode.Text, controller.Mode);
        Assert.Equal("{\"a\" 9\n", controller.Text!.Text);
        Assert.NotNull(controller.File.ParseError);

        File.WriteAllText(controller.File.Path, "{\"b\": 1, \"c\": 2}\n");
        ExternalResult back = controller.CheckExternal(null, asks.Reload(true), asks.Confirm(true));

        Assert.Equal(ExternalOutcome.Reloaded, back.Outcome);
        Assert.Equal(EditMode.Tree, controller.Mode);
        Assert.True(controller.CanUndo);
        controller.Undo();
        Assert.Equal("{\"a\": 2}\n", controller.Document.Source.Text);
    }

    // ----- Failure -----

    [Fact]
    public void A_file_that_cannot_be_read_is_reported_once_with_its_error()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        File.Delete(controller.File.Path);
        Directory.CreateDirectory(controller.File.Path);
        var asks = new Asks();

        ExternalResult first = controller.CheckExternal(null, asks.Reload(true), asks.Confirm(true));
        ExternalResult second = controller.CheckExternal(null, asks.Reload(true), asks.Confirm(true));

        Assert.Equal(ExternalOutcome.Failed, first.Outcome);
        Assert.Contains("is a folder", first.Error);
        Assert.Equal(first.Error, controller.Message);
        Assert.Equal(ExternalOutcome.None, second.Outcome);
        Assert.Equal(0, asks.Count);
    }

    // ----- Helpers -----

    private static EditController Open(Scratch dir, string text, string name = "config.json")
    {
        string path = dir.File(name);
        File.WriteAllText(path, text);
        return new EditController(DocumentFile.Open(path, Json));
    }

    private static EditResult? Apply(EditController controller, EditOp op)
    {
        return controller.Apply(op, out string? error) ?? throw new InvalidOperationException("The edit was refused: " + error);
    }

    private static SetPart SetValue(string key, string value)
    {
        return new SetPart(RootValue.Append(new MemberStep(key, 0)), PartKind.Value, -1, value);
    }

    /// <summary>Fake confirmations. Each one returns its fixed answer and counts how often any of them was asked.</summary>
    private sealed class Asks
    {
        public int Count { get; private set; }

        /// <summary>The question of a changed, dirty document: Reload (true) or Keep mine (false).</summary>
        public Func<bool, bool> Reload(bool answer)
        {
            return _ =>
            {
                Count++;
                return answer;
            };
        }

        /// <summary>A yes-or-no confirmation: Keep or Close for a deleted file, and the save flow's reload confirmation.</summary>
        public Func<bool> Confirm(bool answer)
        {
            return () =>
            {
                Count++;
                return answer;
            };
        }

        public Func<ExternalChangeChoice> External(ExternalChangeChoice answer)
        {
            return () =>
            {
                Count++;
                return answer;
            };
        }
    }

    /// <summary>A temporary folder that is removed on disposal.</summary>
    private sealed class Scratch : IDisposable
    {
        public Scratch()
        {
            Path = Directory.CreateTempSubdirectory("cfgedit-tests-external-").FullName;
        }

        public string Path { get; }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: a folder left behind is only temporary data.
            }
        }
    }
}
