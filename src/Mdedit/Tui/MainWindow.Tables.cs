using Mdedit.Core.Render;
using Mdedit.Core.Tables;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Mdedit.Tui;

/// <summary>
/// The tables of the window, in view mode: F2 edits the cell at the caret in the <see cref="TableCellField"/>; Alt+Up and
/// Alt+Down move the caret's row, Alt+Left and Alt+Right its column (outside a table Alt+Left is Back, see <see cref="HandleLinkKey"/>);
/// Alt+T opens the table menu at the caret, and the right-click menu over a table has the same items. Edit mode has none of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Edits.</b> Each operation of <see cref="TableEdit"/> returns one replacement of the text. It is applied to the buffer as one change (one
/// undo step), the view is rendered from the new text, and the caret goes to the operation's target cell. A refusal shows its message on the
/// message line and changes nothing; in the field the field stays open with its text.
/// </para>
/// <para>
/// <b>The field.</b> It is a child of the view, over the cell's first screen line. Enter commits, Esc cancels, Tab and Shift+Tab commit and open the
/// next or previous cell (after the last cell, the commit closes the field). While the field is open the window takes no keys but Ctrl+Q, and the
/// external-change check waits (see <see cref="IsIdle"/>).
/// </para>
/// </remarks>
internal sealed partial class MainWindow
{
    /// <summary>The key of the table menu.</summary>
    internal static readonly Key TableMenuKey = Key.T.WithAlt;

    /// <summary>The message of the table menu's key outside a table.</summary>
    internal const string NotInTable = "Not in a table";

    /// <summary>The narrowest the inline field is, in columns, when the cell is near the view's right edge.</summary>
    private const int MinCellFieldWidth = 20;

    // The inline field of the cell being edited, or null. Its cell is the one it was opened on, which the commit writes.
    private TableCellField? _cellField;
    private RenderedView.TableCell _cellFieldCell;

    /// <summary>An operation of the table: the text and the block it applies to, and the cell that it is applied at.</summary>
    /// <returns>The replacement, or a refusal.</returns>
    private delegate TableEditResult TableOperation(string text, RenderBlock table, RenderedView.TableCell cell);

    /// <summary>
    /// The window's table keys in view mode: F2 edits the caret's cell in the field. Enter is an edit of the text (in a cell it moves the caret to
    /// the next row, see <c>MainWindow.ViewEditing.cs</c>), Tab and Shift+Tab move the caret to the next or previous cell, Alt+Up and Alt+Down move the caret's row,
    /// Alt+Left and Alt+Right its column, and the table menu key opens the menu. The arrows and the Tab keys return false outside a table (and Tab on
    /// the table's last or first cell), so that Alt+Left is Back there and Tab moves to a link. Returns false for a key it does not bind.
    /// </summary>
    private bool HandleTableKey(Key key)
    {
        if (_editing)
        {
            return false;
        }

        if (key == Key.F2)
        {
            return OpenCellAtCaret();
        }

        if (key == TableMenuKey)
        {
            ShowTableMenu();
            return true;
        }

        if (key == Key.Tab || key == Key.Tab.WithShift)
        {
            return MoveToNeighbourCell(forward: key == Key.Tab);
        }

        if (key == Key.CursorUp.WithAlt)
        {
            return ApplyAtCaret((text, table, cell) => AtColumn(TableEdit.MoveRow(text, table, cell.Row, -1), cell.Column));
        }

        if (key == Key.CursorDown.WithAlt)
        {
            return ApplyAtCaret((text, table, cell) => AtColumn(TableEdit.MoveRow(text, table, cell.Row, 1), cell.Column));
        }

        if (key == Key.CursorLeft.WithAlt)
        {
            return ApplyAtCaret((text, table, cell) => AtRow(TableEdit.MoveColumn(text, table, cell.Column, -1), cell.Row));
        }

        if (key == Key.CursorRight.WithAlt)
        {
            return ApplyAtCaret((text, table, cell) => AtRow(TableEdit.MoveColumn(text, table, cell.Column, 1), cell.Row));
        }

        return false;
    }

