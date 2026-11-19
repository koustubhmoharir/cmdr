using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Logvu.Tui;

/// <summary>
/// The borderless main window: the status bar on the first row, the content view below it, the search bar's row while a search is open, and
/// the key hints on the last row. The content is the timeline, or the detail of an event while one is open. The window gives the
/// application-wide keys a last chance when no focused view handled them: first the search's shortcuts, then Esc.
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly Action _quit;
    private readonly Func<Key, bool> _shortcuts;
    private readonly Action _escape;
    private readonly View _searchBar;
    private View? _content;
    private int _bottomRows = 1;

    /// <summary>The first row of the content, below the status bar.</summary>
    private const int TopRows = 1;

    /// <summary>Creates the window and adds its views. The timeline is the first content, and the search bar's row is hidden.</summary>
    /// <param name="quit">Quits the application. Called for Ctrl+Q.</param>
    /// <param name="timeline">The timeline, the first content.</param>
    /// <param name="status">The status bar on the first row: the status text, inverted.</param>
    /// <param name="hints">The key hints on the last row.</param>
    /// <param name="searchBar">The search bar, on the row above the hints while it is shown.</param>
    /// <param name="shortcuts">
    /// Handles a key that no focused view took. Returns <c>true</c> when it handled the key. Called before Esc is looked at.
    /// </param>
    /// <param name="escape">Called for Esc when no focused view took it.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public MainWindow(Action quit, View timeline, View status, View hints, View searchBar, Func<Key, bool> shortcuts, Action escape)
    {
        ArgumentNullException.ThrowIfNull(quit);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(hints);
        ArgumentNullException.ThrowIfNull(searchBar);
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(escape);

        _quit = quit;
        _shortcuts = shortcuts;
        _escape = escape;
        _searchBar = searchBar;

        // No outer frame: the status bar, the content and the hints fill the whole window.
        BorderStyle = LineStyle.None;

        status.X = 0;
        status.Y = 0;
        status.Width = Dim.Fill();
        status.Height = 1;
        Add(status);

        hints.X = 0;
        hints.Y = Pos.AnchorEnd(1);
        hints.Width = Dim.Fill();
        hints.Height = 1;
        Add(hints);

        searchBar.X = 0;
        searchBar.Y = Pos.AnchorEnd(2);
        searchBar.Width = Dim.Fill();
        searchBar.Height = 1;
        searchBar.Visible = false;
        Add(searchBar);

        SetContent(timeline);
    }

    /// <summary>The view that fills the rows between the status bar and the hints (and the search bar while it is shown).</summary>
    public View? Content => _content;

    /// <summary>
    /// Shows a view in the rows between the status bar and the hints in place of the current content, and gives it the focus. The status bar,
    /// the hints and the search bar's row stay. The view it replaces is removed, not disposed: its owner disposes it when it is done with it.
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
        content.Y = TopRows;
        content.Width = Dim.Fill();
        content.Height = Dim.Fill(_bottomRows);
        Add(content);
        _content = content;
        content.SetFocus();
    }

    /// <summary>
    /// Shows or hides the search bar's row. While it is shown the content stops one row higher, so the bar takes the row under it.
    /// </summary>
    /// <param name="open">Whether the row is shown.</param>
    public void ShowSearchBar(bool open)
    {
        _searchBar.Visible = open;
        _bottomRows = open ? 2 : 1;
        if (_content is not null)
        {
            _content.Height = Dim.Fill(_bottomRows);
        }

        SetNeedsLayout();
    }

    /// <remarks>
    /// Esc keeps Terminal.Gui's default Quit binding out of the way: a view that does not use Esc leaves it here, and it is swallowed after
    /// the escape callback has run.
    /// </remarks>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Q.WithCtrl)
        {
            _quit();
            return true;
        }

        if (_shortcuts(key))
        {
            return true;
        }

        if (key == Key.Esc)
        {
            _escape();
            return true;
        }

        return base.OnKeyDown(key);
    }
}
