using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.Editor.Highlighting;
using Terminal.Gui.Input;

namespace Mdedit.Tests;

/// <summary>
/// The source editor on its own: the Markdown highlighting it is given, word wrap and Alt+Z, the line breaks, the version and the undo
/// history of its buffer, the caret and selection placement, and the tracked positions the buffer gives the view.
/// </summary>
[Collection("Terminal GUI")]
public sealed class SourceEditorTests
{
    [Fact]
    public void The_highlighting_is_the_built_in_Markdown_definition_by_name_and_by_extension()
    {
        var editor = new SourceEditor();

        Assert.NotNull(HighlightingManager.Instance.GetDefinition(SourceEditor.HighlightingName));
        Assert.Same(HighlightingManager.Instance.GetDefinition(SourceEditor.HighlightingName), editor.HighlightingDefinition);
        Assert.Same(editor.HighlightingDefinition, HighlightingManager.Instance.GetDefinitionByExtension(".md"));
    }

    [Fact]
    public void Word_wrap_is_on_and_Alt_Z_toggles_it()
    {
        var editor = new SourceEditor();
        Assert.True(editor.WordWrap);

        editor.NewKeyDownEvent(Key.Z.WithAlt);
        Assert.False(editor.WordWrap);

        editor.NewKeyDownEvent(Key.Z.WithAlt);
        Assert.True(editor.WordWrap);
    }

    [Fact]
    public void Text_is_set_exactly_as_given_with_its_own_line_breaks()
    {
        const string crlf = "# Title\r\n\r\nBody\r\n";
        var editor = new SourceEditor { LineBreak = "\r\n" };

        editor.Text = crlf;

        Assert.Equal(crlf, editor.Buffer.Text);
    }

    [Fact]
    public void Enter_inserts_the_line_break_of_the_editor()
    {
        var editor = new SourceEditor { LineBreak = "\r\n" };
        editor.Text = "ab";
        editor.CaretOffset = 1;

        editor.NewKeyDownEvent(Key.Enter);

        Assert.Equal("a\r\nb", editor.Buffer.Text);
        Assert.Equal(3, editor.CaretOffset);
    }

    [Fact]
    public void The_version_counts_each_change_including_undo_and_redo()
    {
        var editor = new SourceEditor { Text = "abc" };
        int start = editor.Version;

        editor.Buffer.Replace(3, 0, "d");
        Assert.Equal(start + 1, editor.Version);

        editor.Undo(redo: false);
        Assert.Equal(start + 2, editor.Version);

        editor.Undo(redo: true);
        Assert.Equal(start + 3, editor.Version);
    }

    [Fact]
    public void Undo_reports_the_offset_just_after_the_first_restored_text_and_redo_the_end_of_the_text_it_inserts()
    {
        var editor = new SourceEditor { Text = "abcdef" };
        editor.Buffer.UndoStack.ClearAll();
        editor.Buffer.Replace(2, 0, "XYZ");

        // Undoing the insertion removes it: the place of the removal is the offset of the caret.
        int? undone = editor.Undo(redo: false);
        Assert.Equal(2, undone);
        Assert.Equal("abcdef", editor.Buffer.Text);

        // Redoing it inserts the text again: the caret goes after it.
        int? redone = editor.Undo(redo: true);
        Assert.Equal(5, redone);
        Assert.Equal("abXYZcdef", editor.Buffer.Text);
    }

    [Fact]
    public void Undo_with_nothing_to_undo_changes_nothing_and_reports_no_offset()
    {
        var editor = new SourceEditor { Text = "abc" };
        editor.Buffer.UndoStack.ClearAll();
        int version = editor.Version;

        Assert.Null(editor.Undo(redo: false));
        Assert.Null(editor.Undo(redo: true));
        Assert.Equal(version, editor.Version);
        Assert.Equal("abc", editor.Buffer.Text);
    }

    [Fact]
    public void PlaceCaret_selects_from_the_anchor_to_the_caret_offset()
    {
        var editor = new SourceEditor { Text = "0123456789" };

        editor.PlaceCaret(7, 3);

        Assert.True(editor.HasSelection);
        Assert.Equal(3, editor.SelectionStart);
        Assert.Equal(7, editor.SelectionEnd);
        Assert.Equal(7, editor.CaretOffset);
        Assert.Equal(3, editor.SelectionAnchor);
    }

    [Fact]
    public void PlaceCaret_with_the_caret_before_the_anchor_selects_the_range_with_the_caret_at_its_start()
    {
        var editor = new SourceEditor { Text = "0123456789" };

        editor.PlaceCaret(3, 7);

        Assert.True(editor.HasSelection);
        Assert.Equal(3, editor.SelectionStart);
        Assert.Equal(7, editor.SelectionEnd);
        Assert.Equal(3, editor.CaretOffset);
        Assert.Equal(7, editor.SelectionAnchor);
    }

    [Fact]
    public void PlaceCaret_without_an_anchor_clears_the_selection_and_clamps_to_the_text()
    {
        var editor = new SourceEditor { Text = "abc" };
        editor.PlaceCaret(0, 2);

        editor.PlaceCaret(99, null);

        Assert.False(editor.HasSelection);
        Assert.Equal(3, editor.CaretOffset);
    }

    [Fact]
    public void An_anchor_for_a_tracked_position_moves_after_an_insertion_at_it_and_is_deleted_with_its_text()
    {
        var editor = new SourceEditor { Text = "# Title\n\nBody\n" };
        ITrackedOffset position = new AnchorOffsetTracker(editor.Buffer).Track(0);

        editor.Buffer.Insert(0, "Lead\n\n");
        Assert.Equal(6, position.Offset);
        Assert.False(position.IsDeleted);

        // Text removed around the position deletes it; text removed from its place leaves it.
        editor.Buffer.Remove(6, 4);
        Assert.False(position.IsDeleted);
        editor.Buffer.Remove(5, 4);
        Assert.True(position.IsDeleted);
    }
}
