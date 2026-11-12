using Cmdr.Core.Config;
using Cmdr.Core.Session;

namespace Cmdr.Tui;

/// <summary>Paths, ancestor chains and counts of the group tree. Pure; no UI types.</summary>
internal static class GroupTree
{
    /// <summary>The group and its ancestors, outermost first. Empty for null.</summary>
    public static IReadOnlyList<GroupEntry> ChainTo(GroupEntry? group)
    {
        var chain = new List<GroupEntry>();
        for (GroupEntry? current = group; current is not null; current = current.Parent)
        {
            chain.Insert(0, current);
        }

        return chain;
    }

    /// <summary>
    /// The definitions of the group and its ancestors, outermost first, for <see cref="Cmdr.Core.Config.ConfigPaths"/> and
    /// <see cref="Cmdr.Core.Processes.LaunchSpec.From"/>. The name, working directory and environment are filled in.
    /// </summary>
    public static IReadOnlyList<GroupSpec> SpecChain(GroupEntry? group) =>
        ChainTo(group).Select(g => new GroupSpec
        {
            Name = g.Name,
            Cwd = g.Cwd,
            Env = g.Env is null ? null : new Dictionary<string, string?>(g.Env),
        }).ToList();

    /// <summary>
    /// The groups that can be edited: those of the opened file. Base groups are read-only, so they are never a parent, a target
    /// or a sibling that a change would be made in. Keeps the order.
    /// </summary>
    public static IReadOnlyList<GroupEntry> Editable(IEnumerable<GroupEntry> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        return groups.Where(group => !group.IsReadOnly).ToList();
    }

    /// <summary>The names from the outermost group down to the item, joined with <paramref name="separator"/>.</summary>
    public static string PathText(ItemEntry item, string separator)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(separator);

        var names = new List<string> { item.Name };
        for (GroupEntry? ancestor = item.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            names.Insert(0, ancestor.Name);
        }

        return string.Join(separator, names);
    }

    /// <summary>Whether <paramref name="candidate"/> is <paramref name="root"/> or is nested below it.</summary>
    public static bool IsSelfOrDescendant(GroupEntry candidate, GroupEntry root)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(root);

        for (GroupEntry? current = candidate; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The number of groups below the group, at any depth.</summary>
    public static int DescendantGroupCount(GroupEntry group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return group.Groups.Sum(child => 1 + DescendantGroupCount(child));
    }

    /// <summary>
    /// The confirmation for deleting a group: <c>Delete group 'x' with 2 subgroup(s) and 5 command(s)?</c>. The subgroup
    /// part is left out when there are none.
    /// </summary>
    public static string DeleteQuestion(GroupEntry group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var parts = new List<string>(2);
        int subgroups = DescendantGroupCount(group);
        if (subgroups > 0)
        {
            parts.Add($"{subgroups} subgroup(s)");
        }

        parts.Add($"{group.AllCommands.Count} command(s)");
        return $"Delete group '{group.Name}' with {string.Join(" and ", parts)}?";
    }
}
