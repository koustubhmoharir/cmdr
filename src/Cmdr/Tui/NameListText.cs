using Cmdr.Core.Config;

namespace Cmdr.Tui;

/// <summary>
/// The text of a list of variable names in a single-line field: names separated by commas (for example the <c>secret</c> names).
/// Names are trimmed and blank entries are ignored. Pure.
/// </summary>
internal static class NameListText
{
    /// <summary>Joins the names with commas and a space. Null and an empty list give empty text.</summary>
    public static string Format(IReadOnlyList<string>? names) => names is null ? string.Empty : string.Join(", ", names);

    /// <summary>Splits the text on commas. Each name is trimmed, and blank entries are dropped.</summary>
    public static IReadOnlyList<string> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Split(',')
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .ToList();
    }

    /// <summary>
    /// True when <see cref="Parse"/> of <see cref="Format"/> gives back exactly <paramref name="names"/>. False for a name the comma form
    /// cannot hold: one with a comma, a line break, a space at either end, or an empty name. Null is true (it is written as empty text).
    /// </summary>
    public static bool CanEdit(IReadOnlyList<string>? names)
    {
        if (names is null)
        {
            return true;
        }

        if (names.Any(name => name.Length == 0 || name.IndexOfAny(LineBreaksAndComma) >= 0))
        {
            return false;
        }

        return Parse(Format(names)).SequenceEqual(names, StringComparer.Ordinal);
    }

    /// <summary>A copy of the names, or null when there are none (an absent setting is not written).</summary>
    public static List<string>? NamesOrNull(IReadOnlyList<string> names) => names.Count == 0 ? null : names.ToList();

    /// <summary>
    /// The errors of the names in the text, with the rules of the configuration check (see <see cref="CmdrConfig.EnvNameErrors"/>).
    /// <paramref name="label"/> names the field in each error, for example "Secret".
    /// </summary>
    public static IReadOnlyList<string> Errors(string text, string label) =>
        CmdrConfig.EnvNameErrors(Parse(text), label);

    private static readonly char[] LineBreaksAndComma = { ',', '\r', '\n' };
}
