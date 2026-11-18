namespace Cmdr.Tui;

/// <summary>
/// The hint bar's keys while the find field of the search bar has the focus, in place of the pane hints. The last entry names
/// what Esc is labelled for: "Esc Clear" while the field has text, "Esc Close" when it is empty. Pure.
/// </summary>
internal static class SearchHint
{
    private const string Keys = "F3 Next  ⇧F3 Prev  ↑↓ History  Alt+C Case  Alt+W Word  Alt+R Regex  Enter Pane";

    /// <summary>
    /// The search keys, ending "Esc Clear" when <paramref name="hasText"/> is true and "Esc Close" otherwise.
    /// </summary>
    public static string Describe(bool hasText) => Keys + (hasText ? "  Esc Clear" : "  Esc Close");
}
