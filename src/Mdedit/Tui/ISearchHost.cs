namespace Mdedit.Tui;

/// <summary>What the view search needs from the window that owns it: the bar's row, the focus and the message line.</summary>
internal interface ISearchHost
{
    /// <summary>Shows or hides the search bar's row, above the message line.</summary>
    /// <param name="open">True to show the bar.</param>
    void ShowSearchBar(bool open);

    /// <summary>Gives the focus back to the rendered view.</summary>
    void FocusView();

    /// <summary>Shows a message on the message line.</summary>
    /// <param name="text">The message.</param>
    /// <param name="error">Whether the message is an error.</param>
    void ShowMessage(string text, bool error);
}
