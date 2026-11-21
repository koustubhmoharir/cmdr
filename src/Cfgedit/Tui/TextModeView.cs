using Terminal.Gui.Views;

// TextView is marked obsolete in Terminal.Gui 2.5.0; cmdr's dialogs use it for their multi-line fields (see CommandDialog).
#pragma warning disable CS0618

namespace Cfgedit.Tui;

/// <summary>
/// The text mode's editor: the file's text in a multi-line text view that fills the tree's place in the window. Tab adds a tab character,
/// as the file's text needs, rather than moving the focus.
/// </summary>
internal sealed class TextModeView : TextView
{
    /// <summary>Creates the view with no text.</summary>
    public TextModeView()
    {
        Multiline = true;
        TabKeyAddsTab = true;
    }
}
