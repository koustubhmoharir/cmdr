namespace Cmdr.Core.Session;

/// <summary>
/// One planned step of a run: a command, or a group with the steps planned inside it. Plans are built when a run is
/// created, so later edits to the tree do not change what the run does.
/// </summary>
internal sealed class PlanNode
{
    private static readonly IReadOnlyList<PlanNode> NoChildren = Array.Empty<PlanNode>();

    /// <summary>A command step.</summary>
    public PlanNode(CommandEntry command)
    {
        Command = command;
        Children = NoChildren;
    }

    /// <summary>A group step with its planned children.</summary>
    public PlanNode(GroupEntry group, IReadOnlyList<PlanNode> children)
    {
        Group = group;
        Children = children;
    }

    /// <summary>The command of a command step; null for a group step.</summary>
    public CommandEntry? Command { get; }

    /// <summary>The group of a group step; null for a command step.</summary>
    public GroupEntry? Group { get; }

    /// <summary>The planned children of a group step, in order. Empty for a command step.</summary>
    public IReadOnlyList<PlanNode> Children { get; }

    /// <summary>True if the command is this step or planned inside it.</summary>
    public bool ContainsCommand(CommandEntry entry) =>
        Command == entry || Children.Any(child => child.ContainsCommand(entry));

    /// <summary>True if the group is this step or planned inside it.</summary>
    public bool ContainsGroup(GroupEntry group) =>
        Group == group || Children.Any(child => child.ContainsGroup(group));

    /// <summary>Adds every command planned at or below this step to the list.</summary>
    public void CollectCommands(List<CommandEntry> into)
    {
        if (Command is not null)
        {
            into.Add(Command);
        }

        foreach (PlanNode child in Children)
        {
            child.CollectCommands(into);
        }
    }
}
