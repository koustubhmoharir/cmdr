using Cfgedit.Core.Files;
using Cfgedit.Core.Model;
using Cfgedit.Tui;

namespace Cfgedit.Tests;

/// <summary>
/// <see cref="EditController"/> flows, with fake confirmations: the save and quit flows, the dirty state of new files, and the focus
/// returned by undo and redo. Results are read from the file system, not through the controller.
/// </summary>
public sealed class EditControllerTests
{
    private static readonly IConfigFormat Json = Formats.FromName("json")!;

    // ----- Dirty state and single edits -----

    [Fact]
    public void A_new_file_is_dirty_until_its_first_save()
    {
        using var dir = new Scratch();
        string path = dir.File("new.json");
        var controller = new EditController(DocumentFile.CreateNew(path, Json, null));

        Assert.True(controller.IsDirty);
        Assert.Null(controller.Save());

        Assert.False(controller.IsDirty);
        Assert.True(controller.File.Exists);
        Assert.Equal("{}" + Environment.NewLine, File.ReadAllText(path));
        Assert.Equal("Saved", controller.Message);
    }

    [Fact]
    public void An_opened_file_is_clean_until_an_edit_and_clean_again_after_undo()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.False(controller.IsDirty);

        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        Assert.True(controller.IsDirty);

