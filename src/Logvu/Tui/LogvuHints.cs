using Shared.Tui;
using Terminal.Gui.Input;

namespace Logvu.Tui;

/// <summary>
/// The key hints of the timeline and the detail, as data: the text each shows, and the keys its labels perform when clicked. The texts are
/// the ones the status line has always shown; a hint's text is built from its parts (see <see cref="KeyHints"/>), so the data and the text
/// cannot differ. Pure.
/// </summary>
internal static class LogvuHints
{
    /// <summary>The key hints of the timeline with no selection.</summary>
    public static Hint Timeline { get; } = TimelineWith("c copy event");

    /// <summary>The key hints of the timeline while its selected row has a selection, which <c>c</c> copies.</summary>
    public static Hint TimelineSelection { get; } = TimelineWith("c copy selection");

    /// <summary>The key hints of the detail with no selection.</summary>
    public static Hint Detail { get; } = DetailWith("c copy line");

    /// <summary>The key hints of the detail while it has a selection, which <c>c</c> copies.</summary>
    public static Hint DetailSelection { get; } = DetailWith("c copy selection");

    private static Hint TimelineWith(string copy) => KeyHints.Join(
        KeyHints.Bound('f', "f filter"),
        KeyHints.Concat(KeyHints.Bound('-', "-"), KeyHints.Plain("/"), KeyHints.Bound('+', "+"), KeyHints.Plain(" level")),
        KeyHints.Bound(Key.Space, "Space pause/follow"),
        KeyHints.Bound('c', copy),
        KeyHints.Bound(Key.End, "End newest"),
        KeyHints.Bound(Key.O.WithCtrl, "^O open"),
        KeyHints.Bound(Key.Q.WithCtrl, "^Q quit"));

    private static Hint DetailWith(string copy) => KeyHints.Join(
        KeyHints.Bound('f', "f filter on value"),
        KeyHints.Bound(Key.Enter, "Enter toggle"),
        KeyHints.Bound('z', "z folds"),
        KeyHints.Bound('r', "r raw"),
        KeyHints.Bound('c', copy),
        KeyHints.Bound(Key.Esc, "←/Esc back"));
}
