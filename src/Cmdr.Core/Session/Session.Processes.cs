using Cmdr.Core.Config;
using Cmdr.Core.Processes;

namespace Cmdr.Core.Session;

public sealed partial class Session
{
    /// <summary>
    /// Enter on command rows: if any target is stopping or exited with leftover processes, force-kills those and starts nothing.
    /// Otherwise starts the targets that can start: parallel groups start each command now; sequential groups start a
    /// sequence of them in file order. Running commands are left alone.
    /// </summary>
    /// <returns>A message when something was not done, otherwise null.</returns>
    /// <exception cref="ArgumentException">The targets are not all in the same group.</exception>
    public string? Activate(IReadOnlyList<CommandEntry> targets)
    {
        GroupEntry? group = GroupOf(targets);
        if (group is null)
        {
            return null;
        }

        // Killing takes precedence: if any target is stopping or has leftovers, Enter only kills those and starts nothing.
        List<CommandEntry> killable = targets.Where(IsKillable).ToList();
        if (killable.Count > 0)
        {
            foreach (CommandEntry entry in killable)
            {
                entry.Process.ForceKill();
            }

            return null;
        }

        List<CommandEntry> startable = targets.Where(e => e.Process.CanStart).ToList();
        if (startable.Count == 0)
        {
            return null;
        }

        return StartCommands(group, startable);
    }

    /// <summary>
    /// Enter on a group: if anything below it is stopping or has leftover processes, force-kills those and starts nothing.
    /// Otherwise runs the group: a sequential group runs its startable items in order (sub-groups as sub-runs); a parallel
    /// group starts its startable commands now and its sequential sub-groups as runs, and tracks nothing itself. Items that
    /// cannot start are skipped, and groups left with nothing to start are dropped.
    /// </summary>
    /// <returns>A message when the group could not be started because it overlaps a running sequence; otherwise null.</returns>
    /// <exception cref="ArgumentException">The group does not belong to this session.</exception>
    public string? Activate(GroupEntry group)
    {
        EnsureOwned(group);

        List<CommandEntry> killable = group.AllCommands.Where(IsKillable).ToList();
        if (killable.Count > 0)
        {
            foreach (CommandEntry entry in killable)
            {
                entry.Process.ForceKill();
            }

            return null;
        }

        return StartGroup(group);
    }

    /// <summary>
    /// Esc / Ctrl+C on command rows: cancels the innermost active run containing each target (that run fails, and so do the
    /// runs around it). Gracefully stops the targets that are running or stopping. Stop signals are sent in the background.
    /// </summary>
    /// <exception cref="ArgumentException">The targets are not all in the same group.</exception>
    public string? Stop(IReadOnlyList<CommandEntry> targets)
    {
        GroupOf(targets);
        CancelInnermostRuns(targets);

        foreach (CommandEntry entry in targets.Where(IsRunningOrStopping).ToList())
        {
            Task.Run(() => entry.Process.RequestStop());
        }

        return null;
    }

    /// <summary>
    /// Esc / Ctrl+C on a group: cancels every active run at or below the group, and the enclosing runs that have the group as
    /// a current or pending step (so they fail upward). Gracefully stops everything running below the group.
    /// </summary>
    /// <exception cref="ArgumentException">The group does not belong to this session.</exception>
    public string? Stop(GroupEntry group)
    {
        EnsureOwned(group);
        CancelRunsFor(group);

        foreach (CommandEntry entry in group.AllCommands.Where(IsRunningOrStopping).ToList())
        {
            Task.Run(() => entry.Process.RequestStop());
        }

        return null;
    }

    /// <summary>Force-kills the process trees of the targets. Never automatic.</summary>
    /// <exception cref="ArgumentException">The targets are not all in the same group.</exception>
    public void ForceKill(IReadOnlyList<CommandEntry> targets)
    {
        GroupOf(targets);
        foreach (CommandEntry entry in targets)
        {
            entry.Process.ForceKill();
        }
    }

