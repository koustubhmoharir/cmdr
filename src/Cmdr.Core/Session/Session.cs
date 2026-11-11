using Cmdr.Core.Config;
using Cmdr.Core.Processes;

namespace Cmdr.Core.Session;

/// <summary>
/// The UI-independent state of a cmdr session: the loaded configuration, one <see cref="CommandEntry"/> per command with
/// its process supervisor, editing with dirty tracking, and process actions (see the partial files for those).
/// </summary>
/// <remarks>
/// Editing and process actions are called from the UI thread. Sequences run on background tasks and
/// <see cref="ManagedProcess"/> events arrive on any thread, so <see cref="EntryChanged"/> may be raised from any thread.
/// Handlers that touch UI state must marshal to the UI thread.
/// </remarks>
public sealed partial class Session : IDisposable
{
    private readonly object _sync = new();
    private readonly List<GroupEntry> _groups = new();
    private readonly int _processBufferLines;
    private bool _isDirty;
    private volatile bool _disposed;

    private Session(string configPath, CmdrConfig config)
    {
        ConfigPath = configPath;
        ConfigDir = Path.GetDirectoryName(configPath)!;
        BufferLines = config.BufferLines;
        LogsDirSetting = config.LogsDir;
        LogsDir = ConfigPaths.ResolveLogsDir(config, ConfigDir);
        LoadErrors = config.Validate();

        // An invalid buffer size is reported through LoadErrors; the processes still need a usable buffer.
        _processBufferLines = Math.Max(1, config.BufferLines);

        foreach (GroupSpec spec in config.Groups)
        {
            var group = new GroupEntry(spec.Name, spec.Description, spec.Mode);
            _groups.Add(group);
            foreach (CommandSpec command in spec.Commands)
            {
                AttachCommand(group, command.Clone());
            }
        }
    }

    /// <summary>Raised when the state of a command changes (process state, output, log, queue). May be raised from any thread.</summary>
    public event Action<CommandEntry>? EntryChanged;

    /// <summary>Raised after any edit operation and when <see cref="IsDirty"/> changes. Raised on the thread that made the change.</summary>
    public event Action? StructureChanged;

    /// <summary>Full path of the configuration file.</summary>
    public string ConfigPath { get; }

    /// <summary>Directory of the configuration file; relative paths resolve against it.</summary>
    public string ConfigDir { get; }

    /// <summary>Maximum number of output lines kept per command.</summary>
    public int BufferLines { get; }

    /// <summary>The <c>logsDir</c> value as written in the config file (null when not set).</summary>
    public string? LogsDirSetting { get; }

    /// <summary>Resolved directory for log files.</summary>
    public string LogsDir { get; }

    /// <summary>Groups in file order. The list is owned by the session and changes with edits.</summary>
    public IReadOnlyList<GroupEntry> Groups => _groups;

    /// <summary>True when there are unsaved changes.</summary>
    public bool IsDirty => _isDirty;

    /// <summary>Validation errors of the configuration as loaded. Empty when it is valid.</summary>
    public IReadOnlyList<string> LoadErrors { get; }

    /// <summary>Loads the configuration file. A missing file yields an empty session.</summary>
    /// <exception cref="ConfigException">The file is malformed.</exception>
    public static Session Load(string configPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(configPath);

        string fullPath = Path.GetFullPath(configPath);
        CmdrConfig config = ConfigStore.Load(fullPath);
        return new Session(fullPath, config);
    }

    /// <summary>Adds a group at the end.</summary>
    public GroupEntry AddGroup(string name, string? description, GroupMode mode)
    {
        ArgumentNullException.ThrowIfNull(name);

        var group = new GroupEntry(name, description, mode);
        _groups.Add(group);
        MarkDirty();
        return group;
    }

