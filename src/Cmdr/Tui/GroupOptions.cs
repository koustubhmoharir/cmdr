using Cmdr.Core.Session;

namespace Cmdr.Tui;

/// <summary>One choice of a group dropdown: the label shown, and the group it stands for (null for top level).</summary>
internal sealed record GroupOption(string Label, GroupEntry? Group);

/// <summary>
/// The choices of the group dropdowns, in depth-first order. Pure; no UI types.
/// </summary>
/// <remarks>
/// The dropdown reports its choice as text, so labels are made unique: a repeated label gets " (2)", " (3)", … This only
/// happens when a name contains the separator or a group is called "(top level)".
/// </remarks>
internal static class GroupOptions
{
    public const string TopLevelLabel = "(top level)";

    /// <summary>Between the names of a group path in a label.</summary>
    public const string Separator = " › ";

    /// <summary>
    /// The parents a group can be given: top level first, then every group by its path, excluding <paramref name="excluded"/>
    /// and the groups below it (they would make a cycle). <paramref name="allGroups"/> is depth-first, as <c>Session.AllGroups</c> is.
    /// </summary>
    public static IReadOnlyList<GroupOption> ParentChoices(IEnumerable<GroupEntry> allGroups, GroupEntry? excluded)
    {
        ArgumentNullException.ThrowIfNull(allGroups);

        var options = new List<GroupOption> { new(TopLevelLabel, null) };
        foreach (GroupEntry group in allGroups)
        {
            if (excluded is null || !GroupTree.IsSelfOrDescendant(group, excluded))
            {
                options.Add(new GroupOption(GroupTree.PathText(group, Separator), group));
            }
        }

        return Unique(options);
    }

    /// <summary>The groups a command can be put in: every group by its path, depth-first. No top-level choice.</summary>
    public static IReadOnlyList<GroupOption> TargetChoices(IEnumerable<GroupEntry> allGroups)
    {
        ArgumentNullException.ThrowIfNull(allGroups);

        return Unique(allGroups.Select(group => new GroupOption(GroupTree.PathText(group, Separator), group)).ToList());
    }

    /// <summary>The index of the option with the label, or -1. Labels are unique, so the index is the option's.</summary>
    public static int IndexOfLabel(IReadOnlyList<GroupOption> options, string? label)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.ToList().FindIndex(option => option.Label == label);
    }

    /// <summary>The labels of the options, in order.</summary>
    public static IReadOnlyList<string> Labels(IEnumerable<GroupOption> options) =>
        options.Select(option => option.Label).ToList();

    private static IReadOnlyList<GroupOption> Unique(List<GroupOption> options)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<GroupOption>(options.Count);
        foreach (GroupOption option in options)
        {
            string label = option.Label;
            for (int n = 2; !used.Add(label); n++)
            {
                label = option.Label + " (" + n + ")";
            }

            result.Add(option with { Label = label });
        }

        return result;
    }
}
