using Cmdr.Core.Config;
using Cmdr.Core.Processes;

namespace Cmdr.Core.Session;

/// <summary>
/// The UI-independent state of a cmdr session: the loaded configuration as a tree of groups and commands, one
/// <see cref="CommandEntry"/> per command with its process supervisor, editing with dirty tracking, and process actions
/// (see the partial files for those). Editing is allowed only in <see cref="SessionMode.Edit"/>, starting only in
/// <see cref="SessionMode.Run"/>.
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
    private volatile SessionMode _mode;

    private Session(IReadOnlyList<ConfigFile> chain, SessionMode mode)
    {
        _mode = mode;
        string openedDir = chain[^1].Dir;
        // Each file imports the one before it in the chain, so every source is built after the source of its base.
        var sources = new List<ConfigSource>(chain.Count);
        for (int i = 0; i < chain.Count; i++)
        {
            sources.Add(new ConfigSource(chain[i], openedDir, i > 0 ? sources[i - 1] : null));
        }

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

    /// <summary>
    /// The mode the session is in. Set by <see cref="Load"/>, then changed by <see cref="SwitchToEdit"/> and <see cref="SwitchToRun"/>.
    /// Edit and run operations check it; see the <c>Exception</c> sections of the methods.
    /// </summary>
    public SessionMode Mode => _mode;

    /// <summary>Validation errors of the configuration as loaded. Empty when it is valid.</summary>
    public IReadOnlyList<string> LoadErrors { get; }

    /// <summary>
    /// Loads the configuration file and the base files it imports (see <see cref="ConfigStore.LoadChain"/>). A missing file
    /// yields an empty session. Problems are reported in <see cref="LoadErrors"/>. The session starts in <paramref name="mode"/>.
    /// </summary>
    /// <exception cref="ConfigException">A file is malformed, a base file is missing, or the imports form a cycle.</exception>
    public static Session Load(string configPath, SessionMode mode)
    {
        ArgumentException.ThrowIfNullOrEmpty(configPath);

        return new Session(ConfigStore.LoadChain(configPath), mode);
    }

    /// <summary>
    /// Switches to edit mode. Refused, and nothing changes, while any command is running, stopping or has leftover processes,
    /// while a command is queued or a sequence run is active, or while a restart is pending. Does nothing when already in edit mode.
    /// </summary>
    /// <returns>The reason it was refused, or null when the session is now in edit mode.</returns>
    public string? SwitchToEdit()
    {
        lock (_sync)
        {
            if (_mode == SessionMode.Edit)
            {
                return null;
            }

            List<CommandEntry> entries = Walk(_groups).OfType<CommandEntry>().ToList();
            int active = entries.Count(e => e.IsActive);
            if (active > 0)
            {
                return $"Cannot switch to edit mode: {active} command(s) are running or have leftover processes.";
            }

            if (entries.Any(e => e.IsQueued) || ActiveRuns().Count > 0)
            {
                return "Cannot switch to edit mode: a sequence is running.";
            }

            if (_restarts.Count > 0)
            {
                return "Cannot switch to edit mode: a restart is pending.";
            }

            _mode = SessionMode.Edit;
            return null;
        }
    }

    /// <summary>
    /// Switches to run mode. Always allowed. Unsaved changes are kept, and so is <see cref="IsDirty"/>.
    /// </summary>
    public void SwitchToRun()
    {
        lock (_sync)
        {
            _mode = SessionMode.Run;
        }
    }

    /// <summary>
    /// Adds a group at the end of <paramref name="parent"/>'s items, or at the end of the top level when <paramref name="parent"/> is null.
    /// The group belongs to the file of its parent (the opened file at the top level).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The session is in run mode; <paramref name="parent"/> comes from a base file; or the new group would be deeper than <see cref="CmdrConfig.MaxGroupDepth"/> levels.
    /// </exception>
    public GroupEntry AddGroup(GroupEntry? parent, GroupSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(settings.Name);

        GroupEntry group;
        lock (_sync)
        {
            EnsureEditMode();
            if (parent is not null)
            {
                EnsureOwned(parent);
                EnsureEditable(parent);
                if (parent.Depth + 2 > CmdrConfig.MaxGroupDepth)
                {
                    throw new InvalidOperationException(
                        $"Cannot add a group inside '{parent.Name}': groups can be nested at most {CmdrConfig.MaxGroupDepth} levels deep.");
                }
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
    /// The session is in run mode; the group comes from a base file; the group would be moved into a group of a base file; the group
    /// would be moved into itself or one of its descendants; or the group and the groups below it would be deeper than
    /// <see cref="CmdrConfig.MaxGroupDepth"/> levels at the new place. Nothing changes when an exception is thrown.
    /// </exception>
    public void UpdateGroup(GroupEntry group, GroupSettings settings, GroupEntry? parent)
    {
        lock (_sync)
        {
            EnsureEditMode();
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

                int newLevel = parent is null ? 1 : parent.Depth + 2;
                if (newLevel + GroupHeight(group) - 1 > CmdrConfig.MaxGroupDepth)
                {
                    throw new InvalidOperationException(
                        $"Cannot move group '{group.Name}' there: groups can be nested at most {CmdrConfig.MaxGroupDepth} levels deep.");
                }
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
    /// <exception cref="InvalidOperationException">The session is in run mode, or the group comes from a base file.</exception>
    public void RemoveGroup(GroupEntry group)
    {
        var removedCommands = new List<CommandEntry>();
        lock (_sync)
        {
            EnsureEditMode();
            EnsureOwned(group);
            EnsureEditable(group);

            foreach (CommandEntry entry in group.AllCommands)
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
    /// <exception cref="InvalidOperationException">The session is in run mode, or the group comes from a base file.</exception>
    public CommandEntry AddCommand(GroupEntry group, CommandSpec spec)
    {
        CommandEntry entry;
        lock (_sync)
        {
            EnsureEditMode();
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
    /// the command moves to the end of it (any group in the tree).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The session is in run mode; the command comes from a base file; or the command would move into a group of a base file.
    /// </exception>
    public void UpdateCommand(CommandEntry entry, CommandSpec spec, GroupEntry target)
    {
        lock (_sync)
        {
            EnsureEditMode();
            EnsureOwned(entry);
            EnsureOwned(target);
            EnsureEditable(entry);
            EnsureEditable(target);
            ArgumentNullException.ThrowIfNull(spec);

            bool moving = !ReferenceEquals(target, entry.Group);
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
    /// <exception cref="InvalidOperationException">The session is in run mode, or the command comes from a base file.</exception>
    public void RemoveCommand(CommandEntry entry)
    {
        lock (_sync)
        {
            EnsureEditMode();
            EnsureOwned(entry);
            EnsureEditable(entry);

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
    /// False, with no change, when <paramref name="delta"/> is zero or at the boundary.
    /// At the top level, a group of the opened file never moves above the first group of the opened file, so base groups stay first.
    /// </returns>
    /// <exception cref="InvalidOperationException">The session is in run mode, or the item comes from a base file.</exception>
    public bool MoveItem(ItemEntry item, int delta)
    {
        lock (_sync)
        {
            EnsureEditMode();
            EnsureOwned(item);
            EnsureEditable(item);

            if (delta == 0)
            {
                return false;
            }

            GroupEntry? parent = item.Parent;
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
    /// <exception cref="InvalidOperationException">The session is in run mode, or the command comes from a base file.</exception>
    public bool MoveCommand(CommandEntry entry, int delta) => MoveItem(entry, delta);

    /// <summary>
    /// Builds the opened file's configuration from the current state: its groups only (base groups are not included), with its
    /// <c>base</c>, <c>bufferLines</c> and <c>logsDir</c>. The result shares no objects with the session.
    /// </summary>
    public CmdrConfig BuildConfig()
    {
        var config = new CmdrConfig
        {
            Base = _base,
            BufferLines = MainSource.BufferLines,
            LogsDir = LogsDirSetting,
            Env = MainSource.Env is null ? null : new Dictionary<string, string?>(MainSource.Env),
            EnvScript = MainSource.EnvScript,
            BaseEnv = MainSource.BaseEnv is null ? null : new List<string>(MainSource.BaseEnv),
            Secret = MainSource.Secret is null ? null : new List<string>(MainSource.Secret),
        };
        foreach (GroupEntry group in _groups)
        {
            if (ReferenceEquals(group.Source, MainSource))
            {
                config.Groups.Add(ToSpecTree(group));
            }
        }

        return config;
    }

    /// <summary>
    /// Validation errors of the current state. Empty when it can be saved. These are the opened file's own errors, followed by the
    /// log path conflicts that involve a command of the opened file (see <see cref="ConfigStore.LogPathErrors"/>). Errors that are
    /// only within base files do not block saving, though <see cref="LoadErrors"/> still reports them.
    /// </summary>
    public IReadOnlyList<string> Validate() => ValidateConfig(BuildConfig());

    // The errors of the opened file's state, given its built configuration, then the log path conflicts with the base files.
    private IReadOnlyList<string> ValidateConfig(CmdrConfig config)
    {
        var errors = new List<string>(config.Validate());
        errors.AddRange(ConfigStore.LogPathErrors(BuildChain(config), onlyInvolvingOpened: true));
        return errors;
    }

    /// <summary>
    /// The current state of every file of the chain, in <see cref="Sources"/> order. The opened file uses <paramref name="mainConfig"/>
    /// (see <see cref="BuildConfig"/>); each base file holds its top-level groups as they are now.
    /// </summary>
    private List<ConfigFile> BuildChain(CmdrConfig mainConfig)
    {
        var chain = new List<ConfigFile>(Sources.Count);
        foreach (ConfigSource source in Sources)
        {
            CmdrConfig config = mainConfig;
            if (!ReferenceEquals(source, MainSource))
            {
                config = new CmdrConfig { BufferLines = source.BufferLines, LogsDir = source.LogsDirSetting };
                config.Groups.AddRange(_groups.Where(group => ReferenceEquals(group.Source, source)).Select(ToSpecTree));
            }

            chain.Add(new ConfigFile(source.Path, config, source.IsBase));
        }

        return chain;
    }

    /// <summary>
    /// Saves the configuration to <see cref="ConfigPath"/> and clears the dirty flag. The state must pass <see cref="Validate"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The current state has validation errors (see <see cref="Validate"/>); the errors are the message, one per line. Nothing is written.</exception>
    public void Save()
    {
        CmdrConfig config = BuildConfig();
        IReadOnlyList<string> errors = ValidateConfig(config);
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
    /// The rows of the command list. Each file of <see cref="Sources"/> starts with its <see cref="FileRow"/> (every file, also one
    /// without groups), followed by the groups and commands of that file, each group header followed by its items, depth-first in
    /// file order. Nested groups are shown as rows too (indentation is a UI concern).
    /// </summary>
    public IReadOnlyList<ListRow> BuildRows()
    {
        var rows = new List<ListRow>();
        IReadOnlyList<ItemEntry> items = Walk(_groups).ToList();
        foreach (ConfigSource source in Sources)
        {
            rows.Add(new FileRow(source));
            foreach (ItemEntry item in items)
            {
                if (!ReferenceEquals(item.Source, source))
                {
                    continue;
                }

                if (item is GroupEntry group)
                {
                    rows.Add(new GroupRow(group));
                }
                else
                {
                    rows.Add(new CommandRow((CommandEntry)item));
                }
            }
        }

        return rows;
    }

    /// <summary>
    /// Releases the session: revokes pending restarts, cancels running environment scripts, cancels sequences, unsubscribes and disposes all processes, then waits up
    /// to two seconds in total for their logs to be written. Does not kill processes on Linux. A restart that was pending
    /// completes with <see cref="OperationCanceledException"/> and starts nothing.
    /// </summary>
    public void Dispose()
    {
        RevokeRestarts(_ => true, force: false);
        _envCache.Dispose();

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

    /// <summary>Number of group levels in the subtree of <paramref name="group"/>: 1 when it has no child groups.</summary>
    private static int GroupHeight(GroupEntry group)
    {
        int height = 1;
        foreach (GroupEntry child in group.Groups)
        {
            height = Math.Max(height, 1 + GroupHeight(child));
        }

        return height;
    }

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
        group.EnvScript = spec.EnvScript?.Clone();
        group.Secret = spec.Secret is null ? null : new List<string>(spec.Secret);
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

    /// <summary>Refuses a change of the tree unless the session is in edit mode. Caller holds <c>_sync</c>.</summary>
    /// <exception cref="InvalidOperationException">The session is in run mode.</exception>
    private void EnsureEditMode()
    {
        if (_mode != SessionMode.Edit)
        {
            throw new InvalidOperationException("Groups and commands can only be changed in edit mode.");
        }
    }

    /// <summary>Refuses a start, restart or log toggle unless the session is in run mode. Caller holds <c>_sync</c>.</summary>
    /// <exception cref="InvalidOperationException">The session is in edit mode.</exception>
    private void EnsureRunMode(string message = "Commands can only be started in run mode.")
    {
        if (_mode != SessionMode.Run)
        {
            throw new InvalidOperationException(message);
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
