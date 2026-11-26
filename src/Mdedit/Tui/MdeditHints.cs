using Shared.Tui;
using Terminal.Gui.Input;

namespace Mdedit.Tui;

/// <summary>
/// The key hints of the viewer and of the pickers, as data: the text each shows, and the keys its labels perform when clicked (see
/// <see cref="KeyHints"/>).
/// </summary>
internal static class MdeditHints
{
    // The Open file hint of both pickers: Ctrl+O opens a file through a file dialog. Declared first, as the pickers use it.
    private static Hint OpenFile { get; } = KeyHints.Bound(Key.O.WithCtrl, "^O Open file");

    /// <summary>
    /// The key hints of the viewer, the most important first, so that the row keeps them when it is cut: Edit, Save, Find, Copy, Fold, Open. The
    /// quit key is the right part (see <see cref="Quit"/>), so it stays on the row when the left part is cut.
    /// </summary>
    public static Hint View { get; } = KeyHints.Join(
        KeyHints.Bound(Key.T.WithCtrl, "^T Edit"),
        KeyHints.Bound(Key.S.WithCtrl, "^S Save"),
        KeyHints.Bound('/', "/ Find"),
        KeyHints.Bound('y', "y Copy"),
        KeyHints.Bound(Key.Space, "Space Fold"),
        KeyHints.Bound(Key.O.WithCtrl, "^O Open"));

    /// <summary>
    /// The key hints of edit mode: the view, the save, the find and the word wrap, then the quit key at the right.
    /// </summary>
    public static Hint Edit { get; } = KeyHints.Join(
        KeyHints.Bound(Key.T.WithCtrl, "^T View"),
        KeyHints.Bound(Key.S.WithCtrl, "^S Save"),
        KeyHints.Bound(Key.F.WithCtrl, "^F Find"),
        KeyHints.Bound(SourceEditor.WrapKey, "Alt+Z Wrap"));

    /// <summary>The quit key, shown at the right edge of the viewer's hints.</summary>
    public static Hint Quit { get; } = KeyHints.Bound(Key.Q.WithCtrl, "^Q Quit");

    /// <summary>The key hints of the picker at startup. Esc and Ctrl+Q quit.</summary>
    public static Hint Picker { get; } = KeyHints.Join(
        KeyHints.Plain("0-9 Select by number"),
        OpenFile,
        KeyHints.Bound(Key.Delete, "Del Remove"),
        KeyHints.Bound(Key.Enter, "Enter Open"),
        KeyHints.Concat(KeyHints.Bound(Key.Esc, "Esc"), KeyHints.Plain("/"), KeyHints.Bound(Key.Q.WithCtrl, "^Q"), KeyHints.Plain(" Quit")));

    /// <summary>The key hints of the picker shown over the viewer. Esc goes back to the document, and Ctrl+Q quits.</summary>
    public static Hint OpenPicker { get; } = KeyHints.Join(
        KeyHints.Plain("0-9 Select by number"),
        OpenFile,
        KeyHints.Bound(Key.Delete, "Del Remove"),
        KeyHints.Bound(Key.Esc, "Esc back"),
        KeyHints.Bound(Key.Enter, "Enter Open"),
        KeyHints.Bound(Key.Q.WithCtrl, "^Q quit"));
}
