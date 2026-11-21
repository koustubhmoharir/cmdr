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

    /// <summary>The key hints of the main window.</summary>
    public static Hint Tree { get; } = KeyHints.Join(
        KeyHints.Bound(Key.Space, "Space Fold"),
        KeyHints.Concat(KeyHints.Bound(Key.CursorLeft, "\x2190"), KeyHints.Bound(Key.CursorRight, "\x2192"), KeyHints.Plain(" Part")),
        KeyHints.Concat(KeyHints.Bound(Key.O.WithCtrl, "^O"), KeyHints.Plain(" Open")),
        KeyHints.Concat(KeyHints.Bound(Key.Q.WithCtrl, "^Q"), KeyHints.Plain(" Quit")));

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