    /// <summary>Changes the settings of a group.</summary>
    /// <exception cref="InvalidOperationException">The mode changes while the group's sequence is running.</exception>
    public void UpdateGroup(GroupEntry group, string name, string? description, GroupMode mode)
    {
        EnsureOwned(group);
        ArgumentNullException.ThrowIfNull(name);

        if (mode != group.Mode && group.IsSequenceRunning)
        {
            throw new InvalidOperationException($"Cannot change the mode of group '{group.Name}' while its sequence is running.");
        }

        group.Name = name;
        group.Description = description;
        group.Mode = mode;
        MarkDirty();
    }

    /// <summary>Removes a group and disposes its processes.</summary>
    /// <exception cref="InvalidOperationException">A command of the group is active, or its sequence is running.</exception>
    public void RemoveGroup(GroupEntry group)
    {
        EnsureOwned(group);

        if (group.IsSequenceRunning)
        {
            throw new InvalidOperationException($"Cannot remove group '{group.Name}' while its sequence is running.");
        }

        int active = group.Commands.Count(c => c.IsActive);
        if (active > 0)
        {
            throw new InvalidOperationException($"Cannot remove group '{group.Name}': {active} command(s) are running or have leftover processes.");
        }

        foreach (CommandEntry entry in group.MutableCommands)
        {
            DetachCommand(entry);
        }

        group.MutableCommands.Clear();
        _groups.Remove(group);
        MarkDirty();
    }

    /// <summary>Adds a command at the end of a group. The spec is copied.</summary>
    public CommandEntry AddCommand(GroupEntry group, CommandSpec spec)
    {
        EnsureOwned(group);
        ArgumentNullException.ThrowIfNull(spec);

        CommandEntry entry = AttachCommand(group, spec.Clone());
        MarkDirty();
        return entry;
    }

    /// <summary>
    /// Replaces the spec of a command (the spec is copied). If <paramref name="target"/> differs from the current group,
    /// the command moves to the end of it. A running process is not affected; changes apply on next start.
    /// </summary>
    /// <exception cref="InvalidOperationException">The command is queued or part of a running sequence, and would move.</exception>
    public void UpdateCommand(CommandEntry entry, CommandSpec spec, GroupEntry target)
    {
        EnsureOwned(entry);
        EnsureOwned(target);
        ArgumentNullException.ThrowIfNull(spec);

        bool moving = !ReferenceEquals(target, entry.Group);
        if (moving && IsInSequence(entry))
        {
            throw new InvalidOperationException($"Cannot move '{entry.Spec.Name}' while it is queued or part of a running sequence.");
        }

        entry.Spec = spec.Clone();

        if (moving)
        {
            entry.Group.MutableCommands.Remove(entry);
            target.MutableCommands.Add(entry);
            entry.Group = target;
        }

        MarkDirty();
    }

    /// <summary>Removes a command and disposes its process.</summary>
    /// <exception cref="InvalidOperationException">The command is active, queued, or part of a running sequence.</exception>
    public void RemoveCommand(CommandEntry entry)
    {
        EnsureOwned(entry);

        if (entry.IsActive || IsInSequence(entry))
        {
            throw new InvalidOperationException($"Cannot remove '{entry.Spec.Name}' while it is running, queued or leftover processes remain.");
        }

        entry.Group.MutableCommands.Remove(entry);
        DetachCommand(entry);
        MarkDirty();
    }

    /// <summary>
    /// Moves a command within its group by <paramref name="delta"/> positions.
    /// </summary>
    /// <returns>False, with no change, at the boundary, when <paramref name="delta"/> is zero, or while the group's sequence is running.</returns>
    public bool MoveCommand(CommandEntry entry, int delta)
    {
        EnsureOwned(entry);

        List<CommandEntry> commands = entry.Group.MutableCommands;
        int index = commands.IndexOf(entry);
        int target = index + delta;

        if (delta == 0 || entry.Group.IsSequenceRunning || target < 0 || target >= commands.Count)
        {
            return false;
        }

        (commands[index], commands[target]) = (commands[target], commands[index]);
        MarkDirty();
        return true;
    }