    /// <summary>
    /// The result with its target row set to a row. The core puts the caret of a column operation on the header and of a row operation on the
    /// first column; the window keeps the caret on the cell that moved, so it takes the caret's row for a column operation and its column for a
    /// row operation (see <see cref="AtColumn"/>). A refusal is returned as it is.
    /// </summary>
    private static TableEditResult AtRow(TableEditResult result, int row) => result.IsRefused ? result : result with { Row = row };

    /// <summary>The result with its target column set to a column: see <see cref="AtRow"/>.</summary>
    private static TableEditResult AtColumn(TableEditResult result, int column) => result.IsRefused ? result : result with { Column = column };

    /// <summary>
    /// Moves the caret to the next or previous cell of the table the caret is in, without opening a field. Returns false, and does nothing, when
    /// the caret is not in a table cell or is on the table's last (or first) cell.
    /// </summary>
    /// <param name="forward">True for the next cell, false for the previous one.</param>
    private bool MoveToNeighbourCell(bool forward)
    {
        if (_view.TableCellAtCaret() is not { } cell || NeighbourCell(cell, forward) is not { } next)
        {
            return false;
        }

        _view.PlaceTableCell(next.Block, next.Row, next.Column);
        return true;
    }

    /// <summary>Opens the inline field on the caret's cell. Returns false, and does nothing, when the caret is not in a table cell.</summary>
    private bool OpenCellAtCaret()
    {
        if (_view.TableCellAtCaret() is not { } cell)
        {
            return false;
        }

        OpenCellField(cell);
        return true;
    }

    /// <summary>Applies an operation at the caret's cell. Returns false, and does nothing, when the caret is not in a table cell.</summary>
    private bool ApplyAtCaret(TableOperation operation)
    {
        if (_view.TableCellAtCaret() is not { } cell)
        {
            return false;
        }

        ApplyOperation(cell, operation);
        return true;
    }

    /// <summary>Opens the table menu at the caret, or says that the caret is not in a table.</summary>
    private void ShowTableMenu()
    {
        if (_view.TableCellAtCaret() is not { } cell)
        {
            ShowMessage(NotInTable, error: true);
            return;
        }

        ShowMenu(TableItems(cell), _view.CursorScreenPosition);
    }

    /// <summary>
    /// The table menu's items for a cell: Edit cell, the row and column operations, and the alignments. An item whose operation would be refused is
    /// still shown, and choosing it gives the refusal's message.
    /// </summary>
    /// <param name="cell">The cell the items act on.</param>
    private List<MenuItem> TableItems(RenderedView.TableCell cell) =>
    [
        new MenuItem(commandText: "Edit cell", action: () => OpenCellField(cell)),
        TableItem("Insert row above", cell, (text, table, c) => AtColumn(TableEdit.InsertRow(text, table, c.Row), c.Column)),
        TableItem("Insert row below", cell, (text, table, c) => AtColumn(TableEdit.InsertRow(text, table, c.Row + 1), c.Column)),
        TableItem("Duplicate row", cell, (text, table, c) => AtColumn(TableEdit.DuplicateRow(text, table, c.Row), c.Column)),
        TableItem("Delete row", cell, (text, table, c) => AtColumn(TableEdit.DeleteRow(text, table, c.Row), c.Column)),
        TableItem("Move row up", cell, (text, table, c) => AtColumn(TableEdit.MoveRow(text, table, c.Row, -1), c.Column)),
        TableItem("Move row down", cell, (text, table, c) => AtColumn(TableEdit.MoveRow(text, table, c.Row, 1), c.Column)),
        new MenuItem(commandText: "Column", subMenu: new Menu(ColumnItems(cell))),
        new MenuItem(commandText: "Align column", subMenu: new Menu(AlignmentItems(cell))),
    ];

