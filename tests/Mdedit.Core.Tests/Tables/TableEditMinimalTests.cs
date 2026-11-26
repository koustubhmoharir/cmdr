using Mdedit.Core.Render;
using Mdedit.Core.Tables;

namespace Mdedit.Core.Tests.Tables;

/// <summary>Operations on a table that is not aligned in the source: only the cells and lines that change are written.</summary>
public sealed class TableEditMinimalTests
{
    /// <summary>An unaligned table: the body row's second cell is wider than the column.</summary>
    private const string Plain = "| a | b |\n|---|---|\n| 1 | 22 |\n";

    /// <summary>Applies an operation to a document and returns the document after it.</summary>
    private static string After(string text, Func<string, RenderBlock, TableEditResult> operation) =>
        TableCase.Apply(text, operation(text, TableCase.Table(text)));

    [Fact]
    public void Set_cell_changes_only_the_cell_and_keeps_its_padding()
    {
        Assert.Equal("| a | b |\n|---|---|\n| x | 22 |\n", After(Plain, (text, table) => TableEdit.SetCell(text, table, 1, 0, "x")));
    }

    [Fact]
    public void Set_cell_to_empty_leaves_the_padding_of_the_cell()
    {
        Assert.Equal("| a | b |\n|---|---|\n|  | 22 |\n", After(Plain, (text, table) => TableEdit.SetCell(text, table, 1, 0, "")));
    }

    [Fact]
    public void Set_cell_writes_a_pipe_escaped_and_drops_the_spaces_around_the_text()
    {
        Assert.Equal("| a | b |\n|---|---|\n| a\\|b | 22 |\n", After(Plain, (text, table) => TableEdit.SetCell(text, table, 1, 0, "  a|b  ")));
    }

    [Fact]
    public void Set_cell_of_a_header_cell_is_the_header_row()
    {
        Assert.Equal("| hi | b |\n|---|---|\n| 1 | 22 |\n", After(Plain, (text, table) => TableEdit.SetCell(text, table, 0, 0, "hi")));
    }

    [Fact]
    public void Set_cell_with_a_line_break_is_refused()
    {
        TableEditResult result = TableEdit.SetCell(Plain, TableCase.Table(Plain), 1, 0, "a\nb");

        Assert.Equal(TableEdit.OneLine, result.Refusal);
    }

    [Fact]
    public void Set_cell_with_a_carriage_return_is_refused()
    {
        TableEditResult result = TableEdit.SetCell(Plain, TableCase.Table(Plain), 1, 0, "a\rb");

        Assert.Equal(TableEdit.OneLine, result.Refusal);
    }

    [Fact]
    public void Setting_a_cell_to_its_own_text_leaves_the_document_as_it_was()
    {
        const string text = "| a | b |\n|---|---|\n| a\\|b | 22 |\n";
        TableSource source = TableSource.Parse(text, TableCase.Table(text));

        Assert.Equal(text, After(text, (doc, table) => TableEdit.SetCell(doc, table, 1, 0, source.CellText(1, 0))));
    }

    [Fact]
    public void Insert_row_adds_an_empty_row_in_the_outer_pipe_style_above_the_row_at_the_index()
    {
        Assert.Equal("| a | b |\n|---|---|\n|  |  |\n| 1 | 22 |\n", After(Plain, (text, table) => TableEdit.InsertRow(text, table, 1)));
    }

    [Fact]
    public void Insert_row_at_the_end_of_a_document_without_a_final_line_ending_adds_the_line_ending()
    {
        const string text = "| a | b |\n|---|---|\n| 1 | 22 |";

        Assert.Equal("| a | b |\n|---|---|\n| 1 | 22 |\n|  |  |", After(text, (doc, table) => TableEdit.InsertRow(doc, table, 2)));
    }

    [Fact]
    public void Insert_row_without_outer_pipes_gets_the_pipes_its_empty_end_cells_need()
    {
        const string text = "a | b\n-|--\n1 | 22\n";

        Assert.Equal("a | b\n-|--\n|  |  |\n1 | 22\n", After(text, (doc, table) => TableEdit.InsertRow(doc, table, 1)));
    }

