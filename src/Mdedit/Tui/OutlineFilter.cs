using Mdedit.Core.Render;

namespace Mdedit.Tui;

/// <summary>A row of the outline list: the heading's start in the document, and the label the list shows.</summary>
/// <param name="Start">The heading's start offset in the document.</param>
/// <param name="Label">The row's text: the heading's indent, then its text.</param>
internal sealed record OutlineRow(int Start, string Label);

/// <summary>
/// The rows of the outline dialog, as pure data: the headings of <see cref="Outline.Of"/> that match the text typed in the filter, and the
/// index the list starts on. No user interface is involved, so the rules are tested without one.
/// </summary>
internal static class OutlineFilter
{
    /// <summary>The spaces of indent per heading level below level 1: a level-2 heading is indented by two spaces.</summary>
    internal const int IndentPerLevel = 2;

    /// <summary>
    /// The rows whose heading text contains <paramref name="filter"/>, ignoring case, in document order. An empty filter gives every heading.
    /// Each row keeps its indent: the filter is matched against the heading's text, not the label.
    /// </summary>
    /// <param name="entries">The headings, in document order.</param>
    /// <param name="filter">The text typed in the filter field.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal static IReadOnlyList<OutlineRow> Rows(IReadOnlyList<OutlineEntry> entries, string filter)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(filter);

        return entries
            .Where(entry => entry.Text.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new OutlineRow(entry.Start, Label(entry)))
            .ToList();
    }

    /// <summary>The label of a heading: its indent (<see cref="IndentPerLevel"/> spaces per level below 1), then its text.</summary>
    /// <param name="entry">The heading.</param>
    internal static string Label(OutlineEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new string(' ', IndentPerLevel * Math.Max(0, entry.Level - 1)) + entry.Text;
    }

    /// <summary>
    /// The index of the heading the list starts on: the section the caret is in, which is the last heading whose start is at or before the
    /// caret. The first heading when the caret is before all of them. Returns -1 when there are no headings.
    /// </summary>
    /// <param name="entries">The headings, in document order.</param>
    /// <param name="caret">The caret's offset in the document.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <c>null</c>.</exception>
    internal static int InitialIndex(IReadOnlyList<OutlineEntry> entries, int caret)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            return -1;
        }

        int index = 0;
        for (int i = 1; i < entries.Count && entries[i].Start <= caret; i++)
        {
            index = i;
        }

        return index;
    }
}