    /// <summary>The operations on a column, the submenu of "Column": insert left and right, delete, and move left and right.</summary>
    private List<MenuItem> ColumnItems(RenderedView.TableCell cell) =>
    [
        TableItem("Insert column left", cell, (text, table, c) => AtRow(TableEdit.InsertColumn(text, table, c.Column), c.Row)),
        TableItem("Insert column right", cell, (text, table, c) => AtRow(TableEdit.InsertColumn(text, table, c.Column + 1), c.Row)),
        TableItem("Delete column", cell, (text, table, c) => AtRow(TableEdit.DeleteColumn(text, table, c.Column), c.Row)),
        TableItem("Move column left", cell, (text, table, c) => AtRow(TableEdit.MoveColumn(text, table, c.Column, -1), c.Row)),
        TableItem("Move column right", cell, (text, table, c) => AtRow(TableEdit.MoveColumn(text, table, c.Column, 1), c.Row)),
    ];

    /// <summary>The alignments of a column, the submenu of "Align column": left, centre, right, or none.</summary>
    private List<MenuItem> AlignmentItems(RenderedView.TableCell cell) =>
    [
        TableItem("Left", cell, (text, table, c) => AtRow(TableEdit.SetAlignment(text, table, c.Column, ColumnAlignment.Left), c.Row)),
        TableItem("Centre", cell, (text, table, c) => AtRow(TableEdit.SetAlignment(text, table, c.Column, ColumnAlignment.Center), c.Row)),
        TableItem("Right", cell, (text, table, c) => AtRow(TableEdit.SetAlignment(text, table, c.Column, ColumnAlignment.Right), c.Row)),
        TableItem("None", cell, (text, table, c) => AtRow(TableEdit.SetAlignment(text, table, c.Column, null), c.Row)),
    ];

    private MenuItem TableItem(string label, RenderedView.TableCell cell, TableOperation operation) =>
        new(commandText: label, action: () => ApplyOperation(cell, operation));

    /// <summary>Runs an operation on the cell's table and applies its result (see <see cref="ApplyTableEdit"/>).</summary>
    private void ApplyOperation(RenderedView.TableCell cell, TableOperation operation) =>
        ApplyTableEdit(operation(_view.Document.Text, _view.Document.Blocks[cell.Block], cell), cell.Block);

    /// <summary>
    /// Applies a table result to the buffer as one change, renders the view from the new text, and puts the caret on the target cell. A refusal
    /// shows its message and changes nothing.
    /// </summary>
    /// <param name="result">The result of a <see cref="TableEdit"/> operation.</param>
    /// <param name="block">The index of the table's block in the document the result was made from.</param>
    /// <returns>The cell the caret went to, or null when the result is a refusal.</returns>
    private RenderedView.TableCell? ApplyTableEdit(TableEditResult result, int block)
    {
        if (result.IsRefused)
        {
            ShowMessage(result.Refusal!, error: true);
            return null;
        }

        // A table edit is a step of its own, which a typing run does not continue.
        EndRun();
        // The table starts where it did: an edit changes only its own text, so the block is found again by its start.
        int start = _view.Document.Blocks[block].Start;
        _editor.Buffer.Replace(result.Start, result.Length, result.Text);
        RenderBuffer();
        RefreshDirty();
        int target = TableBlockStartingAt(start) is { } landed ? landed : block;
        _view.PlaceTableCell(target, result.Row, result.Column);
        ShowStatus();
        return new RenderedView.TableCell(target, result.Row, result.Column);
    }

