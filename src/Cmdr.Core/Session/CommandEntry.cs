using Cmdr.Core.Config;
using Cmdr.Core.Processes;

namespace Cmdr.Core.Session;

/// <summary>A command in the session: its settings, its process supervisor, and its place in a sequence.</summary>
public sealed class CommandEntry
{
    private bool _isQueued;

    internal CommandEntry(GroupEntry group, CommandSpec spec, ManagedProcess process)
    {
        Group = group;
        Spec = spec;
        Process = process;
    }

    /// <summary>The group this command belongs to.</summary>
    public GroupEntry Group { get; internal set; }

    /// <summary>The command definition. The session stores its own copy; changes apply on next start.</summary>
    public CommandSpec Spec { get; internal set; }

    /// <summary>The process supervisor for this command. Owned by the session.</summary>
    public ManagedProcess Process { get; }

    /// <summary>True while the command waits in a sequence to be started. Safe to read from any thread.</summary>
    public bool IsQueued
    {
        get => Volatile.Read(ref _isQueued);
        internal set => Volatile.Write(ref _isQueued, value);
    }

    /// <summary>True while the leader is running or stopping, or while leftover processes from a run remain.</summary>
    public bool IsActive
    {
        get
        {
            ProcessSnapshot snapshot = Process.Snapshot;
            return snapshot.State is ProcessState.Running or ProcessState.Stopping || snapshot.LeftoverCount > 0;
        }
    }

    /// <summary>True once the command has been removed from the session (its process is disposed).</summary>
    internal bool IsRemoved { get; set; }

    /// <summary>The subscription to <see cref="ManagedProcess.Changed"/>, kept so it can be removed.</summary>
    internal Action<ManagedProcess>? ChangedHandler { get; set; }
}
