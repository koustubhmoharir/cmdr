using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Mdedit.Tui;

/// <summary>What a table cell's inline field asks for: a commit, a cancel, or a move to the next or previous cell after a commit.</summary>
internal enum CellFieldAction
{
    /// <summary>Enter: the text is written to the cell, and the field closes.</summary>
    Commit,

    /// <summary>Esc: the field closes, and the cell keeps its text.</summary>
    Cancel,

    /// <summary>Tab: the text is written, and the next cell is edited (the next row's first cell after the last column).</summary>
    NextCell,

    /// <summary>Shift+Tab: the text is written, and the previous cell is edited.</summary>
    PreviousCell,
}

/// <summary>
/// The inline field that edits a table cell: a text field over the cell's first screen line, with the cell's source text (<c>\|</c> shown as
/// <c>|</c>). Enter, Esc, Tab and Shift+Tab are not the text field's: they are raised as a <see cref="CellFieldAction"/> for the window,
/// which commits or cancels. Every other key is the text field's own, so the window never sees the keys of the text.
/// </summary>
internal sealed class TableCellField : TextField
{
    /// <summary>Raised when Enter, Esc, Tab or Shift+Tab is pressed in the field.</summary>
    public event Action<CellFieldAction>? ActionRequested;

    /// <summary>Takes Enter, Esc, Tab and Shift+Tab as actions; the other keys are the text field's.</summary>
    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
        CellFieldAction? action = null;
        if (key == Key.Enter)
        {
            action = CellFieldAction.Commit;
        }
        else if (key == Key.Esc)
        {
            action = CellFieldAction.Cancel;
        }
        else if (key == Key.Tab)
        {
            action = CellFieldAction.NextCell;
        }
        else if (key == Key.Tab.WithShift)
        {
            action = CellFieldAction.PreviousCell;
        }

        if (action is not { } chosen)
        {
            return base.OnKeyDown(key);
        }

        ActionRequested?.Invoke(chosen);
        return true;
    }
}
