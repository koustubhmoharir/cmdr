using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Logvu.Tui;

/// <summary>
/// The borderless main window: the timeline above the status line. It gives the application-wide keys a last chance when no
/// focused view handled them.
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly Action _quit;

    /// <summary>Creates the window and adds its views.</summary>
    /// <param name="quit">Quits the application. Called for Ctrl+Q.</param>
    /// <param name="timeline">The timeline, which fills every row but the last.</param>
    /// <param name="status">The status line on the last row.</param>
    public MainWindow(Action quit, View timeline, View status)
    {
        ArgumentNullException.ThrowIfNull(quit);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(status);

        _quit = quit;

        // No outer frame: the timeline is the whole window.
        BorderStyle = LineStyle.None;

        timeline.X = 0;
        timeline.Y = 0;
        timeline.Width = Dim.Fill();
        timeline.Height = Dim.Fill(1);
        Add(timeline);

        status.X = 0;
        status.Y = Pos.AnchorEnd(1);
        status.Width = Dim.Fill();
        status.Height = 1;
        Add(status);
    }

    /// <remarks>
    /// Esc keeps Terminal.Gui's default Quit binding out of the way: it does nothing in the timeline (it is reserved for closing the
    /// detail and search later), so an Esc that no view handled is swallowed here.
    /// </remarks>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Q.WithCtrl)
        {
            _quit();
            return true;
        }

        if (key == Key.Esc)
        {
            return true;
        }

        return base.OnKeyDown(key);
    }
}
