using System.Drawing;
using Mdedit.Tui;
using Terminal.Gui.Input;

namespace Mdedit.Tests;

/// <summary>
/// The table cells of the view: the cell under the caret (header, body, outside a table), the screen rectangle of a cell for the inline field,
/// the reveal of a cell in a scrolled table, and the placement of the caret on a cell.
/// </summary>
public sealed partial class RenderedViewTests
{
    // A two-column table with an empty header cell, a paragraph after it.
    private const string GridWithEmpty = "| a |  |\n|---|---|\n| 1 | 2 |\n\nAfter.\n";

    // A small table: the header's first cell 'a' sits on the row below the top border.
    private const string Grid = "| a | b |\n|---|---|\n| 1 | 2 |\n\nAfter.\n";

    [Fact]
    public void The_table_cell_under_the_caret_on_a_header_cell_is_the_header_row_and_its_column()
    {
        RenderedView view = View(Grid);
        RenderedView.TableCell? cell = null;

        Run(view, v =>
        {
            v.PlaceCaret(Offset(Grid, "b"), null);
            cell = v.TableCellAtCaret();
        });

        Assert.Equal(new RenderedView.TableCell(0, 0, 1), cell);
    }

    [Fact]
    public void The_table_cell_under_the_caret_on_a_body_cell_is_its_row_and_column()
    {
        RenderedView view = View(Grid);
        RenderedView.TableCell? cell = null;

        Run(view, v =>
        {
            v.PlaceCaret(Offset(Grid, "2"), null);
            cell = v.TableCellAtCaret();
        });

        Assert.Equal(new RenderedView.TableCell(0, 1, 1), cell);
    }

    [Fact]
    public void The_table_cell_under_the_caret_is_null_outside_a_table()
    {
        RenderedView view = View(Grid);
        RenderedView.TableCell? cell = new RenderedView.TableCell(9, 9, 9);

        Run(view, v =>
        {
            v.PlaceCaret(Offset(Grid, "After"), null);
            cell = v.TableCellAtCaret();
        });

        Assert.Null(cell);
    }

    [Fact]
    public void An_empty_cell_takes_the_caret_on_its_own_cell()
    {
        RenderedView view = View(GridWithEmpty);
        RenderedView.TableCell? cell = null;

        Run(view, v =>
        {
            Assert.True(v.PlaceTableCell(0, 0, 1));
            cell = v.TableCellAtCaret();
        });

        Assert.Equal(new RenderedView.TableCell(0, 0, 1), cell);
    }

    [Fact]
    public void The_arrow_keys_stop_on_an_empty_cell_between_its_neighbours()
    {
        RenderedView view = View(GridWithEmpty);
        RenderedView.TableCell? cell = null;

        Run(view, v =>
        {
            v.PlaceCaret(Offset(GridWithEmpty, "a"), null);
            v.HandleKey(Key.CursorRight);
            cell = v.TableCellAtCaret();
        });

        Assert.Equal(new RenderedView.TableCell(0, 0, 1), cell);
    }

    [Fact]
    public void PlaceTableCell_puts_the_caret_on_the_cell_it_names()
    {
        RenderedView view = View(Grid);
        RenderedView.TableCell? cell = null;

        Run(view, v =>
        {
            Assert.True(v.PlaceTableCell(0, 1, 1));
            cell = v.TableCellAtCaret();
        });

        Assert.Equal(new RenderedView.TableCell(0, 1, 1), cell);
    }

    [Fact]
    public void PlaceTableCell_is_false_for_a_cell_the_table_does_not_have()
    {
        RenderedView view = View(Grid);
        bool placed = true;

        Run(view, v => placed = v.PlaceTableCell(0, 5, 0));

        Assert.False(placed);
    }

    [Fact]
    public void The_rectangle_of_a_header_cell_is_its_text_column_on_the_first_line_of_its_row()
    {
        RenderedView view = View(Grid);
        Rectangle? area = null;

        Result result = Run(view, v => area = v.TableCellRectangle(new RenderedView.TableCell(0, 0, 0)));

        // The top border is line 0, the header row line 1. The gutter is two columns, the bar is at 2 and the cell's text, after its padding, at 4.
        Assert.Equal(new Rectangle(4, 1, 2, 1), area);
        Assert.Equal("a", result.Screens[0].Cells[1][4].Grapheme);
    }

    [Fact]
    public void Revealing_a_cell_of_a_wide_table_scrolls_it_into_view_and_leaves_the_caret()
    {
        RenderedView view = View(WideTable);
        Rectangle? area = null;
        RenderedView.TableCell? caret = null;

        Result result = Run(view, v =>
        {
            v.PlaceCaret(Offset(WideTable, "one"), null);
            Assert.True(v.RevealTableCell(new RenderedView.TableCell(0, 0, 2)));
            area = v.TableCellRectangle(new RenderedView.TableCell(0, 0, 2));
            caret = v.TableCellAtCaret();
        });

        Assert.NotNull(area);
        Assert.InRange(area!.Value.X, 0, 39);
        Assert.StartsWith("C", result.Screens[^1].Cells[1][area.Value.X].Grapheme, StringComparison.Ordinal);
        Assert.Equal(new RenderedView.TableCell(0, 1, 0), caret);
    }
}
