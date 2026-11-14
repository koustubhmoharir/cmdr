using Cmdr.Core.Config;
using Cmdr.Core.Processes;

namespace Cmdr.Core.Session;

/// <summary>
/// The UI-independent state of a cmdr session: the loaded configuration as a tree of groups and commands, one
/// <see cref="CommandEntry"/> per command with its process supervisor, editing with dirty tracking, and process actions
/// (see the partial files for those).
/// </summary>
/// <remarks>
/// Editing and process actions are called from the UI thread. Sequences run on background tasks and
/// <see cref="ManagedProcess"/> events arrive on any thread, so <see cref="EntryChanged"/> may be raised from any thread.
/// Handlers that touch UI state must marshal to the UI thread.
/// <para>
/// Locking: <c>_sync</c> guards the tree structure: the top-level list, the items of every group, <see cref="ItemEntry.Parent"/>
/// and <see cref="ItemEntry.IsRemoved"/>. Every change to that structure happens while holding it, and so does every traversal
/// that a background thread can run (restarts, sequence runs). Edits validate and mutate under the lock, then raise
/// <see cref="StructureChanged"/> and dispose removed processes after releasing it. Events are never raised with the lock held,
/// and neither are process starts, stops or disposals, file I/O or awaits. Lock order: the session lock first, then a
/// sequence run's lock, then a <see cref="ManagedProcess"/> lock; never the reverse.
/// </para>
/// </remarks>
public sealed partial class Session : IDisposable
{
    private static readonly TimeSpan LogFlushOnDisposeTimeout = TimeSpan.FromSeconds(2);

    private readonly object _sync = new();
    private readonly List<GroupEntry> _groups = new();
    private readonly List<RestartOperation> _restarts = new();
    private readonly string? _base;
    private bool _isDirty;
    private volatile bool _disposed;

    private Session(IReadOnlyList<ConfigFile> chain)
    {
        string openedDir = chain[^1].Dir;
        var sources = chain.Select(file => new ConfigSource(file, openedDir)).ToList();
        Sources = sources;
        MainSource = sources[^1];

        ConfigPath = MainSource.Path;
        ConfigDir = MainSource.Dir;
        BufferLines = MainSource.BufferLines;
        LogsDirSetting = MainSource.LogsDirSetting;
        LogsDir = MainSource.LogsDir;
        _base = chain[^1].Config.Base;
        LoadErrors = ConfigStore.ValidateChain(chain);

        // Base groups come first, then the opened file's groups, in chain order.
        for (int i = 0; i < chain.Count; i++)
        {
            foreach (GroupSpec spec in chain[i].Config.Groups)
            {
                _groups.Add(BuildGroup(spec, sources[i]));
            }
        }
    }

    /// <summary>Raised when the state of a command changes (process state, output, log, queue). May be raised from any thread.</summary>
    public event Action<CommandEntry>? EntryChanged;

    /// <summary>Raised after any edit operation and when <see cref="IsDirty"/> changes. Raised on the thread that made the change.</summary>
    public event Action? StructureChanged;

    /// <summary>Full path of the opened configuration file (the file that is saved).</summary>
    public string ConfigPath { get; }

    /// <summary>Directory of the opened configuration file; relative paths of its items resolve against it.</summary>
    public string ConfigDir { get; }

    /// <summary>Maximum number of output lines kept per command of the opened file.</summary>
    public int BufferLines { get; }

    /// <summary>The <c>logsDir</c> value of the opened file as written in the config file (null when not set).</summary>
    public string? LogsDirSetting { get; }

    /// <summary>Resolved log directory of the opened file.</summary>
    public string LogsDir { get; }

    /// <summary>
    /// Every file of the import chain, base files first and the opened file last. Read-only files (<see cref="ConfigSource.IsBase"/>)
    /// cannot be edited in the session.
    /// </summary>
    public IReadOnlyList<ConfigSource> Sources { get; }

    /// <summary>The source of the opened file: the one that is saved, and the one new top-level groups go to.</summary>
    public ConfigSource MainSource { get; }

    /// <summary>Top-level groups in file order. The list is owned by the session and changes with edits.</summary>
    public IReadOnlyList<GroupEntry> Groups => _groups;

