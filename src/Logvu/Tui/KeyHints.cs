using System.Text;
using Terminal.Gui.Input;

namespace Logvu.Tui;

/// <summary>
/// A span of a hint's text that a click performs a key for: the span in characters of the text, and the key.
/// </summary>
/// <param name="Start">The index of the span's first character in the hint's text.</param>
/// <param name="Length">The number of characters in the span.</param>
/// <param name="Key">The key that a click on the span performs.</param>
internal readonly record struct HintTarget(int Start, int Length, Key Key);

/// <summary>
/// A key hint: its text, as the bottom row shows it, and the spans of the text that a click performs a key for. Text outside the spans
/// does nothing when it is clicked.
/// </summary>
/// <param name="Text">The text of the hint.</param>
/// <param name="Targets">The spans of the text, in order and not overlapping.</param>
internal sealed record Hint(string Text, IReadOnlyList<HintTarget> Targets);

/// <summary>
/// The key hints of the timeline and the detail, as data: the text each shows, and the keys its labels perform when clicked. The texts are
/// the ones the status line has always shown; a hint's text is built from its parts, so the data and the text cannot differ. Pure.
/// </summary>
internal static class KeyHints
{
    /// <summary>The separator between the hints on the row.</summary>
    public const string Separator = "  ";

    /// <summary>The key hints of the timeline with no selection.</summary>
    public static Hint Timeline { get; } = TimelineWith("c copy event");

    /// <summary>The key hints of the timeline while its selected row has a selection, which <c>c</c> copies.</summary>
    public static Hint TimelineSelection { get; } = TimelineWith("c copy selection");

    /// <summary>The key hints of the detail with no selection.</summary>
    public static Hint Detail { get; } = DetailWith("c copy line");

    /// <summary>The key hints of the detail while it has a selection, which <c>c</c> copies.</summary>
    public static Hint DetailSelection { get; } = DetailWith("c copy selection");

    /// <summary>A hint whose whole text performs <paramref name="key"/>: a key and its label, such as <c>f filter</c>.</summary>
    /// <param name="key">The key the text performs.</param>
    /// <param name="text">The text of the hint.</param>
    public static Hint Bound(Key key, string text) => new(text, [new HintTarget(0, text.Length, key)]);

    /// <summary>A hint with no targets: text that a click does nothing for.</summary>
    /// <param name="text">The text of the hint.</param>
    public static Hint Plain(string text) => new(text, []);

    /// <summary>
    /// Joins hints end to end, without a separator. The targets of each part move with it, so a part can be split into bound and plain text.
    /// </summary>
    /// <param name="parts">The parts, in order.</param>
    public static Hint Concat(params Hint[] parts)
    {
        var text = new StringBuilder();
        var targets = new List<HintTarget>();
        foreach (Hint part in parts)
        {
            foreach (HintTarget target in part.Targets)
            {
                targets.Add(target with { Start = target.Start + text.Length });
            }

            text.Append(part.Text);
        }

        return new Hint(text.ToString(), targets);
    }

    /// <summary>Joins hints with <see cref="Separator"/>, as the row shows them.</summary>
    /// <param name="hints">The hints, in order.</param>
    public static Hint Join(params Hint[] hints)
    {
        var parts = new List<Hint>(hints.Length * 2 - 1);
        for (int i = 0; i < hints.Length; i++)
        {
            if (i > 0)
            {
                parts.Add(Plain(Separator));
            }

            parts.Add(hints[i]);
        }

        return Concat([.. parts]);
    }

    private static Hint TimelineWith(string copy) => Join(
        Bound('f', "f filter"),
        Concat(Bound('-', "-"), Plain("/"), Bound('+', "+"), Plain(" level")),
        Bound(Key.Space, "Space pause/follow"),
        Bound('c', copy),
        Bound(Key.End, "End newest"),
        Bound(Key.O.WithCtrl, "^O open"),
        Bound(Key.Q.WithCtrl, "^Q quit"));

    private static Hint DetailWith(string copy) => Join(
        Bound('f', "f filter on value"),
        Bound(Key.Enter, "Enter toggle"),
        Bound('z', "z folds"),
        Bound('r', "r raw"),
        Bound('c', copy),
        Bound(Key.Esc, "←/Esc back"));
}
