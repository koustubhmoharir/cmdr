namespace Cmdr.Core.Session;

/// <summary>
/// One command waited for by one pending restart, captured when the restart was found. It identifies the run the restart is
/// waiting for, so acting on it later never affects a newer run of the command: once that run has ended and the restart has
/// started the replacement, <see cref="Session.ForceKillForPendingRestart"/> skips the handle.
/// </summary>
public sealed class PendingRestart
{
    internal PendingRestart(CommandEntry command, RestartOperation operation, int runId)
    {
        Command = command;
        Operation = operation;
        RunId = runId;
    }

    /// <summary>The command the pending restart waits for.</summary>
    public CommandEntry Command { get; }

    /// <summary>The pending restart this handle belongs to.</summary>
    internal RestartOperation Operation { get; }

    /// <summary>The run of <see cref="Command"/>'s process that was current when the restart was found.</summary>
    internal int RunId { get; }
}