        controller.Undo();
        Assert.False(controller.IsDirty);
    }

    [Fact]
    public void Apply_records_one_undo_step_and_returns_the_result()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");

        EditResult? result = controller.Apply(SetValue("a", "2"), out string? error);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Same(result.Document, controller.Document);
        Assert.Equal("{\"a\": 2}\n", controller.Document.Source.Text);
        Assert.True(controller.CanUndo);
        Assert.False(controller.CanRedo);
    }

    [Fact]
    public void A_refused_edit_returns_the_error_and_keeps_the_document()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        ConfigDocument before = controller.Document;

        EditResult? result = controller.Apply(new DeleteNode(RootValue), out string? error);

        Assert.Null(result);
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Equal(error, controller.Message);
        Assert.Same(before, controller.Document);
        Assert.False(controller.IsDirty);
        Assert.False(controller.CanUndo);
    }

    // ----- Undo and redo -----

    [Fact]
    public void Undo_and_redo_return_the_focus_of_the_edit()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, new InsertNode(RootValue, 0, new NewJsonValue("x", JsonValueKind.Number, "5"))));

        NodePath? undone = controller.Undo();
        NodePath? redone = controller.Redo();

        Assert.Equal(RootValue, undone);
        Assert.Equal(RootValue.Append(new MemberStep("x", 0)), redone);
        Assert.Equal("{\"x\": 5, \"a\": 1}\n", controller.Document.Source.Text);
    }

    [Fact]
    public void Undo_with_nothing_to_undo_returns_null_and_says_so()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{}\n");

        Assert.Null(controller.Undo());
        Assert.Equal("Nothing to undo", controller.Message);
    }

    [Fact]
    public void Redo_with_nothing_to_redo_returns_null_and_says_so()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{}\n");

        Assert.Null(controller.Redo());
        Assert.Equal("Nothing to redo", controller.Message);
    }

    // ----- Reload -----

    [Fact]
    public void Reload_replaces_the_document_clears_the_history_and_leaves_nothing_dirty()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        File.WriteAllText(dir.File("config.json"), "{\"disk\": true}\n");

        string? error = controller.Reload();

        Assert.Null(error);
        Assert.Equal("{\"disk\": true}\n", controller.Document.Source.Text);
        Assert.False(controller.CanUndo);
        Assert.False(controller.IsDirty);
        Assert.Equal("Reloaded", controller.Message);
    }

    [Fact]
    public void Reload_of_a_file_that_is_gone_returns_the_error_and_keeps_the_document()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        ConfigDocument before = controller.Document;
        File.Delete(dir.File("config.json"));

        string? error = controller.Reload();

        Assert.NotNull(error);
        Assert.Equal(error, controller.Message);
        Assert.Same(before, controller.Document);
    }

    // ----- Save flow -----

    [Fact]
    public void SaveFlow_without_an_external_change_saves_and_asks_nothing()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        var asks = new Asks();

        SaveResult result = controller.SaveFlow(asks.External(ExternalChangeChoice.Cancel), asks.Reload(true));

        Assert.Equal(new SaveResult(SaveOutcome.Saved, null), result);
        Assert.Equal(0, asks.ExternalCount);
        Assert.Equal(0, asks.ReloadCount);
        Assert.Equal("{\"a\": 2}\n", File.ReadAllText(dir.File("config.json")));
        Assert.False(controller.IsDirty);
    }

    [Fact]
    public void SaveFlow_with_an_external_change_and_overwrite_writes_over_it()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        File.WriteAllText(dir.File("config.json"), "{\"external\": true}\n");
        var asks = new Asks();

        SaveResult result = controller.SaveFlow(asks.External(ExternalChangeChoice.Overwrite), asks.Reload(true));

        Assert.Equal(new SaveResult(SaveOutcome.Saved, null), result);
        Assert.Equal(1, asks.ExternalCount);
        Assert.Equal(0, asks.ReloadCount);
        Assert.Equal("{\"a\": 2}\n", File.ReadAllText(dir.File("config.json")));
        Assert.False(controller.IsDirty);
    }

    [Fact]
    public void SaveFlow_with_an_external_change_and_reload_confirmed_replaces_the_document()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        File.WriteAllText(dir.File("config.json"), "{\"external\": true}\n");
        var asks = new Asks();

        SaveResult result = controller.SaveFlow(asks.External(ExternalChangeChoice.Reload), asks.Reload(true));

        Assert.Equal(new SaveResult(SaveOutcome.Reloaded, null), result);
        Assert.Equal(1, asks.ReloadCount);
        Assert.Equal("{\"external\": true}\n", controller.Document.Source.Text);
        Assert.False(controller.CanUndo);
        Assert.False(controller.IsDirty);
        Assert.Equal("{\"external\": true}\n", File.ReadAllText(dir.File("config.json")));
    }

    [Fact]
    public void SaveFlow_with_an_external_change_and_reload_declined_keeps_everything()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        File.WriteAllText(dir.File("config.json"), "{\"external\": true}\n");
        var asks = new Asks();

        SaveResult result = controller.SaveFlow(asks.External(ExternalChangeChoice.Reload), asks.Reload(false));

        Assert.Equal(new SaveResult(SaveOutcome.Cancelled, null), result);
        Assert.Equal(1, asks.ReloadCount);
        Assert.Equal("{\"a\": 2}\n", controller.Document.Source.Text);
        Assert.True(controller.CanUndo);
        Assert.True(controller.IsDirty);
        Assert.Equal("{\"external\": true}\n", File.ReadAllText(dir.File("config.json")));
    }

    [Fact]
    public void SaveFlow_with_an_external_change_and_cancel_writes_nothing()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        File.WriteAllText(dir.File("config.json"), "{\"external\": true}\n");
        var asks = new Asks();

        SaveResult result = controller.SaveFlow(asks.External(ExternalChangeChoice.Cancel), asks.Reload(true));

        Assert.Equal(new SaveResult(SaveOutcome.Cancelled, null), result);
        Assert.Equal(0, asks.ReloadCount);
        Assert.True(controller.IsDirty);
        Assert.Equal("{\"external\": true}\n", File.ReadAllText(dir.File("config.json")));
    }

    [Fact]
    public void SaveFlow_for_a_new_file_that_someone_created_meanwhile_asks_before_overwriting()
    {
        using var dir = new Scratch();
        string path = dir.File("new.json");
        var controller = new EditController(DocumentFile.CreateNew(path, Json, null));
        File.WriteAllText(path, "{\"created\": true}\n");
        var asks = new Asks();

        SaveResult result = controller.SaveFlow(asks.External(ExternalChangeChoice.Overwrite), asks.Reload(true));

        Assert.Equal(new SaveResult(SaveOutcome.Saved, null), result);
        Assert.Equal(1, asks.ExternalCount);
        Assert.Equal("{}" + Environment.NewLine, File.ReadAllText(path));
    }

    [Fact]
    public void SaveFlow_whose_save_fails_returns_the_error_and_stays_dirty()
    {
        using var dir = new Scratch();
        string folder = dir.File("missing");
        var controller = new EditController(DocumentFile.CreateNew(System.IO.Path.Combine(folder, "new.json"), Json, null));
        var asks = new Asks();

        SaveResult result = controller.SaveFlow(asks.External(ExternalChangeChoice.Cancel), asks.Reload(true));

        Assert.Equal(SaveOutcome.Failed, result.Outcome);
        Assert.Contains("does not exist", result.Error);
        Assert.Equal(result.Error, controller.Message);
        Assert.Equal(0, asks.ExternalCount);
        Assert.True(controller.IsDirty);
    }

    // ----- Quit and switch flow -----

    [Fact]
    public void CloseFlow_of_a_clean_file_closes_without_asking()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{}\n");
        var asks = new Asks();

        CloseResult result = controller.CloseFlow(asks.Unsaved(UnsavedChoice.Cancel), asks.External(ExternalChangeChoice.Cancel), asks.Reload(true));

        Assert.Equal(new CloseResult(CloseOutcome.Closed, null), result);
        Assert.Equal(0, asks.UnsavedCount);
    }

    [Fact]
    public void CloseFlow_of_a_dirty_file_with_discard_closes_and_leaves_the_file_as_it_was()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        var asks = new Asks();

        CloseResult result = controller.CloseFlow(asks.Unsaved(UnsavedChoice.Discard), asks.External(ExternalChangeChoice.Cancel), asks.Reload(true));

        Assert.Equal(new CloseResult(CloseOutcome.Closed, null), result);
        Assert.Equal(1, asks.UnsavedCount);
        Assert.Equal("{\"a\": 1}\n", File.ReadAllText(dir.File("config.json")));
    }

    [Fact]
    public void CloseFlow_of_a_dirty_file_with_cancel_stays_open_and_dirty()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        var asks = new Asks();

        CloseResult result = controller.CloseFlow(asks.Unsaved(UnsavedChoice.Cancel), asks.External(ExternalChangeChoice.Cancel), asks.Reload(true));

        Assert.Equal(new CloseResult(CloseOutcome.Cancelled, null), result);
        Assert.True(controller.IsDirty);
        Assert.Equal("{\"a\": 1}\n", File.ReadAllText(dir.File("config.json")));
    }

    [Fact]
    public void CloseFlow_of_a_dirty_file_with_save_saves_and_closes()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        var asks = new Asks();

        CloseResult result = controller.CloseFlow(asks.Unsaved(UnsavedChoice.Save), asks.External(ExternalChangeChoice.Cancel), asks.Reload(true));

        Assert.Equal(new CloseResult(CloseOutcome.Closed, null), result);
        Assert.Equal("{\"a\": 2}\n", File.ReadAllText(dir.File("config.json")));
    }

    [Fact]
    public void CloseFlow_with_save_that_fails_stays_open_with_the_error()
    {
        using var dir = new Scratch();
        var controller = new EditController(DocumentFile.CreateNew(System.IO.Path.Combine(dir.File("missing"), "new.json"), Json, null));
        var asks = new Asks();

        CloseResult result = controller.CloseFlow(asks.Unsaved(UnsavedChoice.Save), asks.External(ExternalChangeChoice.Cancel), asks.Reload(true));

        Assert.Equal(CloseOutcome.Failed, result.Outcome);
        Assert.Contains("does not exist", result.Error);
        Assert.True(controller.IsDirty);
    }

    [Fact]
    public void CloseFlow_with_save_and_an_external_change_that_is_cancelled_stays_open()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        File.WriteAllText(dir.File("config.json"), "{\"external\": true}\n");
        var asks = new Asks();

        CloseResult result = controller.CloseFlow(asks.Unsaved(UnsavedChoice.Save), asks.External(ExternalChangeChoice.Cancel), asks.Reload(true));

        Assert.Equal(new CloseResult(CloseOutcome.Cancelled, null), result);
        Assert.Equal("{\"external\": true}\n", File.ReadAllText(dir.File("config.json")));
    }

    [Fact]
    public void CloseFlow_with_save_and_a_reload_keeps_the_file_open_with_the_disk_content()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        Assert.NotNull(Apply(controller, SetValue("a", "2")));
        File.WriteAllText(dir.File("config.json"), "{\"external\": true}\n");
        var asks = new Asks();

        CloseResult result = controller.CloseFlow(asks.Unsaved(UnsavedChoice.Save), asks.External(ExternalChangeChoice.Reload), asks.Reload(true));

        Assert.Equal(new CloseResult(CloseOutcome.Reloaded, null), result);
        Assert.Equal("{\"external\": true}\n", controller.Document.Source.Text);
    }

    [Fact]
    public void CloseFlow_of_a_new_file_that_was_never_saved_asks_and_discarding_creates_nothing()
    {
        using var dir = new Scratch();
        string path = dir.File("new.json");
        var controller = new EditController(DocumentFile.CreateNew(path, Json, null));
        var asks = new Asks();

        CloseResult result = controller.CloseFlow(asks.Unsaved(UnsavedChoice.Discard), asks.External(ExternalChangeChoice.Cancel), asks.Reload(true));

        Assert.Equal(new CloseResult(CloseOutcome.Closed, null), result);
        Assert.Equal(1, asks.UnsavedCount);
        Assert.False(File.Exists(path));
    }

    // ----- Helpers -----

    private static readonly NodePath RootValue = NodePath.Empty.Append(new ItemStep(0));

    private static EditController Open(Scratch dir, string text)
    {
        string path = dir.File("config.json");
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

    /// <summary>Fake confirmations. Each one returns its fixed answer and counts how often it was asked.</summary>
    private sealed class Asks
    {
        public int ExternalCount { get; private set; }

        public int ReloadCount { get; private set; }

        public int UnsavedCount { get; private set; }

        public Func<ExternalChangeChoice> External(ExternalChangeChoice answer)
        {
            return () =>
            {
                ExternalCount++;
                return answer;
            };
        }

        public Func<bool> Reload(bool answer)
        {
            return () =>
            {
                ReloadCount++;
                return answer;
            };
        }

        public Func<UnsavedChoice> Unsaved(UnsavedChoice answer)
        {
            return () =>
            {
                UnsavedCount++;
                return answer;
            };
        }
    }

    /// <summary>A temporary folder that is removed on disposal.</summary>
    private sealed class Scratch : IDisposable
    {
        public Scratch()
        {
            Path = Directory.CreateTempSubdirectory("cfgedit-tests-edit-").FullName;
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