    /// <summary>The index of the table block that starts at an offset in the view's document, or null when there is none.</summary>
    private int? TableBlockStartingAt(int start)
    {
        for (int index = 0; index < _view.Document.Blocks.Length; index++)
        {
            if (_view.Document.Blocks[index] is { Table: not null } block && block.Start == start)
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>
    /// Opens the inline field on a cell: the view scrolls to show it, and the field holds the cell's source text (with <c>\|</c> shown as <c>|</c>).
    /// Does nothing when the cell is not in the layout.
    /// </summary>
    /// <param name="cell">The cell.</param>
    private void OpenCellField(RenderedView.TableCell cell)
    {
        CloseCellField();
        if (!_view.RevealTableCell(cell) || _view.TableCellRectangle(cell) is not { } area)
        {
            return;
        }

        string text = TableSource.Parse(_view.Document.Text, _view.Document.Blocks[cell.Block]).CellText(cell.Row, cell.Column);
        var field = new TableCellField
        {
            Text = text,
            X = area.X,
            Y = area.Y,
            Width = Math.Max(MinCellFieldWidth, _view.Viewport.Width - area.X),
        };
        field.ActionRequested += OnCellFieldAction;
        _cellField = field;
        _cellFieldCell = cell;
        _view.Add(field);
        field.SetFocus();
    }

    /// <summary>
    /// Handles the field's action. A cancel closes the field. Otherwise the text is written when it changed; a refused write keeps the field
    /// open with its text. Then the field closes, and a Tab or Shift+Tab opens the neighbouring cell.
    /// </summary>
    private void OnCellFieldAction(CellFieldAction action)
    {
        if (_cellField is not { } field)
        {
            return;
        }

        if (action == CellFieldAction.Cancel)
        {
            CloseCellField();
            return;
        }

        if (CommitCellText(field.Text, _cellFieldCell) is not { } landed)
        {
            return;
        }

        CloseCellField();
        bool? forward = action switch
        {
            CellFieldAction.NextCell => true,
            CellFieldAction.PreviousCell => false,
            _ => null,
        };
        if (forward is { } direction && NeighbourCell(landed, direction) is { } next)
        {
            OpenCellField(next);
        }
    }

    /// <summary>
    /// Writes a cell's text when it differs from the cell's. Returns the cell the caret goes to, or null when the write is refused (its message
    /// is shown).
    /// </summary>
    /// <param name="text">The field's text.</param>
    /// <param name="cell">The cell.</param>
    private RenderedView.TableCell? CommitCellText(string text, RenderedView.TableCell cell)
    {
        string source = _view.Document.Text;
        RenderBlock table = _view.Document.Blocks[cell.Block];
        if (text == TableSource.Parse(source, table).CellText(cell.Row, cell.Column))
        {
            return cell;
        }

        return ApplyTableEdit(TableEdit.SetCell(source, table, cell.Row, cell.Column, text), cell.Block);
    }

    /// <summary>
    /// The cell after (forward) or before a cell in reading order: the next column, or the next row's first cell after the last column. Null
    /// after the last cell, or before the first.
    /// </summary>
    private RenderedView.TableCell? NeighbourCell(RenderedView.TableCell cell, bool forward)
    {
        TableModel table = _view.Document.Blocks[cell.Block].Table!;
        int columns = table.Alignments.Length;
        if (forward)
        {
            if (cell.Column + 1 < columns)
            {
                return cell with { Column = cell.Column + 1 };
            }

            return cell.Row + 1 < table.Rows.Length ? new RenderedView.TableCell(cell.Block, cell.Row + 1, 0) : null;
        }

        if (cell.Column > 0)
        {
            return cell with { Column = cell.Column - 1 };
        }

        return cell.Row > 0 ? new RenderedView.TableCell(cell.Block, cell.Row - 1, columns - 1) : null;
    }

    /// <summary>Closes the inline field, if one is open, without writing anything: the field's text is written only by a commit.</summary>
    private void CloseCellField()
    {
        if (_cellField is not { } field)
        {
            return;
        }

        _cellField = null;
        field.ActionRequested -= OnCellFieldAction;
        _view.Remove(field);
        field.Dispose();
        _view.SetFocus();
    }
}
