using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Cmdr.Tui;

/// <summary>The borderless main window. Gives the application-wide keys a last chance when no focused view handled them.</summary>
internal sealed class MainWindow : Window
{
    private readonly MainController _controller;

    public MainWindow(MainController controller)
    {
        _controller = controller;

        // No outer frame: the panes have their own borders, and the config file name is shown in the list pane's title.
        BorderStyle = LineStyle.None;
    }

    /// <remarks>
    /// Esc keeps Terminal.Gui's default Quit binding so that dialogs can be cancelled with it. In the main window Esc means
    /// "stop", so an Esc that no pane handled is swallowed here; otherwise it would quit without the quit flow's prompts.
    /// </remarks>
    protected override bool OnKeyDown(Key key) => _controller.OnGlobalKey(key) || key == Key.Esc || base.OnKeyDown(key);
}
