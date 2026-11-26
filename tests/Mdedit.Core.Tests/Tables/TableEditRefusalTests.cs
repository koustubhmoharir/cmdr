using Mdedit.Core.Render;
using Mdedit.Core.Tables;

namespace Mdedit.Core.Tests.Tables;

/// <summary>The refusals of the table operations, with their messages, and the out-of-range arguments.</summary>
public sealed class TableEditRefusalTests
{
    /// <summary>A table with two body rows and two columns.</summary>
    private const string Table = "| a | b |\n|---|---|\n| 1 | 22 |\n| 3 | 4 |\n";

    /// <summary>A table with one column.</summary>
    private const string OneColumn = "| a |\n|---|\n| 1 |\n";

    [Fact]
    public void Deleting_the_header_row_is_refused()
    {
        TableEditResult result = TableEdit.DeleteRow(Table, TableCase.Table(Table), 0);

        Assert.Equal("The header row cannot be deleted", result.Refusal);
        Assert.True(result.IsRefused);
    }

    [Fact]
    public void Duplicating_the_header_row_is_refused()
    {
        Assert.Equal("The header row cannot be duplicated", TableEdit.DuplicateRow(Table, TableCase.Table(Table), 0).Refusal);
    }

    [Fact]
    public void Inserting_a_row_before_the_header_is_refused()
    {
        Assert.Equal("The header row stays first", TableEdit.InsertRow(Table, TableCase.Table(Table), 0).Refusal);
    }

    [Fact]
    public void Moving_the_header_row_is_refused()
    {
        Assert.Equal("The header row stays first", TableEdit.MoveRow(Table, TableCase.Table(Table), 0, 1).Refusal);
    }

    [Fact]
    public void Moving_a_row_onto_the_header_is_refused()
    {
        Assert.Equal("The header row stays first", TableEdit.MoveRow(Table, TableCase.Table(Table), 1, -1).Refusal);
    }

    [Fact]
    public void Moving_the_last_row_down_is_refused_as_already_last()
    {
        Assert.Equal("Already last", TableEdit.MoveRow(Table, TableCase.Table(Table), 2, 1).Refusal);
    }

    [Fact]
    public void Moving_the_first_column_left_is_refused_as_already_first()
    {
        Assert.Equal("Already first", TableEdit.MoveColumn(Table, TableCase.Table(Table), 0, -1).Refusal);
    }

    [Fact]
    public void Moving_the_last_column_right_is_refused_as_already_last()
    {
        Assert.Equal("Already last", TableEdit.MoveColumn(Table, TableCase.Table(Table), 1, 1).Refusal);
    }

    [Fact]
    public void Deleting_the_only_column_is_refused()
    {
        Assert.Equal("The last column cannot be deleted", TableEdit.DeleteColumn(OneColumn, TableCase.Table(OneColumn), 0).Refusal);
    }

    [Fact]
    public void A_cell_with_a_line_break_is_refused_with_its_message()
    {
        Assert.Equal("A table cell is one line", TableEdit.SetCell(Table, TableCase.Table(Table), 1, 1, "x\ny").Refusal);
    }

    [Fact]
    public void An_edit_whose_result_does_not_reparse_as_the_table_is_refused_and_changes_nothing()
    {
        const string text = "| a | b |\n|---|---|\n| 1 | 22 |\n";
        TableEditResult result = TableEdit.SetCell(text, TableCase.Table(text), 1, 0, "a\\|b");

        Assert.Equal("Could not edit the table safely", result.Refusal);
        Assert.Equal(text, TableCase.Apply(text, result));
    }

    [Fact]
    public void A_refused_result_has_no_replacement()
    {
        TableEditResult result = TableEdit.DeleteRow(Table, TableCase.Table(Table), 0);

        Assert.Equal((0, 0, string.Empty), (result.Start, result.Length, result.Text));
    }

    [Fact]
    public void An_applied_result_is_not_refused()
    {
        Assert.False(TableEdit.DeleteRow(Table, TableCase.Table(Table), 1).IsRefused);
    }

    [Fact]
    public void Rows_and_columns_outside_the_table_are_an_argument_error()
    {
        RenderBlock table = TableCase.Table(Table);

        Assert.Throws<ArgumentOutOfRangeException>(() => TableEdit.SetCell(Table, table, 3, 0, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => TableEdit.SetCell(Table, table, 0, 2, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => TableEdit.InsertRow(Table, table, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => TableEdit.InsertColumn(Table, table, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => TableEdit.DeleteRow(Table, table, -1));
    }

    [Fact]
    public void Inserting_at_the_bounds_is_allowed()
    {
        RenderBlock table = TableCase.Table(Table);

        Assert.False(TableEdit.InsertRow(Table, table, 3).IsRefused);
        Assert.False(TableEdit.InsertColumn(Table, table, 2).IsRefused);
        Assert.False(TableEdit.InsertColumn(Table, table, 0).IsRefused);
    }

    [Fact]
    public void A_block_that_is_not_a_table_is_an_argument_error()
    {
        const string text = "A paragraph.\n";
        RenderBlock paragraph = RenderDocument.Parse(text).Blocks[0];

        Assert.Throws<ArgumentException>(() => TableEdit.SetCell(text, paragraph, 0, 0, "x"));
    }
}
