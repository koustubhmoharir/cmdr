using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Cmdr.Tui;

/// <summary>The main window. Gives the application-wide keys a last chance when no focused view handled them.</summary>
internal sealed class MainWindow : Window
{
    private readonly MainController _controller;

    public MainWindow(MainController controller)
    {
        _controller = controller;
    }

    protected override bool OnKeyDown(Key key) => _controller.OnGlobalKey(key) || base.OnKeyDown(key);
}
