using Cmdr.Core.Config;

namespace Cmdr.Core.Session;

/// <summary>
/// What Enter on command rows or on a group will do, decided when it is pressed: either kill the commands that are stopping or
/// have leftovers, or start the commands that can start then. Made by <see cref="Session.PrepareActivate(IReadOnlyList{CommandEntry})"/>
/// and <see cref="Session.PrepareActivate(GroupEntry)"/>, and carried out by <see cref="Session.Activate(PreparedActivation, IReadOnlyDictionary{string, string}?)"/>.
/// </summary>
/// <remarks>
/// Belongs to the session it was prepared in. It registers nothing and holds no lock: activating it checks the commands again, so
/// a command that became startable after the decision is not started by it.
/// </remarks>
public sealed class PreparedActivation
{
    /// <param name="owner">The session the activation was prepared in.</param>
    /// <param name="group">The group of the targets, or of the activated group; null when there are no targets.</param>
    /// <param name="isGroup">True for an activation of a group, which starts the group's planned commands.</param>
    /// <param name="targets">The targets of an activation of commands.</param>
    /// <param name="killable">The commands to force-kill. Non-empty only when the decision is to kill.</param>
    /// <param name="startable">The commands that could start when the activation was prepared.</param>
    /// <param name="requests">The inputs the start needs.</param>
    internal PreparedActivation(
        Session owner,
        GroupEntry? group,
        bool isGroup,
        IReadOnlyList<CommandEntry> targets,
        IReadOnlyList<CommandEntry> killable,
        IReadOnlyList<CommandEntry> startable,
        IReadOnlyList<InputRequest> requests)
    {
        Owner = owner;
        Group = group;
        IsGroup = isGroup;
        Targets = targets;
        Killable = killable;
        Startable = startable;
        Requests = requests;
    }

    /// <summary>The inputs the start needs, in first-seen order. Empty when the decision is to kill, to start nothing, or when no input is needed.</summary>
    public IReadOnlyList<InputRequest> Requests { get; }

    /// <summary>The session the activation was prepared in.</summary>
    internal Session Owner { get; }

    /// <summary>The group of the targets, or of the activated group.</summary>
    internal GroupEntry? Group { get; }

    /// <summary>True for an activation of a group.</summary>
    internal bool IsGroup { get; }

    /// <summary>The targets of an activation of commands; empty for a group.</summary>
    internal IReadOnlyList<CommandEntry> Targets { get; }

    /// <summary>The commands to force-kill, when the decision is to kill.</summary>
    internal IReadOnlyList<CommandEntry> Killable { get; }

    /// <summary>The commands that could start when the activation was prepared.</summary>
    internal IReadOnlyList<CommandEntry> Startable { get; }
}
