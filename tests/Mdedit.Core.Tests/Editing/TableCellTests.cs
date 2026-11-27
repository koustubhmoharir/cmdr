using Mdedit.Core.Editing;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Editing;

/// <summary>
/// Editing a table cell: typing, Backspace and Delete inside one cell, the pending space at a cell's edge, a selection inside a cell and across
/// cells, and Enter in a table. The minimal table is not aligned in the source, so only the cell changes; the aligned one is rewritten aligned.
/// </summary>
public sealed class TableCellTests
{
    /// <summary>A table that is not aligned in the source: its body's second cell is wider than the column, so only the edited cell is written.</summary>
    private const string Minimal = "| a | b |\n|---|---|\n| 1 | 22 |\n";

    /// <summary>A table that is aligned in the source, so an edit rewrites all of it aligned.</summary>
    private const string Aligned = "| a | b |\n|---|---|\n| 1 | 2 |\n";

    [Fact]
    public void Typing_in_an_aligned_table_cell_rewrites_the_table_aligned()
    {
        // "x" follows the "1" at 22 (the caret is at 23). The first column is 3 wide, so every line is written at that width.
        Assert.Equal(new EditCase.Outcome("| a   | b   |\n| --- | --- |\n| 1x  | 2   |\n", 32), EditCase.Type(Aligned, 23, "x"));
    }

    [Fact]
    public void Typing_in_a_minimal_table_cell_changes_only_the_cell()
    {
        Assert.Equal(new EditCase.Outcome("| a | b |\n|---|---|\n| 1x | 22 |\n", 24), EditCase.Type(Minimal, 23, "x"));
    }

    [Fact]
    public void A_pipe_typed_in_a_cell_is_written_escaped()
    {
        // The cell's text is "1|"; its source is "1\|", which keeps the cell whole. The caret is after the escape, at 25.
        Assert.Equal(new EditCase.Outcome("| a | b |\n|---|---|\n| 1\\| | 22 |\n", 25), EditCase.Type(Minimal, 23, "|"));
    }

    [Fact]
    public void Typing_in_an_empty_cell_writes_it_at_the_place_between_its_pipes()
    {
        // The empty cell's place is its closing pipe at 23. The padding is one space each side, so the "x" is at 22 and the caret after it at 23.
        const string source = "| a | b |\n|---|---|\n|  | 22 |\n";

        Assert.Equal(new EditCase.Outcome("| a | b |\n|---|---|\n| x | 22 |\n", 23), EditCase.Type(source, 23, "x"));
    }

    [Fact]
    public void Typing_at_the_end_of_a_bold_cell_continues_the_bold()
    {
        // "1" is at 24, inside "**" at 22 and 23 and 25 and 26; the caret after it at 25 is before the closing "**", so the "x" joins the bold.
        const string source = "| a | b |\n|---|---|\n| **1** | 22 |\n";

        Assert.Equal(new EditCase.Outcome("| a | b |\n|---|---|\n| **1x** | 22 |\n", 26), EditCase.Type(source, 25, "x"));
    }

    [Fact]
    public void A_space_at_a_cell_start_is_held_and_written_with_the_next_character()
    {
        // A space before the "1" does not show, so it is held; the "x" that follows writes " x" in front of the "1".
        ViewEditResult.Pending held = EditCase.Held(ViewEdit.Insert(RenderDocument.Parse(Minimal), 22, " "));

        Assert.Equal(new ViewEditResult.Pending(22, " "), held);
        Assert.Equal(new EditCase.Outcome("| a | b |\n|---|---|\n|  x1 | 22 |\n", 24), EditCase.Type(Minimal, 22, "x", held));
    }

    [Fact]
    public void Backspace_at_a_cell_start_does_nothing_without_a_message()
    {
        Assert.Equal(string.Empty, EditCase.Refusal(ViewEdit.DeleteBackward(RenderDocument.Parse(Minimal), 22)));
    }

    [Fact]
    public void Delete_at_a_cell_end_does_nothing_without_a_message()
    {
        Assert.Equal(string.Empty, EditCase.Refusal(ViewEdit.DeleteForward(RenderDocument.Parse(Minimal), 23)));
    }

    [Fact]
    public void Backspace_in_a_minimal_cell_removes_the_character_before_the_caret()
    {
        // The "2" at 23 goes; the cell is "1", so its padding stays and the caret is after the "1" at 23.
        const string source = "| a | b |\n|---|---|\n| 12 | 22 |\n";

        Assert.Equal(new EditCase.Outcome("| a | b |\n|---|---|\n| 1 | 22 |\n", 23), EditCase.Backspace(source, 24));
    }

    [Fact]
    public void Delete_in_a_minimal_cell_removes_the_character_after_the_caret()
    {
        const string source = "| a | b |\n|---|---|\n| 12 | 22 |\n";

        Assert.Equal(new EditCase.Outcome("| a | b |\n|---|---|\n| 2 | 22 |\n", 22), EditCase.Delete(source, 22));
    }

    [Fact]
    public void A_selection_inside_a_cell_is_removed_and_the_cell_keeps_its_padding()
    {
        // The "1" and "2" at 22 and 23 go. The cell is empty, so it is written at its closing pipe at 23, where the caret is.
        const string source = "| a | b |\n|---|---|\n| 12 | 22 |\n";

        Assert.Equal(new EditCase.Outcome("| a | b |\n|---|---|\n|  | 22 |\n", 23), EditCase.Replace(source, 22, 24, string.Empty));
    }

    [Fact]
    public void A_selection_across_table_cells_is_refused()
    {
        // 22 is the first cell's "1", and 26 the second cell's "22".
        Assert.Equal(EditMessages.CellSelection, EditCase.Refusal(ViewEdit.ReplaceSelection(RenderDocument.Parse(Minimal), 22, 26, string.Empty)));
    }

    [Fact]
    public void A_space_at_a_cell_end_is_held_as_pending()
    {
        // The space after the "1" at 23 would trail the cell's text, so it is held, and the cell is not written.
        Assert.Equal(new ViewEditResult.Pending(23, " "), EditCase.Held(ViewEdit.Insert(RenderDocument.Parse(Minimal), 23, " ")));
    }

    [Fact]
    public void Enter_over_a_selection_in_a_table_cell_removes_it_and_moves_to_the_next_row()
    {
        // The header's "a" goes, so the header's first cell is empty and its line is one character shorter; the next row's "1" moves to 21.
        Assert.Equal(new EditCase.Outcome("|  | b |\n|---|---|\n| 1 | 22 |\n", 21), EditCase.EnterOver(Minimal, 2, 3));
    }

    [Fact]
    public void Enter_in_a_table_cell_moves_to_the_same_column_of_the_next_row()
    {
        // The header's "a" is at 2; the next row's first cell has its "1" at 22.
        Assert.Equal(new ViewEditResult.Move(22), EditCase.EnterAt(Minimal, 2));
    }

    [Fact]
    public void Enter_in_a_column_of_the_header_moves_to_that_column_of_the_next_row()
    {
        // The header's "b" is at 6; the next row's second cell starts with its "22" at 26.
        Assert.Equal(new ViewEditResult.Move(26), EditCase.EnterAt(Minimal, 6));
    }

    [Fact]
    public void Enter_on_the_last_row_adds_a_row_below_and_moves_to_its_first_cell()
    {
        // The new row is the line "|  |  |" at 31, after the "\n" at 30; its first cell's place is the closing pipe at 34.
        Assert.Equal(new EditCase.Outcome("| a | b |\n|---|---|\n| 1 | 22 |\n|  |  |\n", 34), EditCase.Enter(Minimal, 22));
    }
}
