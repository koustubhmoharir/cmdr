using Mdedit.Core.Render;
using Mdedit.Core.Tables;

namespace Mdedit.Core.Tests.Tables;

/// <summary>Setting a cell's source: the text between the pipes is written as it is, where <see cref="TableEdit.SetCell"/> takes the shown text.</summary>
public sealed class TableEditSourceTests
{
    /// <summary>An unaligned table: the body's second cell is wider than the column, so only the edited cell is written.</summary>
    private const string Plain = "| a | b |\n|---|---|\n| 1 | 22 |\n";

    /// <summary>Applies an operation to a document and returns the document after it.</summary>
    private static string After(string text, Func<string, RenderBlock, TableEditResult> operation) =>
        TableCase.Apply(text, operation(text, TableCase.Table(text)));

    [Fact]
    public void Set_cell_source_keeps_an_escape_as_written()
    {
        Assert.Equal("| a | b |\n|---|---|\n| a\\|b | 22 |\n", After(Plain, (text, table) => TableEdit.SetCellSource(text, table, 1, 0, "a\\|b")));
    }

    [Fact]
    public void Set_cell_source_writes_markup_as_it_is()
    {
        Assert.Equal("| a | b |\n|---|---|\n| *x* | 22 |\n", After(Plain, (text, table) => TableEdit.SetCellSource(text, table, 1, 0, "*x*")));
    }

    [Fact]
    public void Set_cell_source_refuses_a_line_break()
    {
        Assert.Equal(TableEdit.OneLine, TableEdit.SetCellSource(Plain, TableCase.Table(Plain), 1, 0, "a\nb").Refusal);
    }
}
