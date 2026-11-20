using Shared.Tui;
using Terminal.Gui.Input;

namespace Cmdr.Tui;

/// <summary>
/// The hint bar's keys while the find field of the search bar has the focus, in place of the pane hints. The last entry names
/// what Esc is labelled for: "Esc Clear" while the field has text, "Esc Close" when it is empty. Each label is a click target for
/// its key; ↑ and ↓ are targets of their own, and "History" is plain text. Pure.
/// </summary>
internal static class SearchHint
{
    /// <summary>
    /// The search keys with their click targets, ending "Esc Clear" when <paramref name="hasText"/> is true and "Esc Close" otherwise.
    /// </summary>
    public static Hint Build(bool hasText) => KeyHints.Join(
        KeyHints.Bound(Key.F3, "F3 Next"),
        KeyHints.Bound(Key.F3.WithShift, "⇧F3 Prev"),
        KeyHints.Concat(KeyHints.Bound(Key.CursorUp, "↑"), KeyHints.Bound(Key.CursorDown, "↓"), KeyHints.Plain(" History")),
        KeyHints.Bound(Key.C.WithAlt, "Alt+C Case"),
        KeyHints.Bound(Key.W.WithAlt, "Alt+W Word"),
        KeyHints.Bound(Key.R.WithAlt, "Alt+R Regex"),
        KeyHints.Bound(Key.Enter, "Enter Pane"),
        KeyHints.Bound(Key.Esc, hasText ? "Esc Clear" : "Esc Close"));

    /// <summary>
    /// The search keys, ending "Esc Clear" when <paramref name="hasText"/> is true and "Esc Close" otherwise.
    /// </summary>
    public static string Describe(bool hasText) => Build(hasText).Text;
}
