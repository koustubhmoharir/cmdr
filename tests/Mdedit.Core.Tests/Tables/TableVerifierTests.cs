using Mdedit.Core.Render;
using Mdedit.Core.Tables;

namespace Mdedit.Core.Tests.Tables;

/// <summary>The verifier, called directly with the grid it should check: the right replacement passes, a wrong one does not.</summary>
public sealed class TableVerifierTests
{
    private const string Text = "| a | b |\n|---|---|\n| 1 | 22 |\n";

    /// <summary>The grid of the table after an edit, made from the table as parsed from <see cref="Text"/>.</summary>
    private static (RenderBlock Block, TableGrid Grid) Edited(Func<TableGrid, TableGrid> edit)
    {
        RenderBlock block = TableCase.Table(Text);
        TableGrid grid = TableGrid.From(TableSource.Parse(Text, block), block);
        return (block, edit(grid));
    }

    [Fact]
    public void A_document_with_the_intended_cell_passes()
    {
        (RenderBlock block, TableGrid grid) = Edited(table => table.SetCell(1, 0, "x"));

        Assert.True(TableVerifier.Verify("| a | b |\n|---|---|\n| x | 22 |\n", block, grid));
    }

    [Fact]
    public void The_original_document_does_not_pass_for_an_edit_that_changed_a_cell()
    {
        (RenderBlock block, TableGrid grid) = Edited(table => table.SetCell(1, 0, "x"));

        Assert.False(TableVerifier.Verify(Text, block, grid));
    }

    [Fact]
    public void A_document_with_a_cell_in_the_wrong_place_does_not_pass()
    {
        (RenderBlock block, TableGrid grid) = Edited(table => table.SetCell(1, 0, "x"));

        Assert.False(TableVerifier.Verify("| a | b |\n|---|---|\n| 1 | x |\n", block, grid));
    }

    [Fact]
    public void A_document_with_a_row_the_grid_does_not_have_does_not_pass()
    {
        (RenderBlock block, TableGrid grid) = Edited(table => table.InsertRow(1));

        Assert.False(TableVerifier.Verify(Text, block, grid));
        Assert.True(TableVerifier.Verify("| a | b |\n|---|---|\n|  |  |\n| 1 | 22 |\n", block, grid));
    }

    [Fact]
    public void A_document_whose_alignment_differs_from_the_grid_does_not_pass()
    {
        (RenderBlock block, TableGrid grid) = Edited(table => table.SetAlignment(0, ColumnAlignment.Center));

        Assert.False(TableVerifier.Verify(Text, block, grid));
        Assert.True(TableVerifier.Verify("| a | b |\n|:-:|---|\n| 1 | 22 |\n", block, grid));
    }

    [Fact]
    public void A_cell_text_that_is_not_one_cell_does_not_pass()
    {
        (RenderBlock block, TableGrid grid) = Edited(table => table.SetCell(1, 0, "a\\|b"));

        Assert.False(TableVerifier.Verify("| a | b |\n|---|---|\n| a\\\\|b | 22 |\n", block, grid));
    }

    [Fact]
    public void A_document_with_no_table_at_the_block_offset_does_not_pass()
    {
        (RenderBlock block, TableGrid grid) = Edited(table => table);

        Assert.False(TableVerifier.Verify("Just text.\n", block, grid));
    }
}