    /// <summary>Every group at any depth, depth-first in file order.</summary>
    public IReadOnlyList<GroupEntry> AllGroups => Walk(_groups).OfType<GroupEntry>().ToList();

    /// <summary>True when there are unsaved changes.</summary>
    public bool IsDirty => _isDirty;

    /// <summary>Validation errors of the configuration as loaded. Empty when it is valid.</summary>
    public IReadOnlyList<string> LoadErrors { get; }

    /// <summary>
    /// Loads the configuration file and the base files it imports (see <see cref="ConfigStore.LoadChain"/>). A missing file
    /// yields an empty session. Problems are reported in <see cref="LoadErrors"/>.
    /// </summary>
    /// <exception cref="ConfigException">A file is malformed, a base file is missing, or the imports form a cycle.</exception>
    public static Session Load(string configPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(configPath);

        return new Session(ConfigStore.LoadChain(configPath));
    }

    /// <summary>
    /// Adds a group at the end of <paramref name="parent"/>'s items, or at the end of the top level when <paramref name="parent"/> is null.
    /// The group belongs to the file of its parent (the opened file at the top level).
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="parent"/> comes from a base file.</exception>
    public GroupEntry AddGroup(GroupEntry? parent, GroupSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(settings.Name);

        GroupEntry group;
        lock (_sync)
        {
            if (parent is not null)
            {
                EnsureOwned(parent);
                EnsureEditable(parent);
            }

            group = NewGroup(settings, parent?.Source ?? MainSource);
            Place(parent, group);
            _isDirty = true;
        }

        RaiseStructureChanged();
        return group;
    }

