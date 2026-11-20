namespace Cmdr.Core.Session;

/// <summary>
/// A restart that is registered with its session but has not started anything yet. Made by
/// <see cref="Session.PrepareRestart(IReadOnlyList{CommandEntry}, IReadOnlyDictionary{string, string}?, ScriptValues?, CancellationToken)"/> and
/// <see cref="Session.PrepareRestart(GroupEntry, IReadOnlyDictionary{string, string}?, ScriptValues?, CancellationToken)"/>, and run by
/// <see cref="Session.RunRestartAsync"/>.
/// </summary>
/// <remarks>
/// Must be passed to <see cref="Session.RunRestartAsync"/> exactly once; a second run throws <see cref="InvalidOperationException"/>.
/// From the moment it is prepared, until it is run, Stop, Stop of a group, ForceKill, StopAll, ForceKillAll and Dispose revoke
/// it like any pending restart, so it starts nothing. A prepared restart that is never run stays registered, so it stays
/// revocable, and it is never removed from the session.
/// </remarks>
public sealed class PreparedRestart
{
    private int _claimed;

    /// <param name="owner">The session the restart was prepared in.</param>
    /// <param name="operation">The registered restart, or null when there is nothing to restart.</param>
    /// <param name="targets">The commands of a restart of commands, or null for a restart of a group.</param>
    /// <param name="affected">The active runs that contain the targets when the restart was prepared; empty for a group restart.</param>
    /// <param name="values">The frozen input and script values the restart starts commands with.</param>
    internal PreparedRestart(
        Session owner,
        RestartOperation? operation,
        IReadOnlyList<CommandEntry>? targets,
        IReadOnlyList<SequenceRun> affected,
        LaunchValues values)
    {
        Owner = owner;
        Operation = operation;
        Targets = targets;
        Affected = affected;
        Values = values;
    }

    /// <summary>The session the restart was prepared in.</summary>
    internal Session Owner { get; }

    /// <summary>The registered restart; null when the prepared restart has nothing to do.</summary>
    internal RestartOperation? Operation { get; }

    /// <summary>The commands of a restart of commands; null for a restart of a group.</summary>
    internal IReadOnlyList<CommandEntry>? Targets { get; }

    /// <summary>The active runs that contained the targets when the restart was prepared.</summary>
    internal IReadOnlyList<SequenceRun> Affected { get; }

    /// <summary>The input and script values the restart starts commands with.</summary>
    internal LaunchValues Values { get; }

    /// <summary>Marks the restart as run. Returns false if it already was.</summary>
    internal bool Claim() => Interlocked.Exchange(ref _claimed, 1) == 0;
}
