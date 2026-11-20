using Cmdr.Core.Session;

namespace Cmdr.Tui;

/// <summary>
/// Collapse rules of the nested command list. A collapsed group hides every row below it, at any depth. Pure; no UI types.
/// </summary>
internal static class TreeRows
{
    /// <summary>The item of a row: its group, or its command.</summary>
    public static ItemEntry ItemOf(ListRow row) => row switch
    {
        GroupRow group => group.Group,
        CommandRow command => command.Entry,
        _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown row type."),
    };

    /// <summary>
    /// The header to focus instead of <paramref name="item"/> when it is hidden: the outermost collapsed ancestor, which is the
    /// nearest ancestor header that is shown. Null when the item itself is shown.
    /// </summary>
    public static GroupEntry? FocusHeaderFor(ItemEntry item, IReadOnlySet<GroupEntry> collapsed)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(collapsed);

        GroupEntry? outermost = null;
        for (GroupEntry? ancestor = item.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (collapsed.Contains(ancestor))
            {
                outermost = ancestor;
            }
        }

        return outermost;
    }

    /// <summary>
    /// The groups that start collapsed when the session is loaded: those with <see cref="GroupEntry.Expanded"/> false, at any depth.
    /// Called once at load; later changes of the setting do not affect the list.
    /// </summary>
    public static IReadOnlyList<GroupEntry> InitiallyCollapsed(IEnumerable<GroupEntry> allGroups)
    {
        ArgumentNullException.ThrowIfNull(allGroups);

        return allGroups.Where(group => !group.Expanded).ToList();
    }

    /// <summary>Whether the item is hidden by a collapsed ancestor.</summary>
    public static bool IsHidden(ItemEntry item, IReadOnlySet<GroupEntry> collapsed) => FocusHeaderFor(item, collapsed) is not null;

    /// <summary>The rows that are shown: all rows except those below a collapsed group. Keeps the order.</summary>
    public static IReadOnlyList<ListRow> Visible(IReadOnlyList<ListRow> all, IReadOnlySet<GroupEntry> collapsed)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(collapsed);

        return all.Where(row => !IsHidden(ItemOf(row), collapsed)).ToList();
    }

    /// <summary>
    /// The parent offered by default for a new group: the parent of the focused header's group, or of the focused command's group.
    /// Null (top level) when nothing is focused.
    /// </summary>
    public static GroupEntry? DefaultParentFor(ListRow? focused) => focused switch
    {
        GroupRow group => group.Group.Parent,
        CommandRow command => command.Entry.Group.Parent,
        _ => null,
    };
}
