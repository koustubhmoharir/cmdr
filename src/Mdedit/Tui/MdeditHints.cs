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
    /// The key hints of the viewer. Find and Copy are not listed yet: their keys are not bound until the next part of the viewer.
    /// The quit key is the right part (see <see cref="Quit"/>), so it stays on the row when the left part is cut.
    /// </summary>
    public static Hint View { get; } = KeyHints.Bound(Key.O.WithCtrl, "^O Open");

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