    /// <summary>
    /// Changes the settings of a group and, when <paramref name="parent"/> differs from its current parent, moves it to the
    /// end of the new parent's items (or of the top level when <paramref name="parent"/> is null).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The group comes from a base file; the group would be moved into a group of a base file; the group would be moved into
    /// itself or one of its descendants; or its mode changes or it moves while the group or anything below it is part of a run.
    /// </exception>
    public void UpdateGroup(GroupEntry group, GroupSettings settings, GroupEntry? parent)
    {
        lock (_sync)
        {
            EnsureOwned(group);
            EnsureEditable(group);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(settings.Name);
            if (parent is not null)
            {
                EnsureOwned(parent);
                EnsureEditable(parent);
            }

            bool reparent = !ReferenceEquals(parent, group.Parent);
            if (reparent)
            {
                for (GroupEntry? ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
                {
                    if (ReferenceEquals(ancestor, group))
                    {
                        throw new InvalidOperationException($"Cannot move group '{group.Name}' into itself or into one of its own groups.");
                    }
                }
            }

            if ((reparent || settings.Mode != group.Mode) && IsBusyGroup(group))
            {
                throw new InvalidOperationException($"Cannot change the mode or move group '{group.Name}' while it or something inside it is part of a run.");
            }

            if (reparent)
            {
                Unplace(group);
                Place(parent, group);
            }

            group.Rename(settings.Name);
            group.Description = settings.Description;
            group.Mode = settings.Mode;
            group.Cwd = settings.Cwd;
            group.Env = CopyEnv(settings.Env);
            group.Expanded = settings.Expanded;
            _isDirty = true;
        }

        RaiseStructureChanged();
    }

    /// <summary>Removes a group with everything below it and disposes their processes.</summary>
    /// <exception cref="InvalidOperationException">
    /// The group comes from a base file; or something below the group is running, has leftover processes, or is part of a run.
    /// </exception>
    public void RemoveGroup(GroupEntry group)
    {
        var removedCommands = new List<CommandEntry>();
        lock (_sync)
        {
            EnsureOwned(group);
            EnsureEditable(group);

            if (IsBusyGroup(group))
            {
                throw new InvalidOperationException($"Cannot remove group '{group.Name}' while it or something inside it is part of a run.");
            }

            List<CommandEntry> commands = group.AllCommands.ToList();
            int active = commands.Count(c => c.IsActive);
            if (active > 0)
            {
                throw new InvalidOperationException($"Cannot remove group '{group.Name}': {active} command(s) are running or have leftover processes.");
            }

            foreach (CommandEntry entry in commands)
            {
                MarkCommandRemoved(entry);
                removedCommands.Add(entry);
            }

            foreach (GroupEntry removed in group.SelfAndDescendantGroups())
            {
                removed.IsRemoved = true;
            }

            Unplace(group);
            _isDirty = true;
        }

        DisposeProcesses(removedCommands);
        RaiseStructureChanged();
    }

    /// <summary>Adds a command at the end of a group. The spec is copied.</summary>
    /// <exception cref="InvalidOperationException">The group comes from a base file.</exception>
    public CommandEntry AddCommand(GroupEntry group, CommandSpec spec)
    {
        CommandEntry entry;
        lock (_sync)
        {
            EnsureOwned(group);
            EnsureEditable(group);
            ArgumentNullException.ThrowIfNull(spec);

            entry = AttachCommand(group, spec.Clone());
            _isDirty = true;
        }

        RaiseStructureChanged();
        return entry;
    }

    /// <summary>
    /// Replaces the spec of a command (the spec is copied). If <paramref name="target"/> differs from the current group,
    /// the command moves to the end of it (any group in the tree). A running process is not affected; changes apply on next start.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The command comes from a base file; the command would move into a group of a base file; or the command is queued or
    /// part of a running sequence, and would move.
    /// </exception>
    public void UpdateCommand(CommandEntry entry, CommandSpec spec, GroupEntry target)
    {
        lock (_sync)
        {
            EnsureOwned(entry);
            EnsureOwned(target);
            EnsureEditable(entry);
            EnsureEditable(target);
            ArgumentNullException.ThrowIfNull(spec);

            bool moving = !ReferenceEquals(target, entry.Group);
            if (moving && IsBusyCommand(entry))
            {
                throw new InvalidOperationException($"Cannot move '{entry.Spec.Name}' while it is queued or part of a running sequence.");
            }

            entry.Spec = spec.Clone();

            if (moving)
            {
                entry.Group.Detach(entry);
                target.Attach(entry);
            }

            _isDirty = true;
        }

        RaiseStructureChanged();
    }

    /// <summary>Removes a command and disposes its process.</summary>
    /// <exception cref="InvalidOperationException">
    /// The command comes from a base file; or the command is active, queued, or part of a running sequence.
    /// </exception>
    public void RemoveCommand(CommandEntry entry)
    {
        lock (_sync)
        {
            EnsureOwned(entry);
            EnsureEditable(entry);

            if (entry.IsActive || IsBusyCommand(entry))
            {
                throw new InvalidOperationException($"Cannot remove '{entry.Spec.Name}' while it is running, queued or leftover processes remain.");
            }

            entry.Group.Detach(entry);
            MarkCommandRemoved(entry);
            _isDirty = true;
        }

        DisposeProcesses(new[] { entry });
        RaiseStructureChanged();
    }

    /// <summary>
    /// Moves an item (a command or a group) among its siblings by <paramref name="delta"/> positions: among the items of its
    /// parent, or among the top-level groups.
    /// </summary>
    /// <returns>
    /// False, with no change, when <paramref name="delta"/> is zero, at the boundary, or when the item or its parent is part of a run.
    /// At the top level, a group of the opened file never moves above the first group of the opened file, so base groups stay first.
    /// </returns>
    /// <exception cref="InvalidOperationException">The item comes from a base file.</exception>
    public bool MoveItem(ItemEntry item, int delta)
    {
        lock (_sync)
        {
            EnsureOwned(item);
            EnsureEditable(item);

            if (delta == 0)
            {
                return false;
            }

            GroupEntry? parent = item.Parent;
            bool busy = item is GroupEntry group ? IsBusyGroup(group) : IsBusyCommand((CommandEntry)item);
            if (busy || (parent is not null && IsBusyGroup(parent)))
            {
                return false;
            }

            if (parent is null)
            {
                int from = _groups.IndexOf((GroupEntry)item);
                int to = from + delta;
                int firstMain = _groups.FindIndex(group => ReferenceEquals(group.Source, MainSource));
                if (to < firstMain || to >= _groups.Count)
                {
                    return false;
                }

                (_groups[from], _groups[to]) = (_groups[to], _groups[from]);
            }
            else
            {
                int from = parent.IndexOf(item);
                int to = from + delta;
                if (to < 0 || to >= parent.Items.Count)
                {
                    return false;
                }

                parent.Swap(from, to);
            }

            _isDirty = true;
        }

        RaiseStructureChanged();
        return true;
    }

    /// <summary>Moves a command within its group by <paramref name="delta"/> positions. See <see cref="MoveItem"/>.</summary>
    public bool MoveCommand(CommandEntry entry, int delta) => MoveItem(entry, delta);

    /// <summary>
    /// Builds the opened file's configuration from the current state: its groups only (base groups are not included), with its
    /// <c>base</c>, <c>bufferLines</c> and <c>logsDir</c>. The result shares no objects with the session.
    /// </summary>
    public CmdrConfig BuildConfig()
    {
        var config = new CmdrConfig { Base = _base, BufferLines = MainSource.BufferLines, LogsDir = LogsDirSetting };
        foreach (GroupEntry group in _groups)
        {
            if (ReferenceEquals(group.Source, MainSource))
            {
                config.Groups.Add(ToSpecTree(group));
            }
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

    /// <summary>All commands at any depth, depth-first in file order.</summary>
    public IReadOnlyList<CommandEntry> AllEntries
    {
        get
        {
            lock (_sync)
            {
                return Walk(_groups).OfType<CommandEntry>().ToList();
            }
        }
    }

    /// <summary>
    /// The rows of the command list: each group header followed by its items, depth-first in file order. Nested groups
    /// are shown as rows too (indentation is a UI concern).
    /// </summary>
    public IReadOnlyList<ListRow> BuildRows()
    {
        var rows = new List<ListRow>();
        foreach (ItemEntry item in Walk(_groups))
        {
            if (item is GroupEntry group)
            {
                rows.Add(new GroupRow(group));
            }
            else
            {
                rows.Add(new CommandRow((CommandEntry)item));
            }
        }

        return rows;
    }

    /// <summary>
    /// Releases the session: revokes pending restarts, cancels sequences, unsubscribes and disposes all processes, then waits up
    /// to two seconds in total for their logs to be written. Does not kill processes on Linux. A restart that was pending
    /// completes with <see cref="OperationCanceledException"/> and starts nothing.
    /// </summary>
    public void Dispose()
    {
        RevokeRestarts(_ => true, force: false);

        List<SequenceRun> runs;
        List<CommandEntry> entries;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            runs = ActiveRuns();
            entries = AllEntries.ToList();
            foreach (CommandEntry entry in entries)
            {
                MarkCommandRemoved(entry);
            }
        }

        foreach (SequenceRun run in runs)
        {
            run.Cancel();
        }

        DisposeProcesses(entries);

        // Logs are written in the background; give them a moment to reach their files before the process ends. A stalled
        // log file must not hold up quitting, so the wait is bounded for all of them together.
        DateTime deadline = DateTime.UtcNow + LogFlushOnDisposeTimeout;
        foreach (CommandEntry entry in entries)
        {
            TimeSpan left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero || !entry.Process.Output.WaitForLogWrites(left))
            {
                break;
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

    private static IEnumerable<ItemEntry> Walk(IEnumerable<ItemEntry> items) => GroupEntry.Walk(items);

    private static GroupEntry NewGroup(GroupSettings settings, ConfigSource source) =>
        new(settings.Name, settings.Description, settings.Mode, source)
        {
            Cwd = settings.Cwd,
            Env = CopyEnv(settings.Env),
            Expanded = settings.Expanded,
        };

    private static IReadOnlyDictionary<string, string?>? CopyEnv(IReadOnlyDictionary<string, string?>? env) =>
        env is null ? null : new Dictionary<string, string?>(env);

    private static GroupSpec ToSpecTree(GroupEntry group)
    {
        GroupSpec spec = group.ToSpec();
        spec.Items = new List<ItemSpec>();
        foreach (ItemEntry item in group.Items)
        {
            if (item is GroupEntry child)
            {
                spec.Items.Add(ToSpecTree(child));
            }
            else
            {
                spec.Items.Add(((CommandEntry)item).Spec.Clone());
            }
        }

        return spec;
    }

    private GroupEntry BuildGroup(GroupSpec spec, ConfigSource source)
    {
        GroupEntry group = NewGroup(
            new GroupSettings(spec.Name, spec.Description, spec.Mode, spec.Cwd, spec.Env, spec.Expanded ?? true),
            source);
        foreach (ItemSpec item in spec.Items)
        {
            if (item is CommandSpec command)
            {
                AttachCommand(group, command.Clone());
            }
            else if (item is GroupSpec child)
            {
                group.Attach(BuildGroup(child, source));
            }
        }

        return group;
    }

    private CommandEntry AttachCommand(GroupEntry group, CommandSpec spec)
    {
        // The buffer follows the bufferLines of the command's own file. An invalid value is reported through LoadErrors;
        // the process still needs a usable buffer.
        var process = new ManagedProcess(Math.Max(1, group.Source.BufferLines));
        var entry = new CommandEntry(group, spec, process);
        entry.ChangedHandler = _ => RaiseEntryChanged(entry);
        process.Changed += entry.ChangedHandler;
        group.Attach(entry);
        return entry;
    }

    /// <summary>
    /// Marks a command removed and unsubscribes from its process. Caller holds <c>_sync</c>; the process itself is disposed by
    /// <see cref="DisposeProcesses"/> after the lock is released.
    /// </summary>
    private static void MarkCommandRemoved(CommandEntry entry)
    {
        entry.IsRemoved = true;
        if (entry.ChangedHandler is { } handler)
        {
            entry.Process.Changed -= handler;
            entry.ChangedHandler = null;
        }
    }

    /// <summary>Disposes the processes of removed commands. Call without <c>_sync</c>.</summary>
    private static void DisposeProcesses(IEnumerable<CommandEntry> removed)
    {
        foreach (CommandEntry entry in removed)
        {
            entry.Process.Dispose();
        }
    }

    /// <summary>Puts an item at the end of its parent's items, or of the top level when <paramref name="parent"/> is null. Caller holds <c>_sync</c>.</summary>
    private void Place(GroupEntry? parent, ItemEntry item)
    {
        if (parent is null)
        {
            item.Parent = null;
            _groups.Add((GroupEntry)item);
        }
        else
        {
            parent.Attach(item);
        }
    }

    /// <summary>Takes an item out of its parent's items, or out of the top level. Caller holds <c>_sync</c>.</summary>
    private void Unplace(ItemEntry item)
    {
        if (item.Parent is null)
        {
            _groups.Remove((GroupEntry)item);
        }
        else
        {
            item.Parent.Detach(item);
        }
    }

    private void EnsureOwned(ItemEntry item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item is GroupEntry group)
        {
            EnsureOwned(group);
        }
        else
        {
            EnsureOwned((CommandEntry)item);
        }
    }

    /// <summary>Refuses an edit of an item that comes from a base file.</summary>
    /// <exception cref="InvalidOperationException">The item comes from a base file.</exception>
    private static void EnsureEditable(ItemEntry item)
    {
        if (item.ReadOnlyMessage is { } message)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>Refuses a group that is not in the session tree. Takes <c>_sync</c>, so a check made alone is consistent.</summary>
    private void EnsureOwned(GroupEntry group)
    {
        ArgumentNullException.ThrowIfNull(group);
        bool attached;
        lock (_sync)
        {
            attached = IsAttached(group);
        }

        if (!attached)
        {
            throw new ArgumentException("The group does not belong to this session.", nameof(group));
        }
    }

    /// <summary>Refuses a command that is not in the session tree. Takes <c>_sync</c>, so a check made alone is consistent.</summary>
    private void EnsureOwned(CommandEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        bool attached;
        lock (_sync)
        {
            attached = !entry.IsRemoved && IsAttached(entry.Group) && entry.Group.HasItem(entry);
        }

        if (!attached)
        {
            throw new ArgumentException("The command does not belong to this session.", nameof(entry));
        }
    }

    /// <summary>
    /// True if the group is in the session tree: not removed, and linked from the top level down to it. Caller holds <c>_sync</c>.
    /// </summary>
    private bool IsAttached(GroupEntry group)
    {
        if (group.IsRemoved)
        {
            return false;
        }

        if (group.Parent is null)
        {
            return _groups.Contains(group);
        }

        return group.Parent.HasItem(group) && IsAttached(group.Parent);
    }

    /// <summary>Raises <see cref="StructureChanged"/>. Call without <c>_sync</c>.</summary>
    private void RaiseStructureChanged()
    {
        if (!_disposed)
        {
            StructureChanged?.Invoke();
        }
    }
}
