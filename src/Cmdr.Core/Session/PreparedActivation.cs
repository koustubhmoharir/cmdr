using Shared.Processes;

namespace Cmdr.Core.Session;

/// <summary>
/// What Enter on command rows or on a group will do, decided when it is pressed: either kill the commands that are stopping or
/// have leftovers, or start the commands that can start then. Made by <see cref="Session.PrepareActivate(IReadOnlyList{CommandEntry})"/>
/// and <see cref="Session.PrepareActivate(GroupEntry)"/>, and carried out by <see cref="Session.Activate(PreparedActivation, IReadOnlyDictionary{string, string}?, ScriptValues?)"/>.
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
    /// <param name="refused">True when the start would be refused (activating it only reports why).</param>
    internal PreparedActivation(
        Session owner,
        GroupEntry? group,
        bool isGroup,
        IReadOnlyList<CommandEntry> targets,
        IReadOnlyList<CommandEntry> killable,
        IReadOnlyList<CommandEntry> startable,
        IReadOnlyList<InputRequest> requests,
        bool refused = false)
    {
        Refused = refused;
        Owner = owner;
        Group = group;
        IsGroup = isGroup;
        Targets = targets;
        Killable = killable;
        Startable = startable;
        Requests = requests;
    }

    /// <summary>
    /// True when the start would be refused when the activation was prepared (for example a sequence of the group is running): activating
    /// it reports why and starts nothing, so no input or environment script is needed for it.
    /// </summary>
    public bool Refused { get; }

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

    /// <summary>
    /// The commands that could start when the activation was prepared. Empty when the decision is to kill, or when nothing can start.
    /// Unless <see cref="Refused"/>, the commands to run the environment scripts for before activating (see <see cref="Session.PrepareEnvAsync"/>).
    /// </summary>
    public IReadOnlyList<CommandEntry> Startable { get; }
}