    /// <summary>Builds a configuration from the current state. The result shares no objects with the session.</summary>
    public CmdrConfig BuildConfig()
    {
        var config = new CmdrConfig { BufferLines = BufferLines, LogsDir = LogsDirSetting };
        foreach (GroupEntry group in _groups)
        {
            config.Groups.Add(new GroupSpec
            {
                Name = group.Name,
                Description = group.Description,
                Mode = group.Mode,
                Commands = group.Commands.Select(c => c.Spec.Clone()).ToList(),
            });
        }

        return config;
    }

    /// <summary>Validation errors of the current state. Empty when it can be saved.</summary>
    public IReadOnlyList<string> Validate() => BuildConfig().Validate();

    /// <summary>
    /// Saves the configuration to <see cref="ConfigPath"/> and clears the dirty flag.
    /// </summary>
    /// <exception cref="InvalidOperationException">The current state has validation errors; the errors are the message, one per line. Nothing is written.</exception>
    public void Save()
    {
        CmdrConfig config = BuildConfig();
        IReadOnlyList<string> errors = config.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        ConfigStore.Save(config, ConfigPath);
        _isDirty = false;
        RaiseStructureChanged();
    }

    /// <summary>All commands, in file order, across all groups.</summary>
    public IReadOnlyList<CommandEntry> AllEntries => _groups.SelectMany(g => g.Commands).ToList();

    /// <summary>The rows of the command list: each group header followed by its commands, in file order.</summary>
    public IReadOnlyList<ListRow> BuildRows()
    {
        var rows = new List<ListRow>();
        foreach (GroupEntry group in _groups)
        {
            rows.Add(new GroupRow(group));
            foreach (CommandEntry entry in group.Commands)
            {
                rows.Add(new CommandRow(entry));
            }
        }

        return rows;
    }

    /// <summary>Releases the session: cancels sequences, unsubscribes and disposes all processes. Does not kill processes on Linux.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        foreach (GroupEntry group in _groups)
        {
            group.ActiveSequence?.Cancel();
        }

        foreach (GroupEntry group in _groups)
        {
            foreach (CommandEntry entry in group.Commands)
            {
                DetachCommand(entry);
            }
        }
    }

    internal void RaiseEntryChanged(CommandEntry entry)
    {
        if (!_disposed)
        {
            EntryChanged?.Invoke(entry);
        }
    }

    private CommandEntry AttachCommand(GroupEntry group, CommandSpec spec)
    {
        var process = new ManagedProcess(_processBufferLines);
        var entry = new CommandEntry(group, spec, process);
        entry.ChangedHandler = _ => RaiseEntryChanged(entry);
        process.Changed += entry.ChangedHandler;
        group.MutableCommands.Add(entry);
        return entry;
    }

    private void DetachCommand(CommandEntry entry)
    {
        entry.IsRemoved = true;
        if (entry.ChangedHandler is { } handler)
        {
            entry.Process.Changed -= handler;
            entry.ChangedHandler = null;
        }

        entry.Process.Dispose();
    }

    private void EnsureOwned(GroupEntry group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (!_groups.Contains(group))
        {
            throw new ArgumentException("The group does not belong to this session.", nameof(group));
        }
    }

    private void EnsureOwned(CommandEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsRemoved || !_groups.Contains(entry.Group))
        {
            throw new ArgumentException("The command does not belong to this session.", nameof(entry));
        }
    }

    private void MarkDirty()
    {
        _isDirty = true;
        RaiseStructureChanged();
    }

    private void RaiseStructureChanged()
    {
        if (!_disposed)
        {
            StructureChanged?.Invoke();
        }
    }

    private static bool IsInSequence(CommandEntry entry) =>
        entry.IsQueued || entry.Group.ActiveSequence?.Contains(entry) == true;
}