    /// <summary>
    /// F5 on command rows: cancels the innermost runs containing the targets (and waits for the runs around them to end),
    /// stops the running or stopping targets and waits for their leaders to exit, then starts the targets that can start.
    /// </summary>
    /// <returns>A message naming targets that could not start, or the reason a sequence did not start; otherwise null.</returns>
    /// <exception cref="ArgumentException">The targets are not all in the same group.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled while waiting.</exception>
    public async Task<string?> RestartAsync(IReadOnlyList<CommandEntry> targets, CancellationToken ct = default)
    {
        GroupEntry? group = GroupOf(targets);
        if (group is null)
        {
            return null;
        }

        List<SequenceRun> affected = ActiveRuns().Where(run => targets.Any(target => run.Contains(target))).ToList();
        CancelInnermostRuns(targets);
        await Task.WhenAll(affected.Select(run => run.Result)).WaitAsync(ct);

        List<CommandEntry> running = targets.Where(IsRunningOrStopping).ToList();
        await Task.WhenAll(running.Select(entry => Task.Run(() => entry.Process.RequestStop(), ct)));
        await Task.WhenAll(running.Select(entry => entry.Process.WaitForExitAsync(ct)));

        var startable = new List<CommandEntry>();
        var blocked = new List<string>();
        foreach (CommandEntry entry in targets)
        {
            if (entry.Process.CanStart)
            {
                startable.Add(entry);
            }
            else
            {
                blocked.Add($"'{entry.Spec.Name}'");
            }
        }

        var messages = new List<string>();
        if (startable.Count > 0)
        {
            string? message = StartCommands(group, startable);
            if (message is not null)
            {
                messages.Add(message);
            }
        }

        if (blocked.Count > 0)
        {
            messages.Add($"Cannot start {string.Join(", ", blocked)}: leftover processes from an earlier run are still running.");
        }

        return messages.Count == 0 ? null : string.Join(" ", messages);
    }

    /// <summary>
    /// F5 on a group: cancels the runs at or below it and the enclosing runs that contain it, stops everything running below
    /// it and waits for the leaders to exit, then runs the group as Enter does (without the kill step).
    /// </summary>
    /// <returns>Messages about refused or blocked starts, or null.</returns>
    /// <exception cref="ArgumentException">The group does not belong to this session.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled while waiting.</exception>
    public async Task<string?> RestartAsync(GroupEntry group, CancellationToken ct = default)
    {
        EnsureOwned(group);

        List<SequenceRun> cancelled = CancelRunsFor(group);
        await Task.WhenAll(cancelled.Select(run => run.Result)).WaitAsync(ct);

        List<CommandEntry> running = group.AllCommands.Where(IsRunningOrStopping).ToList();
        await Task.WhenAll(running.Select(entry => Task.Run(() => entry.Process.RequestStop(), ct)));
        await Task.WhenAll(running.Select(entry => entry.Process.WaitForExitAsync(ct)));

        // Leftovers are checked before starting: a command started below is not a blocked one.
        var blocked = group.AllCommands.Where(entry => !entry.Process.CanStart).Select(entry => $"'{entry.Spec.Name}'").ToList();

        var messages = new List<string>();
        string? message = StartGroup(group);
        if (message is not null)
        {
            messages.Add(message);
        }

        if (blocked.Count > 0)
        {
            messages.Add($"Cannot start {string.Join(", ", blocked)}: leftover processes from an earlier run are still running.");
        }

        return messages.Count == 0 ? null : string.Join(" ", messages);
    }

    /// <summary>Toggles the log file of a command. Does not change the spec or the dirty flag.</summary>
    public void ToggleLog(CommandEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Process.Output.IsLogging)
        {
            entry.Process.Output.DisableLog();
        }
        else
        {
            entry.Process.Output.EnableLog(ResolveLogPath(entry));
        }

