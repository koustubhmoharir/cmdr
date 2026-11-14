using Cmdr.Core.Config;
using Cmdr.Core.Processes;

namespace Cmdr.Core.Session;

/// <summary>A command in the session: its settings, its process supervisor, and its place in a sequence.</summary>
public sealed class CommandEntry : ItemEntry
{
    private bool _isQueued;

    internal CommandEntry(GroupEntry group, CommandSpec spec, ManagedProcess process)
    {
        Parent = group;
        Spec = spec;
        Process = process;
    }

    /// <summary>The group this command belongs to (its direct parent).</summary>
    public GroupEntry Group => Parent ?? throw new InvalidOperationException("A command always belongs to a group.");

    /// <inheritdoc/>
    public override string Name => Spec.Name;

    /// <summary>The file of the group this command belongs to. A command is always in the same file as its group.</summary>
    public override ConfigSource Source => Group.Source;

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
            return snapshot.State is ProcessState.Running or ProcessState.Stopping || snapshot.HasLeftovers;
        }
    }

    /// <summary>The subscription to <see cref="ManagedProcess.Changed"/>, kept so it can be removed.</summary>
    internal Action<ManagedProcess>? ChangedHandler { get; set; }
}
