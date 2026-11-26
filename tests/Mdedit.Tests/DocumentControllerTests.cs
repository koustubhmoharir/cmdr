using System.Text;
using Mdedit.Tui;
using Shared.Core;

namespace Mdedit.Tests;

/// <summary>
/// The file decisions without a terminal: the save's bytes and its question about a changed file, the unsaved-changes flow of a quit or a
/// switch, the check for a change on disk with each answer, and the one replacement that turns the buffer into the file's text. The answers
/// are delegates, and a delegate that must not be asked fails the test.
/// </summary>
public sealed class DocumentControllerTests : IDisposable
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mdedit-controller-{Guid.NewGuid():N}");

    [Fact]
    public void A_save_writes_the_edited_text_and_keeps_the_BOM_and_the_CRLF_line_breaks_of_the_other_lines()
    {
        string path = Write("notes.md", Bom.Concat(Utf8("a\r\nb\r\n")).ToArray());
        var controller = Controller(path);

        SaveResult result = controller.SaveFlow("a\r\nX\r\n", Unasked<ExternalChangeChoice>());

        Assert.Equal(SaveOutcome.Saved, result.Outcome);
        Assert.Null(result.Error);
        Assert.Equal(Bom.Concat(Utf8("a\r\nX\r\n")).ToArray(), File.ReadAllBytes(path));
        Assert.False(controller.IsDirty("a\r\nX\r\n"));
    }

    [Fact]
    public void A_save_asks_only_when_the_file_changed_on_disk_and_Overwrite_writes_over_it()
    {
        string path = Write("notes.md", Utf8("first\n"));
        var controller = Controller(path);
        File.WriteAllText(path, "someone else\n");
        int asked = 0;

        SaveResult result = controller.SaveFlow("mine\n", () =>
        {
            asked++;
            return ExternalChangeChoice.Overwrite;
        });

        Assert.Equal(1, asked);
        Assert.Equal(SaveOutcome.Saved, result.Outcome);
        Assert.Equal("mine\n", File.ReadAllText(path));
    }

    [Fact]
    public void Cancel_on_the_changed_file_question_writes_nothing()
    {
        string path = Write("notes.md", Utf8("first\n"));
        var controller = Controller(path);
        File.WriteAllText(path, "someone else\n");

        SaveResult result = controller.SaveFlow("mine\n", () => ExternalChangeChoice.Cancel);

        Assert.Equal(SaveOutcome.Cancelled, result.Outcome);
        Assert.Equal("someone else\n", File.ReadAllText(path));
    }

    [Fact]
    public void Reload_on_the_changed_file_question_reads_the_file_and_writes_nothing()
    {
        string path = Write("notes.md", Utf8("first\n"));
        var controller = Controller(path);
        File.WriteAllText(path, "someone else\n");

        SaveResult result = controller.SaveFlow("mine\n", () => ExternalChangeChoice.Reload);

        Assert.Equal(SaveOutcome.Reloaded, result.Outcome);
        Assert.Equal("someone else\n", controller.File.Text.Text);
        Assert.Equal("someone else\n", File.ReadAllText(path));
    }

    [Fact]
    public void A_save_into_a_missing_folder_fails_with_the_folder_named_and_the_document_stays_dirty()
    {
        string path = Path.Combine(_dir, "missing", "new.md");
        var controller = new DocumentController(TextFile.CreateNew(path, SourceText.Create(string.Empty, Utf8NoBom, hasBom: false)));

        SaveResult result = controller.SaveFlow("text\n", Unasked<ExternalChangeChoice>());

        Assert.Equal(SaveOutcome.Failed, result.Outcome);
        Assert.Contains("does not exist", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
        Assert.True(controller.IsDirty("text\n"));
    }

    [Fact]
    public void A_new_file_is_dirty_until_its_first_save_even_when_its_text_is_empty()
    {
        string path = Path.Combine(_dir, "new.md");
        Directory.CreateDirectory(_dir);
        var controller = new DocumentController(TextFile.CreateNew(path, SourceText.Create(string.Empty, Utf8NoBom, hasBom: false)));

        Assert.True(controller.IsDirty(string.Empty));

        controller.SaveFlow(string.Empty, Unasked<ExternalChangeChoice>());

        Assert.False(controller.IsDirty(string.Empty));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void A_clean_document_closes_without_asking()
    {
        string path = Write("notes.md", Utf8("text\n"));

        CloseResult result = Controller(path).CloseFlow("text\n", Unasked<UnsavedChoice>(), Unasked<ExternalChangeChoice>());

        Assert.Equal(CloseOutcome.Closed, result.Outcome);
    }

    [Theory]
    [InlineData("Discard", "Closed", "first\n")]
    [InlineData("Cancel", "Cancelled", "first\n")]
    [InlineData("Save", "Closed", "edited\n")]
    public void A_dirty_document_asks_and_each_answer_closes_or_keeps_it(string answer, string expected, string fileText)
    {
        string path = Write("notes.md", Utf8("first\n"));
        UnsavedChoice choice = Enum.Parse<UnsavedChoice>(answer);

        CloseResult result = Controller(path).CloseFlow("edited\n", () => choice, Unasked<ExternalChangeChoice>());

        Assert.Equal(Enum.Parse<CloseOutcome>(expected), result.Outcome);
        Assert.Equal(fileText, File.ReadAllText(path));
    }

    [Fact]
    public void A_save_that_fails_on_close_keeps_the_document_open_with_its_error()
    {
        string path = Path.Combine(_dir, "missing", "new.md");
        var controller = new DocumentController(TextFile.CreateNew(path, SourceText.Create(string.Empty, Utf8NoBom, hasBom: false)));

        CloseResult result = controller.CloseFlow("text\n", () => UnsavedChoice.Save, Unasked<ExternalChangeChoice>());

        Assert.Equal(CloseOutcome.Failed, result.Outcome);
        Assert.Contains("does not exist", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_clean_file_changed_on_disk_reloads_without_asking()
    {
        string path = Write("notes.md", Utf8("first\n"));
        var controller = Controller(path);
        File.WriteAllText(path, "second\n");

        ExternalResult result = controller.CheckExternal(() => false, Unasked<bool>(), Unasked<bool>());

        Assert.Equal(ExternalOutcome.Reloaded, result.Outcome);
        Assert.Equal("second\n", controller.File.Text.Text);
    }

    [Fact]
    public void Without_a_change_the_check_answers_None_and_asks_nothing()
    {
        string path = Write("notes.md", Utf8("first\n"));

        ExternalResult result = Controller(path).CheckExternal(Unasked<bool>(), Unasked<bool>(), Unasked<bool>());

        Assert.Equal(ExternalOutcome.None, result.Outcome);
    }

    [Fact]
    public void A_dirty_document_with_Keep_mine_keeps_the_text_and_the_next_save_does_not_ask()
    {
        string path = Write("notes.md", Utf8("first\n"));
        var controller = Controller(path);
        File.WriteAllText(path, "second\n");

        ExternalResult result = controller.CheckExternal(() => true, () => false, Unasked<bool>());

        Assert.Equal(ExternalOutcome.KeptMine, result.Outcome);
        Assert.True(controller.IsDirty("edited\n"));
        Assert.Equal(SaveOutcome.Saved, controller.SaveFlow("edited\n", Unasked<ExternalChangeChoice>()).Outcome);
        Assert.Equal("edited\n", File.ReadAllText(path));
    }

    [Fact]
    public void A_dirty_document_with_Reload_reads_the_file()
    {
        string path = Write("notes.md", Utf8("first\n"));
        var controller = Controller(path);
        File.WriteAllText(path, "second\n");

        ExternalResult result = controller.CheckExternal(() => true, () => true, Unasked<bool>());

        Assert.Equal(ExternalOutcome.Reloaded, result.Outcome);
        Assert.Equal("second\n", controller.File.Text.Text);
    }

    [Fact]
    public void A_deleted_file_with_Keep_becomes_an_unsaved_new_file_and_the_next_save_creates_it()
    {
        string path = Write("notes.md", Utf8("first\n"));
        var controller = Controller(path);
        File.Delete(path);

        ExternalResult result = controller.CheckExternal(() => true, Unasked<bool>(), () => true);

        Assert.Equal(ExternalOutcome.KeptDeleted, result.Outcome);
        Assert.False(controller.File.Exists);
        Assert.True(controller.IsDirty("first\n"));
        Assert.Equal(SaveOutcome.Saved, controller.SaveFlow("first\n", Unasked<ExternalChangeChoice>()).Outcome);
        Assert.Equal("first\n", File.ReadAllText(path));
    }

    [Fact]
    public void A_deleted_file_with_Close_changes_nothing_and_says_close()
    {
        string path = Write("notes.md", Utf8("first\n"));
        var controller = Controller(path);
        File.Delete(path);

        ExternalResult result = controller.CheckExternal(() => true, Unasked<bool>(), () => false);

        Assert.Equal(ExternalOutcome.Close, result.Outcome);
        Assert.True(controller.File.Exists);
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_reported_once_and_then_the_check_answers_None()
    {
        string path = Write("notes.md", Utf8("first\n"));
        var controller = Controller(path);
        File.Delete(path);
        Directory.CreateDirectory(path);

        ExternalResult first = controller.CheckExternal(Unasked<bool>(), Unasked<bool>(), Unasked<bool>());
        ExternalResult second = controller.CheckExternal(Unasked<bool>(), Unasked<bool>(), Unasked<bool>());

        Assert.Equal(ExternalOutcome.Failed, first.Outcome);
        Assert.Contains("is a folder", first.Error, StringComparison.Ordinal);
        Assert.Equal(ExternalOutcome.None, second.Outcome);
    }

    [Theory]
    [InlineData("abc", "abc", 3, 0, "")]
    [InlineData("abc", "abXc", 2, 0, "X")]
    [InlineData("abXc", "abc", 2, 1, "")]
    [InlineData("one two three", "one 2 three", 4, 3, "2")]
    [InlineData("", "new", 0, 0, "new")]
    [InlineData("gone", "", 0, 4, "")]
    public void The_replacement_keeps_the_text_around_the_first_and_last_difference(string current, string target, int start, int removed, string inserted)
    {
        Assert.Equal((start, removed, inserted), MainWindow.DiffMiddle(current, target));
    }

    [Fact]
    public void The_replacement_never_splits_a_surrogate_pair()
    {
        // The two emoji share their high surrogate, so the difference starts at its low surrogate: the pair is replaced whole.
        (int start, int removed, string inserted) = MainWindow.DiffMiddle("a\U0001F600b", "a\U0001F601b");

        Assert.Equal(1, start);
        Assert.Equal("\U0001F601", inserted);
        Assert.Equal(2, removed);
    }

    private DocumentController Controller(string path) => new(TextFile.Open(path, (bytes, full) => SourceText.Decode(bytes, full)));

    private static Func<T> Unasked<T>() => () => throw new InvalidOperationException("This question must not be asked.");

    private static UTF8Encoding Utf8NoBom => new(encoderShouldEmitUTF8Identifier: false);

    private static byte[] Utf8(string text) => Utf8NoBom.GetBytes(text);

    private string Write(string name, byte[] bytes)
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
