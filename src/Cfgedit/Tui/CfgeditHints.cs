using Shared.Tui;
using Terminal.Gui.Input;

namespace Cfgedit.Tui;

/// <summary>
/// The key hints of the tree and of the pickers, as data: the text each shows, and the keys its labels perform when clicked (see
/// <see cref="KeyHints"/>). The texts are built from their parts, so the data and the text cannot differ.
/// </summary>
internal static class CfgeditHints
{
    // The Open file hint of both pickers: o and Ctrl+O open a file through a file dialog. Declared first, as the pickers use it.
    private static Hint OpenFile { get; } = KeyHints.Concat(
        KeyHints.Bound('o', "o"), KeyHints.Plain("/"), KeyHints.Bound(Key.O.WithCtrl, "^O"), KeyHints.Plain(" Open file"));

    /// <summary>
    /// The key hints of the main window, with no editor open. The quit key is the right part (see <see cref="Quit"/>), so it stays on
    /// the row when the left part is cut.
    /// </summary>
    public static Hint Tree { get; } = KeyHints.Join(
        KeyHints.Bound(Key.Enter, "Enter Edit"),
        KeyHints.Bound(Key.Tab, "Tab Next"),
        KeyHints.Bound(Key.A, "a Add"),
        KeyHints.Bound(Key.D, "d Del"),
        KeyHints.Bound(Key.T, "t Type"),
        KeyHints.Bound(Key.Z.WithCtrl, "^Z Undo"),
        KeyHints.Bound(Key.S.WithCtrl, "^S Save"),
        KeyHints.Bound(Key.O.WithCtrl, "^O Open"));

    /// <summary>The key hints while the inline editor is open.</summary>
    public static Hint Editing { get; } = KeyHints.Join(
        KeyHints.Bound(Key.Enter, "Enter Commit"),
        KeyHints.Bound(Key.Esc, "Esc Cancel"),
        KeyHints.Bound(Key.Tab, "Tab Next"),
        KeyHints.Bound(Key.F4, "F4 Multi-line"));

    /// <summary>The key hints while the text is shown (text mode). The quit key stays on the right, as in the tree.</summary>
    public static Hint TextMode { get; } = KeyHints.Join(
        KeyHints.Bound(Key.F5, "F5 Parse"),
        KeyHints.Bound(Key.T.WithCtrl, "^T Tree"),
        KeyHints.Bound(Key.S.WithCtrl, "^S Save"));

    /// <summary>The quit key, shown at the right edge of the main window's hints.</summary>
    public static Hint Quit { get; } = KeyHints.Bound(Key.Q.WithCtrl, "^Q Quit");

    /// <summary>The key hints of the picker at startup. Esc and Ctrl+Q quit.</summary>
    public static Hint Picker { get; } = KeyHints.Join(
        KeyHints.Bound(Key.Enter, "Enter Open"),
        KeyHints.Bound(Key.Delete, "Del Remove"),
        OpenFile,
        KeyHints.Plain("0-9 Select by number"),
        KeyHints.Concat(KeyHints.Bound(Key.Esc, "Esc"), KeyHints.Plain("/"), KeyHints.Bound(Key.Q.WithCtrl, "^Q"), KeyHints.Plain(" Quit")));

    /// <summary>The key hints of the picker shown over the main window. Esc goes back to the document, and Ctrl+Q quits.</summary>
    public static Hint OpenPicker { get; } = KeyHints.Join(
        KeyHints.Bound(Key.Enter, "Enter Open"),
        KeyHints.Bound(Key.Delete, "Del Remove"),
        OpenFile,
        KeyHints.Plain("0-9 Select by number"),
        KeyHints.Bound(Key.Esc, "Esc back"),
        KeyHints.Bound(Key.Q.WithCtrl, "^Q quit"));
}