    [Fact]
    public void Duplicate_row_copies_the_row_after_it()
    {
        Assert.Equal("| a | b |\n|---|---|\n| 1 | 22 |\n| 1 | 22 |\n", After(Plain, (text, table) => TableEdit.DuplicateRow(text, table, 1)));
    }

    [Fact]
    public void Delete_row_removes_its_line()
    {
        Assert.Equal("| a | b |\n|---|---|\n", After(Plain, (text, table) => TableEdit.DeleteRow(text, table, 1)));
    }

    [Fact]
    public void Move_row_down_swaps_it_with_the_row_below_and_keeps_the_other_lines()
    {
        const string text = "| a | b |\n|---|---|\n| 1 | 22 |\n| 3 | 4 |\n";

        Assert.Equal("| a | b |\n|---|---|\n| 3 | 4 |\n| 1 | 22 |\n", After(text, (doc, table) => TableEdit.MoveRow(doc, table, 1, 1)));
    }

    [Fact]
    public void Move_row_up_swaps_it_with_the_row_above()
    {
        const string text = "| a | b |\n|---|---|\n| 1 | 22 |\n| 3 | 4 |\n";

        Assert.Equal("| a | b |\n|---|---|\n| 3 | 4 |\n| 1 | 22 |\n", After(text, (doc, table) => TableEdit.MoveRow(doc, table, 2, -1)));
    }

    [Fact]
    public void Insert_column_adds_the_blank_cells_and_the_plain_delimiter()
    {
        Assert.Equal("| a |  | b |\n|---| --- |---|\n| 1 |  | 22 |\n", After(Plain, (text, table) => TableEdit.InsertColumn(text, table, 1)));
    }

    [Fact]
    public void Delete_column_removes_its_cell_from_every_line()
    {
        Assert.Equal("| a |\n|---|\n| 1 |\n", After(Plain, (text, table) => TableEdit.DeleteColumn(text, table, 1)));
    }

    [Fact]
    public void Move_column_swaps_its_cells_on_every_line()
    {
        Assert.Equal("| b | a |\n|---|---|\n| 22 | 1 |\n", After(Plain, (text, table) => TableEdit.MoveColumn(text, table, 0, 1)));
    }

    [Fact]
    public void Set_alignment_changes_only_the_delimiter_cell_and_keeps_its_dashes()
    {
        Assert.Equal("| a | b |\n|---|:---:|\n| 1 | 22 |\n", After(Plain, (text, table) => TableEdit.SetAlignment(text, table, 1, ColumnAlignment.Center)));
        Assert.Equal("| a | b |\n|:---|---|\n| 1 | 22 |\n", After(Plain, (text, table) => TableEdit.SetAlignment(text, table, 0, ColumnAlignment.Left)));
    }

    [Fact]
    public void Set_alignment_to_none_writes_plain_dashes()
    {
        const string text = "| a | b |\n|:--|---|\n| 1 | 22 |\n";

        Assert.Equal("| a | b |\n|--|---|\n| 1 | 22 |\n", After(text, (doc, table) => TableEdit.SetAlignment(doc, table, 0, null)));
    }

    [Fact]
    public void Set_cell_in_a_short_row_normalises_that_row_to_the_header_columns()
    {
        const string text = "| a | b |\n|---|---|\n| 1 |\n| 3 | 4 | 5 |\n";

        Assert.Equal("| a | b |\n|---|---|\n| 1 | 2 |\n| 3 | 4 | 5 |\n", After(text, (doc, table) => TableEdit.SetCell(doc, table, 1, 1, "2")));
    }

    [Fact]
    public void Ragged_rows_that_no_edit_touches_stay_as_written()
    {
        const string text = "| a | b |\n|---|---|\n| 1 |\n| 3 | 4 | 5 |\n";

        Assert.Equal("| x | b |\n|---|---|\n| 1 |\n| 3 | 4 | 5 |\n", After(text, (doc, table) => TableEdit.SetCell(doc, table, 0, 0, "x")));
    }

    [Fact]
    public void Crlf_is_kept_on_every_line_including_a_new_one()
    {
        const string text = "| a | b |\r\n|---|---|\r\n| 1 | 22 |\r\n";

        Assert.Equal("| a | b |\r\n|---|---|\r\n|  |  |\r\n| 1 | 22 |\r\n", After(text, (doc, table) => TableEdit.InsertRow(doc, table, 1)));
        Assert.Equal("| a | b |\r\n|---|---|\r\n| x | 22 |\r\n", After(text, (doc, table) => TableEdit.SetCell(doc, table, 1, 0, "x")));
    }