        RaiseEntryChanged(entry);
    }

    /// <summary>All commands that are running, stopping, or have leftover processes.</summary>
    public IReadOnlyList<CommandEntry> ActiveEntries => AllEntries.Where(e => e.IsActive).ToList();

    /// <summary>Quit: cancels all runs and gracefully stops all running commands (in the background).</summary>
    public void StopAll()
    {
        CancelAllRuns();
        foreach (CommandEntry entry in AllEntries.Where(IsRunningOrStopping))
        {
            Task.Run(() => entry.Process.RequestStop());
        }
    }

    /// <summary>Quit: cancels all runs and force-kills all active commands.</summary>
    public void ForceKillAll()
    {
        CancelAllRuns(force: true);
        foreach (CommandEntry entry in ActiveEntries)
        {
            entry.Process.ForceKill();
        }
    }

    /// <summary>Completes when the leaders of all commands, as they are now, have exited. Does not wait for later starts.</summary>
    public Task WaitForAllLeadersAsync(CancellationToken ct)
    {
        return Task.WhenAll(AllEntries.Select(e => e.Process.WaitForExitAsync(ct)));
    }

    /// <summary>
    /// Starts one command: enables its log file if configured, then launches it with the launch spec of its ancestors.
    /// Returns false, with a system line in the output, if the launch could not be prepared.
    /// </summary>
    internal bool StartEntry(CommandEntry entry)
    {
        CommandSpec spec = entry.Spec;
        try
        {
            if (spec.Log.Enabled)
            {
                entry.Process.Output.EnableLog(ResolveLogPath(entry));
            }

            entry.Process.Start(LaunchSpec.From(entry.Group.SpecChain(), spec, ConfigDir));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ObjectDisposedException or NotSupportedException)
        {
            entry.Process.Output.WriteSystem($"failed to start: {ex.Message}");
            return false;
        }
    }

    private string ResolveLogPath(CommandEntry entry) =>
        ConfigPaths.ResolveLogPath(entry.Group.SpecChain(), entry.Spec, LogsDir);

    /// <summary>
    /// Starts the given commands of a group: in sequence for a sequential group, otherwise each at once. Refuses, with a
    /// message, when the group already has a sequence or a command is part of a running sequence.
    /// </summary>
    private string? StartCommands(GroupEntry group, IReadOnlyList<CommandEntry> startable)
    {
        if (group.Mode == GroupMode.Sequential)
        {
            return StartSequence(group, startable);
        }

        lock (_sync)
        {
            CommandEntry? busy = startable.FirstOrDefault(IsBusyCommand);
            if (busy is not null)
            {
                return BusyMessage(busy);
            }
        }

        foreach (CommandEntry entry in startable)
        {
            StartEntry(entry);
        }

        return null;
    }

    /// <summary>Creates a sequence over the commands and starts it in the background. Returns a message if one cannot start.</summary>
    private string? StartSequence(GroupEntry group, IReadOnlyList<CommandEntry> entries)
    {
        var run = new SequenceRun(this, group, entries.Select(e => new PlanNode(e)).ToList());

        lock (_sync)
        {
            if (group.ActiveSequence is not null)
            {
                return $"A sequence is already running in group '{group.Name}'.";
            }

            CommandEntry? busy = entries.FirstOrDefault(IsBusyCommand);
            if (busy is not null)
            {
                return BusyMessage(busy);
            }

            Register(run);
        }

        Launch(run);
        return null;
    }

    /// <summary>Runs a group (see <see cref="Activate(GroupEntry)"/>) without the kill step. Returns a message when it is refused.</summary>
    private string? StartGroup(GroupEntry group)
    {
        List<PlanNode> children = PlanChildren(group);
        if (children.Count == 0)
        {
            return null;
        }

        SequenceRun? run = null;
        lock (_sync)
        {
            if (group.Mode == GroupMode.Sequential && group.ActiveSequence is not null)
            {
                return $"A sequence is already running in group '{group.Name}'.";
            }

            string? refusal = RefusalFor(children);
            if (refusal is not null)
            {
                return refusal;
            }

            if (group.Mode == GroupMode.Sequential)
            {
                run = new SequenceRun(this, group, children);
                Register(run);
            }
        }

        if (run is not null)
        {
            Launch(run);
        }
        else
        {
            // Untracked: the branches run on their own; nothing waits for them.
            StartBranches(children, owner: null);
        }

        return null;
    }

    /// <summary>
    /// The plan of a group's startable items: commands that can start, and sub-groups that still have something to start.
    /// </summary>
    private static List<PlanNode> PlanChildren(GroupEntry group)
    {
        var nodes = new List<PlanNode>();
        foreach (ItemEntry item in group.Items)
        {
            if (item is CommandEntry command)
            {
                if (command.Process.CanStart)
                {
                    nodes.Add(new PlanNode(command));
                }
            }
            else if (item is GroupEntry child)
            {
                List<PlanNode> children = PlanChildren(child);
                if (children.Count > 0)
                {
                    nodes.Add(new PlanNode(child, children));
                }
            }
        }

        return nodes;
    }

    /// <summary>
    /// The reason a plan cannot start: a sequential group in it already has a run, or a command in it is busy. Null when it can.
    /// Caller holds <c>_sync</c>.
    /// </summary>
    private string? RefusalFor(IReadOnlyList<PlanNode> nodes)
    {
        foreach (PlanNode node in nodes)
        {
            if (node.Command is { } command)
            {
                if (IsBusyCommand(command))
                {
                    return BusyMessage(command);
                }
            }
            else if (node.Group is { } group)
            {
                if (group.Mode == GroupMode.Sequential && group.ActiveSequence is not null)
                {
                    return $"A sequence is already running in group '{group.Name}'.";
                }

                string? nested = RefusalFor(node.Children);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Registers a sub-run on its group, if the group has no active run, and starts it. Returns false when the group is busy.
    /// </summary>
    internal bool TryStart(SequenceRun run)
    {
        lock (_sync)
        {
            if (run.Group.ActiveSequence is not null)
            {
                return false;
            }

            Register(run);
        }

        Launch(run);
        return true;
    }

    /// <summary>Makes the run the active run of its group and marks its planned commands queued. Caller holds <c>_sync</c>.</summary>
    private static void Register(SequenceRun run)
    {
        run.Group.ActiveSequence = run;
        foreach (CommandEntry entry in run.PlannedCommands)
        {
            entry.IsQueued = true;
        }
    }

    /// <summary>Starts a registered run in the background. Events are raised outside the lock.</summary>
    private void Launch(SequenceRun run)
    {
        foreach (CommandEntry entry in run.PlannedCommands)
        {
            RaiseEntryChanged(entry);
        }

        run.Result = Task.Run(() => run.RunAsync());
    }

    /// <summary>
    /// Starts the branches of a parallel group's planned children now: a command is started, a sequential group becomes a
    /// run, and a parallel group recurses. Each branch completes with true on success. With an <paramref name="owner"/>
    /// run, a branch started after the owner was cancelled is stopped as the owner would stop it.
    /// </summary>
    internal List<Task<bool>> StartBranches(IReadOnlyList<PlanNode> children, SequenceRun? owner)
    {
        var branches = new List<Task<bool>>();
        foreach (PlanNode child in children)
        {
            if (child.Command is { } command)
            {
                branches.Add(StartBranchCommand(command, owner));
            }
            else if (child.Group is { } group)
            {
                if (group.Mode == GroupMode.Sequential)
                {
                    var run = new SequenceRun(this, group, child.Children);
                    if (TryStart(run))
                    {
                        if (owner is { IsCancelled: true })
                        {
                            run.Cancel(owner.IsForced);
                        }

                        branches.Add(run.Result);
                    }
                    else
                    {
                        Unqueue(child);
                        WriteRefusal(child, $"A sequence is already running in group '{group.Name}'.");
                        branches.Add(Task.FromResult(false));
                    }
                }
                else
                {
                    branches.Add(AllSucceedAsync(StartBranches(child.Children, owner)));
                }
            }
        }

        return branches;
    }

    /// <summary>Starts a branch command and completes when its leader exits: true for exit code 0.</summary>
    private Task<bool> StartBranchCommand(CommandEntry entry, SequenceRun? owner)
    {
        entry.IsQueued = false;
        if (owner is { IsCancelled: true })
        {
            return Task.FromResult(false);
        }

        if (!entry.Process.CanStart)
        {
            entry.Process.Output.WriteSystem("sequence cancelled: command cannot start");
            return Task.FromResult(false);
        }

        if (!StartEntry(entry) || entry.Process.Snapshot.State == ProcessState.FailedToStart)
        {
            return Task.FromResult(false);
        }

        owner?.StopIfCancelled(entry);
        return WaitBranchAsync(entry);
    }

    private static async Task<bool> WaitBranchAsync(CommandEntry entry)
    {
        await entry.Process.WaitForExitAsync().ConfigureAwait(false);

        int? code = entry.Process.Snapshot.ExitCode;
        if (code == 0)
        {
            return true;
        }

        entry.Process.Output.WriteSystem($"sequence cancelled: exit code {code?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}");
        return false;
    }

    /// <summary>Completes with true when every task succeeds, and with false as soon as one fails. Others are not cancelled.</summary>
    internal static async Task<bool> AllSucceedAsync(IReadOnlyList<Task<bool>> tasks, CancellationToken ct = default)
    {
        var remaining = tasks.ToList();
        while (remaining.Count > 0)
        {
            Task<bool> finished = await Task.WhenAny(remaining).WaitAsync(ct).ConfigureAwait(false);
            remaining.Remove(finished);
            if (!await finished.ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Clears the queued flags of the commands in a planned step that will not run.</summary>
    internal void Unqueue(PlanNode node)
    {
        var entries = new List<CommandEntry>();
        node.CollectCommands(entries);
        lock (_sync)
        {
            foreach (CommandEntry entry in entries)
            {
                entry.IsQueued = false;
            }
        }

        foreach (CommandEntry entry in entries)
        {
            RaiseEntryChanged(entry);
        }
    }

    /// <summary>Writes a reason into the output of the first command of a step that did not start.</summary>
    internal static void WriteRefusal(PlanNode node, string message)
    {
        var entries = new List<CommandEntry>();
        node.CollectCommands(entries);
        if (entries.Count > 0)
        {
            entries[0].Process.Output.WriteSystem(message);
        }
    }

    /// <summary>Called by a run when it ends: clears its group's active run and refreshes its commands.</summary>
    internal void FinishSequence(SequenceRun run)
    {
        lock (_sync)
        {
            if (ReferenceEquals(run.Group.ActiveSequence, run))
            {
                run.Group.ActiveSequence = null;
            }
        }

        foreach (CommandEntry entry in run.PlannedCommands)
        {
            RaiseEntryChanged(entry);
        }
    }

    /// <summary>Every active run in the session, nested ones included.</summary>
    private IEnumerable<SequenceRun> ActiveRuns() =>
        GroupEntry.Walk(_groups).OfType<GroupEntry>().Select(g => g.ActiveSequence).OfType<SequenceRun>().ToList();

    /// <summary>
    /// The innermost active run containing each target is cancelled. Innermost is the deepest group among the runs that
    /// contain the target; runs around it fail through their awaited steps.
    /// </summary>
    private List<SequenceRun> CancelInnermostRuns(IReadOnlyList<CommandEntry> targets)
    {
        List<SequenceRun> runs = ActiveRuns().ToList();
        var innermost = new List<SequenceRun>();
        foreach (CommandEntry target in targets)
        {
            SequenceRun? run = runs
                .Where(candidate => candidate.Contains(target))
                .OrderByDescending(candidate => candidate.Group.Depth)
                .FirstOrDefault();
            if (run is not null && !innermost.Contains(run))
            {
                innermost.Add(run);
            }
        }

        foreach (SequenceRun run in innermost)
        {
            run.Cancel();
        }

        return innermost;
    }

    /// <summary>
    /// Cancels the runs at or below the group, and the enclosing runs that have the group as a step. Returns the runs cancelled.
    /// </summary>
    private List<SequenceRun> CancelRunsFor(GroupEntry group)
    {
        var runs = new List<SequenceRun>();
        foreach (GroupEntry inside in group.SelfAndDescendantGroups())
        {
            if (inside.ActiveSequence is { } insideRun)
            {
                runs.Add(insideRun);
            }
        }

        for (GroupEntry? ancestor = group.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.ActiveSequence is { } enclosing && enclosing.ContainsGroup(group) && !runs.Contains(enclosing))
            {
                runs.Add(enclosing);
            }
        }

        foreach (SequenceRun run in runs)
        {
            run.Cancel();
        }

        return runs;
    }

    private void CancelAllRuns(bool force = false)
    {
        foreach (SequenceRun run in ActiveRuns().ToList())
        {
            run.Cancel(force);
        }
    }

    /// <summary>True if the command is queued, or is the current or a pending step of an active run.</summary>
    private bool IsBusyCommand(CommandEntry entry) =>
        entry.IsQueued || ActiveRuns().Any(run => run.Contains(entry));

    /// <summary>
    /// True if the group has an active run, is a step of one, contains a busy command, or contains a group that is busy.
    /// </summary>
    private bool IsBusyGroup(GroupEntry group) =>
        group.ActiveSequence is not null
        || ActiveRuns().Any(run => run.ContainsGroup(group))
        || group.Items.Any(item => item is GroupEntry child ? IsBusyGroup(child) : IsBusyCommand((CommandEntry)item));

    private static string BusyMessage(CommandEntry entry) =>
        $"Cannot start '{entry.Spec.Name}': it is part of a running sequence.";

    /// <summary>
    /// The commands of the targets. Throws when targets span several groups or were removed.
    /// </summary>
    private static GroupEntry? GroupOf(IReadOnlyList<CommandEntry> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0)
        {
            return null;
        }

        GroupEntry group = targets[0].Group;
        foreach (CommandEntry entry in targets)
        {
            if (!ReferenceEquals(entry.Group, group))
            {
                throw new ArgumentException("All targets must belong to the same group.", nameof(targets));
            }

            if (entry.IsRemoved)
            {
                throw new ArgumentException($"Command '{entry.Spec.Name}' has been removed from the session.", nameof(targets));
            }
        }

        return group;
    }

    private static bool IsKillable(CommandEntry entry)
    {
        ProcessSnapshot snapshot = entry.Process.Snapshot;
        return snapshot.State == ProcessState.Stopping || (snapshot.State == ProcessState.Exited && snapshot.LeftoverCount > 0);
    }

    private static bool IsRunningOrStopping(CommandEntry entry)
    {
        ProcessState state = entry.Process.Snapshot.State;
        return state is ProcessState.Running or ProcessState.Stopping;
    }
}
