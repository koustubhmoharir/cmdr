using Cmdr.Core.Config;

namespace Cmdr.Core.Session;

/// <summary>A group in the session: its settings, its commands in file order, and whether its sequence is running.</summary>
public sealed class GroupEntry
{
    private readonly List<CommandEntry> _commands = new();
    private SequenceRun? _activeSequence;

    internal GroupEntry(string name, string? description, GroupMode mode)
    {
        Name = name;
        Description = description;
        Mode = mode;
    }

    /// <summary>Display name.</summary>
    public string Name { get; internal set; }

    /// <summary>Optional description shown in the UI.</summary>
    public string? Description { get; internal set; }

    /// <summary>How the commands are started.</summary>
    public GroupMode Mode { get; internal set; }

    /// <summary>The group's working directory setting as written in the config (null when not set). Kept so saving does not lose it.</summary>
    internal string? Cwd { get; set; }

    /// <summary>The group's environment setting as written in the config (null when not set). Kept so saving does not lose it.</summary>
    internal Dictionary<string, string?>? Env { get; set; }

    /// <summary>A group definition with this group's settings; its items are not included.</summary>
    internal GroupSpec ToSpec() => new()
    {
        Name = Name,
        Description = Description,
        Mode = Mode,
        Cwd = Cwd,
        Env = Env is null ? null : new Dictionary<string, string?>(Env),
    };

    /// <summary>Commands in file order.</summary>
    public IReadOnlyList<CommandEntry> Commands => _commands;

    /// <summary>True while a sequence of this group is queued or running. Safe to read from any thread.</summary>
    public bool IsSequenceRunning => ActiveSequence is not null;

    /// <summary>The sequence currently queued or running in this group, if any. Read and written under the session lock.</summary>
    internal SequenceRun? ActiveSequence
    {
        get => Volatile.Read(ref _activeSequence);
        set => Volatile.Write(ref _activeSequence, value);
    }

    /// <summary>The mutable command list. Only the session modifies it, under its edit rules.</summary>
    internal List<CommandEntry> MutableCommands => _commands;
}
