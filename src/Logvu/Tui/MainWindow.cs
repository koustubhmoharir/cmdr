using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Logvu.Tui;

/// <summary>
/// The borderless main window: the content view above the status line. The content is the timeline, or the detail of an event
/// while one is open. It gives the application-wide keys a last chance when no focused view handled them.
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly Action _quit;
    private View? _content;

    /// <summary>Creates the window and adds its views. The timeline is the first content.</summary>
    /// <param name="quit">Quits the application. Called for Ctrl+Q.</param>
    /// <param name="timeline">The timeline, the first content, which fills every row but the last.</param>
    /// <param name="status">The status line on the last row.</param>
    public MainWindow(Action quit, View timeline, View status)
    {
        ArgumentNullException.ThrowIfNull(quit);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(status);

        _quit = quit;

        // No outer frame: the content is the whole window above the status line.
        BorderStyle = LineStyle.None;

        status.X = 0;
        status.Y = Pos.AnchorEnd(1);
        status.Width = Dim.Fill();
        status.Height = 1;
        Add(status);

        SetContent(timeline);
    }

    /// <summary>The view that fills the rows above the status line.</summary>
    public View? Content => _content;

    /// <summary>
    /// Shows a view in the rows above the status line in place of the current content, and gives it the focus. The status line stays.
    /// The view it replaces is removed, not disposed: its owner disposes it when it is done with it.
    /// </summary>
    /// <param name="content">The view to show.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <c>null</c>.</exception>
    public void SetContent(View content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (ReferenceEquals(content, _content))
        {
            return;
        }

        if (_content is not null)
        {
            Remove(_content);
        }

        content.X = 0;
        content.Y = 0;
        content.Width = Dim.Fill();
        content.Height = Dim.Fill(1);
        Add(content);
        _content = content;
        content.SetFocus();
    }

    /// <remarks>
    /// Esc keeps Terminal.Gui's default Quit binding out of the way: the timeline does nothing with it, and the detail closes on it
    /// itself, so an Esc that no view handled is swallowed here.
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
