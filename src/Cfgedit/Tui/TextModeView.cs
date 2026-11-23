using Shared.Tui;

namespace Cfgedit.Tui;

/// <summary>
/// The text mode's editor: the file's text in a text area that fills the tree's place in the window. Tab adds a tab character,
/// as the file's text needs, rather than moving the focus.
/// </summary>
internal sealed class TextModeView : TextArea
{
    /// <summary>Creates the view with no text.</summary>
    public TextModeView()
    {
        TabInserts = true;
    }
}
