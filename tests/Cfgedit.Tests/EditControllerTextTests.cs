using System.Text;
using Cfgedit.Core;
using Cfgedit.Core.Files;
using Cfgedit.Core.Model;
using Cfgedit.Tui;
using Shared.Core;

namespace Cfgedit.Tests;

/// <summary>
/// <see cref="EditController"/> in text mode: a file that does not parse starts there, a reparse that changes the text is one undo step and
/// one that changes nothing is not, dirty is the bytes of the text against the bytes last loaded or saved, and text that does not parse is
/// saved only when the question is answered yes. Results are read from the file system.
/// </summary>
public sealed class EditControllerTextTests
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly IConfigFormat Json = Formats.FromName("json")!;

    [Fact]
    public void A_file_that_does_not_parse_starts_in_text_mode_with_its_text_and_is_clean()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\" 1}");

        Assert.Equal(EditMode.Text, controller.Mode);
        Assert.Equal("{\"a\" 1}", controller.Text!.Text);
        Assert.False(controller.IsDirty);
        Assert.Throws<InvalidOperationException>(() => controller.Document);
    }

    [Fact]
    public void Reparsing_unchanged_text_switches_to_the_tree_and_records_no_undo_step()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        controller.EnterText(0);

        NodePath? focus = controller.Reparse(out CfgeditException? error);

        Assert.Null(error);
        Assert.Equal(NodePath.Empty.Append(new ItemStep(0)), focus);
        Assert.Equal(EditMode.Tree, controller.Mode);
        Assert.False(controller.CanUndo);
        Assert.False(controller.IsDirty);
    }

    [Fact]
    public void Reparsing_edited_text_is_one_undo_step_and_undo_returns_the_text_and_clears_dirty()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        controller.EnterText(0);
        controller.SetText("{\"a\": 2}", 0);

        controller.Reparse(out CfgeditException? error);
        Assert.Null(error);
        Assert.True(controller.IsDirty);
        Assert.True(controller.CanUndo);

        controller.Undo();

        Assert.Equal("{\"a\": 1}\n", controller.Document.Source.Text);
        Assert.False(controller.IsDirty);
    }

    [Fact]
    public void Dirty_compares_the_text_bytes_with_the_file_in_text_mode()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\": 1}\n");
        controller.EnterText(0);

        controller.SetText("{\"a\": 1}\n ", 0);
        Assert.True(controller.IsDirty);

        controller.SetText("{\"a\": 1}\n", 0);
        Assert.False(controller.IsDirty);
    }

    [Fact]
    public void A_failing_reparse_keeps_the_text_mode_and_reports_the_error_with_its_line()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{}");
        controller.EnterText(0);
        controller.SetText("{\"a\" 1}", 5);

        NodePath? focus = controller.Reparse(out CfgeditException? error);

        Assert.Null(focus);
        Assert.NotNull(error);
        Assert.Equal(EditMode.Text, controller.Mode);
        Assert.StartsWith("1:6: ", controller.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_caret_offset_is_kept_as_given_in_a_CRLF_document()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\r\n\"a\": 1\r\n}");
        controller.EnterText(0);

        // Offset 6 is the third character of the second line, after the two-character CRLF.
        controller.SetText("{\r\n\"a\": 1\r\n}", 6);

        Assert.Equal("\r\n", controller.Newline);
        Assert.Equal(6, controller.CursorOffset);
        Assert.Equal("{\r\n\"a\": 1\r\n}", controller.Text!.Text);
    }

    [Fact]
    public void The_text_is_taken_verbatim_so_mixed_line_breaks_are_kept_and_the_text_is_not_dirty()
    {
        using var dir = new Scratch();
        const string text = "{\r\n  \"a\": 1,\n  \"b\": 2\r\n}";
        var controller = Open(dir, text);
        controller.EnterText(0);

        controller.SetText(text, 0);

        Assert.Equal(text, controller.Text!.Text);
        Assert.False(controller.IsDirty);
    }

    [Fact]
    public void Text_save_of_text_that_does_not_parse_asks_and_Cancel_writes_nothing()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\" 1}");
        CfgeditException? asked = null;

        SaveResult result = controller.TextSaveFlow(
            () => ExternalChangeChoice.Cancel,
            () => false,
            error =>
            {
                asked = error;
                return false;
            });

        Assert.Equal(SaveOutcome.Cancelled, result.Outcome);
        Assert.Equal(1, asked!.Line);
        Assert.Equal(6, asked.Column);
        Assert.Equal("{\"a\" 1}", File.ReadAllText(controller.File.Path, Utf8));
    }

    [Fact]
    public void Text_save_of_text_that_does_not_parse_writes_it_as_it_is_when_the_question_is_answered_yes()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\" 1}");

        SaveResult result = controller.TextSaveFlow(() => ExternalChangeChoice.Cancel, () => false, _ => true);

        Assert.Equal(SaveOutcome.Saved, result.Outcome);
        Assert.Equal("{\"a\" 1}", File.ReadAllText(controller.File.Path, Utf8));
        Assert.NotNull(controller.File.ParseError);
        Assert.False(controller.IsDirty);
        Assert.Equal(EditMode.Text, controller.Mode);
    }

    [Fact]
    public void Closing_with_unparsed_text_and_no_answer_to_the_question_refuses_the_save()
    {
        using var dir = new Scratch();
        var controller = Open(dir, "{\"a\" 1}");
        controller.SetText("{\"a\" 2}", 0);

        CloseResult result = controller.CloseFlow(() => UnsavedChoice.Save, () => ExternalChangeChoice.Cancel, () => false);

        Assert.Equal(CloseOutcome.Cancelled, result.Outcome);
        Assert.Equal("{\"a\" 1}", File.ReadAllText(controller.File.Path, Utf8));
    }

    private static EditController Open(Scratch dir, string text)
    {
        string path = dir.File("config.json");
        File.WriteAllText(path, text, Utf8);
        return new EditController(DocumentFile.Open(path, Json));
    }

    /// <summary>A temporary folder that is removed on disposal.</summary>
    private sealed class Scratch : IDisposable
    {
        public Scratch()
        {
            Path = Directory.CreateTempSubdirectory("cfgedit-text-").FullName;
        }

        public string Path { get; }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