    [Fact]
    public void Table_in_a_quote_keeps_the_quote_marker_on_a_new_row()
    {
        const string text = "> | a | b |\n> |---|---|\n> | 1 | 22 |\n";

        Assert.Equal("> | a | b |\n> |---|---|\n> |  |  |\n> | 1 | 22 |\n", After(text, (doc, table) => TableEdit.InsertRow(doc, table, 1)));
    }

    [Fact]
    public void Table_in_a_list_item_gets_a_continuation_row_not_a_new_item()
    {
        const string text = "- | a | b |\n  |---|---|\n  | 1 | 22 |\n";

        Assert.Equal("- | a | b |\n  |---|---|\n  |  |  |\n  | 1 | 22 |\n", After(text, (doc, table) => TableEdit.InsertRow(doc, table, 1)));
    }

    [Fact]
    public void Escaped_pipes_in_other_cells_are_kept_as_written()
    {
        const string text = "| a \\| b | c |\n|---|---|\n| 1 | 2 |\n";

        Assert.Equal("| a \\| b | z |\n|---|---|\n| 1 | 2 |\n", After(text, (doc, table) => TableEdit.SetCell(doc, table, 0, 1, "z")));
    }

    [Fact]
    public void Each_operation_targets_the_cell_the_caret_should_go_to()
    {
        RenderBlock table = TableCase.Table(Plain);

        Assert.Equal((1, 0), Target(TableEdit.SetCell(Plain, table, 1, 0, "x")));
        Assert.Equal((2, 0), Target(TableEdit.InsertRow(Plain, table, 2)));
        Assert.Equal((2, 0), Target(TableEdit.DuplicateRow(Plain, table, 1)));
        Assert.Equal((0, 0), Target(TableEdit.DeleteRow(Plain, table, 1)));
        Assert.Equal((0, 1), Target(TableEdit.InsertColumn(Plain, table, 1)));
        Assert.Equal((0, 1), Target(TableEdit.MoveColumn(Plain, table, 0, 1)));
        Assert.Equal((0, 0), Target(TableEdit.DeleteColumn(Plain, table, 1)));
        Assert.Equal((0, 1), Target(TableEdit.SetAlignment(Plain, table, 1, null)));
    }

    [Fact]
    public void A_deleted_last_row_puts_the_target_on_the_new_last_row()
    {
        const string text = "| a | b |\n|---|---|\n| 1 | 22 |\n| 3 | 4 |\n";

        Assert.Equal((1, 0), Target(TableEdit.DeleteRow(text, TableCase.Table(text), 2)));
    }

    [Fact]
    public void The_edit_is_one_replacement_that_covers_the_changed_text_only()
    {
        TableEditResult result = TableEdit.SetCell(Plain, TableCase.Table(Plain), 1, 0, "x");

        Assert.Equal(Plain.IndexOf("| 1 |", StringComparison.Ordinal) + 2, result.Start);
        Assert.Equal(1, result.Length);
        Assert.Equal("x", result.Text);
    }

    [Fact]
    public void A_cell_that_is_a_reference_link_in_the_document_is_accepted()
    {
        const string text = "| a | b |\n|---|---|\n| 1 | 22 |\n\n[x]: http://example.com\n";

        Assert.Equal("| a | b |\n|---|---|\n| [x] | 22 |\n\n[x]: http://example.com\n", After(text, (doc, table) => TableEdit.SetCell(doc, table, 1, 0, "[x]")));
    }

    [Fact]
    public void Verification_refuses_a_cell_whose_pipe_would_split_the_row()
    {
        TableEditResult result = TableEdit.SetCell(Plain, TableCase.Table(Plain), 1, 0, "a\\|b");

        Assert.Equal(TableEdit.CouldNotEdit, result.Refusal);
    }

    /// <summary>The target of a result as (row, column).</summary>
    private static (int Row, int Column) Target(TableEditResult result) => (result.Row, result.Column);
}
