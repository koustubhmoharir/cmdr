namespace Cmdr.Core.Session;

/// <summary>
/// Multi-selection (marks) of the command list. Marks are confined to one group. Pure state rules without UI types;
/// used from the UI thread only.
/// </summary>
public sealed class Selection
{
    private readonly HashSet<CommandEntry> _marked = new();

    /// <summary>The group the marks belong to, or null when nothing is marked.</summary>
    public GroupEntry? Group { get; private set; }

    /// <summary>The marked commands, in file order.</summary>
    public IReadOnlyCollection<CommandEntry> Marked =>
        Group is null ? Array.Empty<CommandEntry>() : Group.Commands.Where(_marked.Contains).ToList();

    /// <summary>Whether the command is marked.</summary>
    public bool IsMarked(CommandEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _marked.Contains(entry);
    }

    /// <summary>
    /// Toggles the mark of a command. Marking a command in another group first clears the previous marks.
    /// </summary>
    public void Toggle(CommandEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (Group != entry.Group)
        {
            _marked.Clear();
            Group = entry.Group;
        }

        if (!_marked.Remove(entry))
        {
            _marked.Add(entry);
        }

        ResetGroupIfEmpty();
    }

    /// <summary>
    /// Toggles selection of all commands of a group: clears when all of them are already marked, otherwise marks them all.
    /// </summary>
    public void ToggleAll(GroupEntry group)
    {
        ArgumentNullException.ThrowIfNull(group);

        if (Group == group && group.Commands.All(_marked.Contains))
        {
            Clear();
            return;
        }

        _marked.Clear();
        foreach (CommandEntry entry in group.Commands)
        {
            _marked.Add(entry);
        }

        Group = group;
        ResetGroupIfEmpty();
    }

    /// <summary>Removes all marks.</summary>
    public void Clear()
    {
        _marked.Clear();
        Group = null;
    }

    /// <summary>
    /// Drops marks of commands that were removed or moved out of the marked group, and clears the group when nothing remains marked.
    /// Call after edits.
    /// </summary>
    public void Prune()
    {
        if (Group is null)
        {
            _marked.Clear();
            return;
        }

        _marked.RemoveWhere(entry => entry.IsRemoved || entry.Group != Group);
        ResetGroupIfEmpty();
    }

    /// <summary>
    /// What an action applies to. A group header means the whole group. A command means the marked commands
    /// when the focused command is in the marked group and something is marked; otherwise just the focused command.
    /// </summary>
    public ActionTarget Targets(ListRow focused)
    {
        ArgumentNullException.ThrowIfNull(focused);

        switch (focused)
        {
            case GroupRow header:
                return new GroupTarget(header.Group);

            case CommandRow row when Group is not null && row.Entry.Group == Group && _marked.Count > 0:
                return new CommandsTarget(Marked.ToList());

            case CommandRow row:
                return new CommandsTarget(new[] { row.Entry });

            default:
                throw new ArgumentOutOfRangeException(nameof(focused), focused, "Unknown row type.");
        }
    }

    private void ResetGroupIfEmpty()
    {
        if (_marked.Count == 0)
        {
            Group = null;
        }
    }
}

/// <summary>The target of an action chosen from the list: a whole group, or a set of commands.</summary>
public abstract record ActionTarget;

/// <summary>An action on a whole group (everything below it).</summary>
/// <param name="Group">The group.</param>
public sealed record GroupTarget(GroupEntry Group) : ActionTarget;

/// <summary>An action on a set of sibling commands.</summary>
/// <param name="Commands">The commands, in file order.</param>
public sealed record CommandsTarget(IReadOnlyList<CommandEntry> Commands) : ActionTarget;
