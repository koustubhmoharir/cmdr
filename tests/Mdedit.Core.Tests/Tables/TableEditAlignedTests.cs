using Mdedit.Core.Render;
using Mdedit.Core.Tables;

namespace Mdedit.Core.Tests.Tables;

/// <summary>Operations on an aligned table: the whole table is rewritten aligned, in its own style.</summary>
public sealed class TableEditAlignedTests
{
    /// <summary>An aligned table with outer pipes: column 0 is 4 wide, column 1 is 3 wide.</summary>
    private const string Grid = "| Name | Qty |\n| ---- | --- |\n| kiwi | 10  |\n| fig  | 3   |\n";

    /// <summary>Applies an operation to a document and returns the document after it.</summary>
    private static string After(string text, Func<string, RenderBlock, TableEditResult> operation) =>
        TableCase.Apply(text, operation(text, TableCase.Table(text)));

    [Fact]
    public void The_grid_is_aligned_so_it_is_rewritten_whole_after_an_edit()
    {
        Assert.True(TableSource.Parse(Grid, TableCase.Table(Grid)).IsAligned);
    }

    [Fact]
    public void Set_cell_keeps_the_column_at_its_width_when_the_text_fits()
    {
        Assert.Equal("| Name | Qty |\n| ---- | --- |\n| kiwi | 7   |\n| fig  | 3   |\n", After(Grid, (text, table) => TableEdit.SetCell(text, table, 1, 1, "7")));
    }

    [Fact]
    public void Set_cell_widens_its_column_for_a_longer_text_and_the_delimiter_follows()
    {
        Assert.Equal("| Name      | Qty |\n| --------- | --- |\n| kiwi plum | 10  |\n| fig       | 3   |\n", After(Grid, (text, table) => TableEdit.SetCell(text, table, 1, 0, "kiwi plum")));
    }

    [Fact]
    public void Set_cell_writes_an_escaped_pipe_and_it_counts_as_its_source_width()
    {
        const string text = "| a | b |\n|---|---|\n| 1 | 2 |\n";

        Assert.Equal("| a    | b   |\n| ---- | --- |\n| x\\|y | 2   |\n", After(text, (doc, table) => TableEdit.SetCell(doc, table, 1, 0, "x|y")));
    }

    [Fact]
    public void Insert_row_adds_a_blank_aligned_row()
    {
        Assert.Equal("| Name | Qty |\n| ---- | --- |\n|      |     |\n| kiwi | 10  |\n| fig  | 3   |\n", After(Grid, (text, table) => TableEdit.InsertRow(text, table, 1)));
    }

    [Fact]
    public void Delete_row_removes_its_line_and_the_table_stays_aligned()
    {
        Assert.Equal("| Name | Qty |\n| ---- | --- |\n| fig  | 3   |\n", After(Grid, (text, table) => TableEdit.DeleteRow(text, table, 1)));
    }

    [Fact]
    public void Duplicate_row_copies_it_aligned()
    {
        Assert.Equal("| Name | Qty |\n| ---- | --- |\n| kiwi | 10  |\n| kiwi | 10  |\n| fig  | 3   |\n", After(Grid, (text, table) => TableEdit.DuplicateRow(text, table, 1)));
    }

    [Fact]
    public void Move_row_up_reorders_the_rows()
    {
        Assert.Equal("| Name | Qty |\n| ---- | --- |\n| fig  | 3   |\n| kiwi | 10  |\n", After(Grid, (text, table) => TableEdit.MoveRow(text, table, 2, -1)));
    }

    [Fact]
    public void Insert_column_at_the_start_adds_a_blank_column_of_the_narrowest_width()
    {
        Assert.Equal("|     | Name | Qty |\n| --- | ---- | --- |\n|     | kiwi | 10  |\n|     | fig  | 3   |\n", After(Grid, (text, table) => TableEdit.InsertColumn(text, table, 0)));
    }

    [Fact]
    public void Delete_column_removes_it_from_every_row()
    {
        Assert.Equal("| Name |\n| ---- |\n| kiwi |\n| fig  |\n", After(Grid, (text, table) => TableEdit.DeleteColumn(text, table, 1)));
    }

    [Fact]
    public void Move_column_reorders_the_cells_and_the_widths()
    {
        Assert.Equal("| Qty | Name |\n| --- | ---- |\n| 10  | kiwi |\n| 3   | fig  |\n", After(Grid, (text, table) => TableEdit.MoveColumn(text, table, 0, 1)));
    }

    [Fact]
    public void Set_alignment_right_gives_the_delimiter_a_trailing_colon_and_justifies_the_cells()
    {
        Assert.Equal("| Name | Qty |\n| ---- | --: |\n| kiwi |  10 |\n| fig  |   3 |\n", After(Grid, (text, table) => TableEdit.SetAlignment(text, table, 1, ColumnAlignment.Right)));
    }

    [Fact]
    public void Set_alignment_centre_changes_only_the_delimiter_of_the_column()
    {
        Assert.Equal("| Name | Qty |\n| :--: | --- |\n| kiwi | 10  |\n| fig  | 3   |\n", After(Grid, (text, table) => TableEdit.SetAlignment(text, table, 0, ColumnAlignment.Center)));
    }

    [Fact]
    public void Aligned_table_without_outer_pipes_stays_without_them()
    {
        const string text = "a   | b\n--- | ---\n1   | 2\n";

        Assert.Equal("a   | b\n--- | ---\n1   | 20\n", After(text, (doc, table) => TableEdit.SetCell(doc, table, 1, 1, "20")));
    }

    [Fact]
    public void Aligned_crlf_table_keeps_crlf_on_every_line()
    {
        const string text = "| Name | Qty |\r\n| ---- | --- |\r\n| kiwi | 10  |\r\n";

        Assert.Equal("| Name | Qty |\r\n| ---- | --- |\r\n| kiwi | 7   |\r\n", After(text, (doc, table) => TableEdit.SetCell(doc, table, 1, 1, "7")));
    }

    [Fact]
    public void Aligned_table_in_a_quote_keeps_the_quote_marker_on_every_line()
    {
        const string text = "> | a | b |\n> |---|---|\n> | 1 | 2 |\n";

        Assert.Equal("> | a   | b   |\n> | --- | --- |\n> |     |     |\n> | 1   | 2   |\n", After(text, (doc, table) => TableEdit.InsertRow(doc, table, 1)));
    }

    [Fact]
    public void Cell_text_round_trips_through_set_cell_without_changing_the_document()
    {
        const string text = "| a    | b   |\n| ---- | --- |\n| x\\|y | 2   |\n";
        TableSource source = TableSource.Parse(text, TableCase.Table(text));

        Assert.Equal(text, After(text, (doc, table) => TableEdit.SetCell(doc, table, 1, 0, source.CellText(1, 0))));
    }
}
