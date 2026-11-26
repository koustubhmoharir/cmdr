using Mdedit.Core.Render;
using Mdedit.Core.Tables;

namespace Mdedit.Core.Tests.Tables;

/// <summary>
/// Edits that leave an end cell of a table without outer pipes empty. The pipe a row needs to keep that cell is written: in a
/// minimal edit, on the row that needs it; in an aligned rewrite, on every line when the column is empty in any row.
/// </summary>
public sealed class TableEditOpenEndTests
{
    /// <summary>A table without outer pipes that is not aligned in the source.</summary>
    private const string Plain = "a | b\n-|--\n1 | 22\n";

    /// <summary>A table without outer pipes that is aligned in the source.</summary>
    private const string Grid = "a   | b\n--- | ---\n1   | 2\n";

    /// <summary>Applies an operation to a document and returns the document after it.</summary>
    private static string After(string text, Func<string, RenderBlock, TableEditResult> operation) =>
        TableCase.Apply(text, operation(text, TableCase.Table(text)));

    [Fact]
    public void Minimal_set_cell_to_empty_last_cell_writes_the_trailing_pipe_after_the_padding()
    {
        Assert.Equal("a | b\n-|--\n1 | |\n", After(Plain, (text, table) => TableEdit.SetCell(text, table, 1, 1, "")));
    }

    [Fact]
    public void Minimal_set_cell_to_empty_first_cell_writes_the_leading_pipe()
    {
        Assert.Equal("a | b\n-|--\n| | 22\n", After(Plain, (text, table) => TableEdit.SetCell(text, table, 1, 0, "")));
    }

    [Fact]
    public void Minimal_set_cell_to_empty_header_last_cell_changes_only_the_header_line()
    {
        Assert.Equal("a | |\n-|--\n1 | 22\n", After(Plain, (text, table) => TableEdit.SetCell(text, table, 0, 1, "")));
    }

    [Fact]
    public void Minimal_set_cell_to_empty_header_first_cell_writes_the_leading_pipe()
    {
        Assert.Equal("| | b\n-|--\n1 | 22\n", After(Plain, (text, table) => TableEdit.SetCell(text, table, 0, 0, "")));
    }

    [Fact]
    public void Minimal_insert_column_at_the_end_adds_the_trailing_pipe_to_rows_that_end_empty()
    {
        Assert.Equal("a | b|  |\n-|--| --- \n1 | 22|  |\n", After(Plain, (text, table) => TableEdit.InsertColumn(text, table, 2)));
    }

    [Fact]
    public void Minimal_insert_row_is_an_empty_row_with_both_end_pipes()
    {
        Assert.Equal("a | b\n-|--\n|  |  |\n1 | 22\n", After(Plain, (text, table) => TableEdit.InsertRow(text, table, 1)));
    }

    [Fact]
    public void Aligned_set_cell_to_empty_last_cell_gives_every_line_a_trailing_pipe()
    {
        Assert.Equal("a   | b   |\n--- | --- |\n1   |     |\n", After(Grid, (text, table) => TableEdit.SetCell(text, table, 1, 1, "")));
    }

    [Fact]
    public void Aligned_set_cell_to_empty_first_cell_gives_every_line_a_leading_pipe()
    {
        Assert.Equal("| a   | b\n| --- | ---\n|     | 2\n", After(Grid, (text, table) => TableEdit.SetCell(text, table, 1, 0, "")));
    }

    [Fact]
    public void Aligned_insert_column_at_the_end_is_empty_in_every_row_so_every_line_gets_a_trailing_pipe()
    {
        Assert.Equal("a   | b   |     |\n--- | --- | --- |\n1   | 2   |     |\n", After(Grid, (text, table) => TableEdit.InsertColumn(text, table, 2)));
    }

    [Fact]
    public void Aligned_insert_row_is_empty_at_both_ends_so_every_line_gets_both_pipes()
    {
        Assert.Equal("| a   | b   |\n| --- | --- |\n|     |     |\n| 1   | 2   |\n", After(Grid, (text, table) => TableEdit.InsertRow(text, table, 1)));
    }

    [Fact]
    public void A_row_without_a_leading_pipe_whose_first_cell_is_blank_reads_as_having_the_leading_pipe()
    {
        const string text = "a | b\n-|--\n  |  b\n";
        TableSourceLine row = TableSource.Parse(text, TableCase.Table(text)).Row(1);

        Assert.True(row.LeadingPipe);
        Assert.Single(row.Cells);
    }
}
