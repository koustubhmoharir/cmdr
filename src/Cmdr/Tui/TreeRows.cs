using Cmdr.Core.Session;

namespace Cmdr.Tui;

/// <summary>
/// Collapse rules of the nested command list. A collapsed file or group hides every row below it, at any depth. Files and
/// groups share one set of collapsed keys: a <see cref="ConfigSource"/> for a file, a <see cref="GroupEntry"/> for a group (see
/// <see cref="KeyOf"/>). Pure; no UI types.
/// </summary>
internal static class TreeRows
{
    /// <summary>
    /// The depth of a group's row in the list. The file rows are at depth 0, so a top-level group is at depth 1: one level below
    /// its file. The depth of the group in the model (<see cref="GroupEntry.Depth"/>) does not count the file.
    /// </summary>
    public static int ListDepth(GroupEntry group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return group.Depth + 1;
    }

    /// <summary>The item of a row: its group, or its command. Not for a file row, which is not an item.</summary>
    public static ItemEntry ItemOf(ListRow row) => row switch
    {
        GroupRow group => group.Group,
        CommandRow command => command.Entry,
        _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Not an item row."),
    };

    /// <summary>
    /// The key of a row, which identifies it in the list: the file of a file row, the group of a group row, the command of a
    /// command row. Rows are rebuilt on every structure change, so the key is compared, not the row.
    /// </summary>
    public static object KeyOf(ListRow row) => row switch
    {
        FileRow file => file.Source,
        GroupRow group => group.Group,
        CommandRow command => command.Entry,
        _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown row type."),
    };

    /// <summary>The text a row is searched by: the display path of a file row, the name of any other row.</summary>
    public static string SearchTextOf(ListRow row) => row is FileRow file ? file.Source.DisplayPath : ItemOf(row).Name;

    /// <summary>
    /// The header to focus instead of <paramref name="item"/> when it is hidden: the outermost collapsed header above it, which is
    /// the file when the file is collapsed, otherwise the nearest collapsed ancestor group that is outermost. Null when the item
    /// itself is shown.
    /// </summary>
    public static object? FocusHeaderFor(ItemEntry item, IReadOnlySet<object> collapsed)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(collapsed);

        // A collapsed file hides everything in it, so it is the outermost header of any of its items.
        if (collapsed.Contains(item.Source))
        {
            return item.Source;
        }

        object? outermost = null;
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
    /// Called once at load; later changes of the setting do not affect the list. Files never start collapsed.
    /// </summary>
    public static IReadOnlyList<GroupEntry> InitiallyCollapsed(IEnumerable<GroupEntry> allGroups)
    {
        ArgumentNullException.ThrowIfNull(allGroups);

        return allGroups.Where(group => !group.Expanded).ToList();
    }

    /// <summary>Whether the item is hidden by a collapsed file or ancestor.</summary>
    public static bool IsHidden(ItemEntry item, IReadOnlySet<object> collapsed) => FocusHeaderFor(item, collapsed) is not null;

    /// <summary>
    /// The rows that are shown: all rows except those below a collapsed file or group. File rows are always shown. Keeps the order.
    /// </summary>
    public static IReadOnlyList<ListRow> Visible(IReadOnlyList<ListRow> all, IReadOnlySet<object> collapsed)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(collapsed);

        return all.Where(row => row is FileRow || !IsHidden(ItemOf(row), collapsed)).ToList();
    }

    /// <summary>
    /// The parent offered by default for a new group: the parent of the focused header's group, or of the focused command's group.
    /// Null (top level) when nothing is focused, or when a file row is focused.
    /// </summary>
    public static GroupEntry? DefaultParentFor(ListRow? focused) => focused switch
    {
        GroupRow group => group.Group.Parent,
        CommandRow command => command.Entry.Group.Parent,
        _ => null,
    };
}
