using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Cmdr.Tui;

/// <summary>The command list. Key and Ctrl+click handling is delegated to the controller; ListView's own marking is off.</summary>
internal sealed class CommandListView : ListView
{
    private readonly MainController _controller;

    public CommandListView(MainController controller, CommandListSource source)
    {
        _controller = controller;
        CanFocus = true;
        ShowMarks = false;
        MarkMultiple = false;
        Source = source;
    }

    protected override bool OnKeyDown(Key key) => _controller.OnListKey(key) || base.OnKeyDown(key);

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) && mouse.Flags.HasFlag(MouseFlags.Ctrl))
        {
            // The base click moves focus to the clicked row; the controller then toggles that row's mark.
            bool handled = base.OnMouseEvent(mouse);
            _controller.OnCtrlClick();
            return handled;
        }

        return base.OnMouseEvent(mouse);
    }
}
