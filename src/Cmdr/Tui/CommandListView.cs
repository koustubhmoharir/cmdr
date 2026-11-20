using Cmdr.Core.Session;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Cmdr.Tui;

/// <summary>
/// The command list. Items are display lines: the selection only ever rests on a main line, never on a detail line. Navigation
/// keys are handled here because ListView's own movement methods are not virtual and would step onto detail lines. Key and
/// Ctrl+click handling is delegated to the controller; ListView's own marking is off.
/// </summary>
internal sealed class CommandListView : ListView
{
    private readonly MainController _controller;
    private readonly CommandListSource _source;

    public CommandListView(MainController controller, CommandListSource source)
    {
        _controller = controller;
        _source = source;
        CanFocus = true;
        ShowMarks = false;
        MarkMultiple = false;
        Source = source;
    }

    private DisplayLines Lines => _source.Lines;

    /// <summary>
    /// Scrolls so that both lines of the selected command (or the one line of a group header) are visible, when they fit
    /// in the viewport. Call after the selection or the rows change.
    /// </summary>
    public void RevealFocusedBlock()
    {
        if (SelectedItem is not int line || Lines.LineToRow(line) < 0)
        {
            return;
        }

        // Not laid out yet: there is no height to reveal in, and a reveal against the height of one would scroll the top
        // of the list out of view (the first focus is on a line below the file row). The first layout keeps the top.
        if (Viewport.Height <= 0)
        {
            return;
        }

        int row = Lines.LineToRow(line);
        int first = Lines.RowToLine(row);
        int last = Lines.LastLineOfRow(row);
        int height = Math.Max(1, Viewport.Height);
        int top = Viewport.Y;
        int newTop = top;

        if (last - first + 1 > height)
        {
            // Taller than the viewport: show its first line.
            if (first < top || first >= top + height)
            {
                newTop = first;
            }
        }
        else
        {
            if (first < newTop)
            {
                newTop = first;
            }

            if (last >= newTop + height)
            {
                newTop = last - height + 1;
            }
        }

        if (newTop != top)
        {
            ScrollVertical(newTop - top);
        }
    }

    protected override bool OnKeyDown(Key key)
    {
        if (_controller.OnListKey(key))
        {
            return true;
        }

        if (NavigateKey(key))
        {
            return true;
        }

        return base.OnKeyDown(key);
    }

    /// <summary>A detail line can never be selected: a selection of one moves to its command's main line.</summary>
    protected override bool OnValueChanging(ValueChangingEventArgs<int?> args)
    {
        if (args.NewValue is int line && Lines.IsDetail(line))
        {
            args.NewValue = Lines.MainLineOf(line);
        }

        return base.OnValueChanging(args);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        bool click = mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed);
        if (click && mouse.Flags.HasFlag(MouseFlags.Ctrl))
        {
            // The mark goes to the row under the pointer, whatever the focus was before the click.
            int clicked = RowAt(mouse);
            bool handled = base.OnMouseEvent(mouse);
            if (clicked >= 0)
            {
                SelectedItem = Lines.RowToLine(clicked);
            }

            RevealFocusedBlock();
            _controller.OnCtrlClick(clicked);
            return handled;
        }

        int glyphRow = click ? GroupGlyphRowAt(mouse) : -1;
        if (glyphRow >= 0)
        {
            // The toggle is on the press, as a row's selection is not: the controller focuses the header before it toggles the group.
            bool handled = base.OnMouseEvent(mouse);
            RevealFocusedBlock();
            _controller.OnGroupGlyphClick(glyphRow);
            return handled;
        }

        bool result = base.OnMouseEvent(mouse);
        if (click)
        {
            RevealFocusedBlock();
        }

        return result;
    }

    /// <summary>The row under a mouse position in this view (a detail line counts as its command), or -1.</summary>
    private int RowAt(Mouse mouse)
    {
        if (mouse.Position is not { } position)
        {
            return -1;
        }

        return Lines.LineToRow(Viewport.Y + position.Y);
    }

    /// <summary>The file or group header row under a mouse position when that position is its ▾ or ▸ cell, or -1.</summary>
    private int GroupGlyphRowAt(Mouse mouse)
    {
        if (mouse.Position is not { } position)
        {
            return -1;
        }

        int row = RowAt(mouse);
        if (row < 0 || row >= _source.Rows.Count)
        {
            return -1;
        }

        // A file row is at depth 0, the list's top level; a group header at its list depth (see TreeRows.ListDepth).
        int glyphColumn = _source.Rows[row] switch
        {
            FileRow => RowLayout.GroupGlyphColumn(0),
            GroupRow group => RowLayout.GroupGlyphColumn(TreeRows.ListDepth(group.Group)),
            _ => -1,
        };
        return glyphColumn >= 0 && position.X == glyphColumn ? row : -1;
    }

    /// <summary>Up, Down, PageUp, PageDown, Home and End over the main lines. Returns true when the key was handled.</summary>
    private bool NavigateKey(Key key)
    {
        int current = SelectedItem ?? -1;
        int target;
        if (key == Key.CursorUp)
        {
            target = Lines.NextFocusableLine(current, -1);
        }
        else if (key == Key.CursorDown)
        {
            target = Lines.NextFocusableLine(current, 1);
        }
        else if (key == Key.PageUp)
        {
            target = PageTarget(current, -1);
        }
        else if (key == Key.PageDown)
        {
            target = PageTarget(current, 1);
        }
        else if (key == Key.Home)
        {
            target = Lines.FirstFocusableLine;
        }
        else if (key == Key.End)
        {
            target = Lines.LastFocusableLine;
        }
        else
        {
            return false;
        }

        if (target >= 0 && target != SelectedItem)
        {
            SelectedItem = target;
        }

        RevealFocusedBlock();
        return true;
    }

    /// <summary>A page away from the current line, on a main line. Without a current line the first line is used.</summary>
    private int PageTarget(int current, int direction)
    {
        if (Lines.Count == 0)
        {
            return -1;
        }

        if (current < 0)
        {
            return Lines.FirstFocusableLine;
        }

        int page = Math.Max(1, Viewport.Height);
        int target = Math.Clamp(current + direction * page, 0, Lines.Count - 1);
        return Lines.MainLineOf(target);
    }
}
