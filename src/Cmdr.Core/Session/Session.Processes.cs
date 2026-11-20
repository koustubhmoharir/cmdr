using Cmdr.Core.Config;
using Cmdr.Core.Processes;

namespace Cmdr.Core.Session;

public sealed partial class Session
{
    /// <summary>
    /// Enter on command rows: if any target is stopping or exited with leftover processes, force-kills those and starts nothing.
    /// Otherwise starts the targets that can start: parallel groups start each command now; sequential groups start a
    /// sequence of them in file order. The targets must be distinct commands in file order; running commands are left alone.
    /// </summary>
    /// <param name="inputs">Values for the input placeholders of the started commands. Null means no values.</param>
    /// <returns>A message when something was not done, otherwise null.</returns>
    /// <exception cref="ArgumentException">The targets are not all in the same group, or are not distinct commands in file order.</exception>
    public string? Activate(IReadOnlyList<CommandEntry> targets, IReadOnlyDictionary<string, string>? inputs = null) =>
        Activate(PrepareActivate(targets), inputs);

    /// <summary>
    /// Decides what Enter on these command rows does, as <see cref="Activate(IReadOnlyList{CommandEntry}, IReadOnlyDictionary{string, string}?)"/>
    /// would, and the inputs it needs. If any target is stopping or has leftover processes, the decision is to kill those; otherwise
    /// it is to start the targets that can start now. Nothing is stopped, started or killed here.
    /// </summary>
    /// <exception cref="ArgumentException">The targets are not all in the same group, or are not distinct commands in file order.</exception>
    public PreparedActivation PrepareActivate(IReadOnlyList<CommandEntry> targets)
    {
        GroupEntry? group = GroupOf(targets);
        if (group is null)
        {
            return new PreparedActivation(this, null, isGroup: false, Array.Empty<CommandEntry>(), Array.Empty<CommandEntry>(), Array.Empty<CommandEntry>(), NoRequests);
        }

        List<CommandEntry> killable = targets.Where(IsKillable).ToList();
        if (killable.Count > 0)
        {
            return new PreparedActivation(this, group, isGroup: false, targets.ToList(), killable, Array.Empty<CommandEntry>(), NoRequests);
        }

        List<CommandEntry> startable = targets.Where(e => e.Process.CanStart).ToList();
        if (startable.Count == 0)
        {
            return new PreparedActivation(this, group, isGroup: false, targets.ToList(), Array.Empty<CommandEntry>(), startable, NoRequests);
        }

        bool refused;
        lock (_sync)
        {
            refused = CommandsRefusal(group, startable) is not null;
        }

        IReadOnlyList<InputPlaceholders.InputRequest> requests = refused ? NoRequests : RequestsFor(startable);
        return new PreparedActivation(this, group, isGroup: false, targets.ToList(), Array.Empty<CommandEntry>(), startable, requests);
    }

    /// <summary>
    /// Carries out what <paramref name="prepared"/> decided, with the input values for it. A kill decision force-kills the commands
    /// that were killable when it was prepared, if they still are, and starts nothing. A start decision starts only the commands it
    /// named that can still start: commands that became startable since then are not started, and their inputs are not needed.
    /// Refusals (a running sequence, a busy command, a command of a sequential group still running) are checked now, as
    /// <see cref="Activate(IReadOnlyList{CommandEntry}, IReadOnlyDictionary{string, string}?)"/> does.
    /// </summary>
    /// <param name="inputs">Values for the input placeholders of the started commands. Null means no values.</param>
    /// <returns>A message when something was not done, otherwise null.</returns>
    /// <exception cref="ArgumentException">The prepared activation was made by another session, or its targets or group have since been removed.</exception>
    public string? Activate(PreparedActivation prepared, IReadOnlyDictionary<string, string>? inputs = null)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (!ReferenceEquals(prepared.Owner, this))
        {
            throw new ArgumentException("The prepared activation was made by another session.", nameof(prepared));
        }

        // Killing takes precedence: what was killable when Enter was pressed is killed, and nothing starts.
        if (prepared.Killable.Count > 0)
        {
            foreach (CommandEntry entry in prepared.Killable)
            {
                if (IsKillable(entry))
                {
                    entry.Process.ForceKill();
                }
            }

            return null;
        }

        if (prepared.IsGroup)
        {
            GroupEntry group = prepared.Group!;
            EnsureOwned(group);
            if (prepared.Startable.Count == 0)
            {
                return null;
            }

            return StartGroup(group, Freeze(inputs), only: new HashSet<CommandEntry>(prepared.Startable));
        }

        GroupEntry? targetsGroup = GroupOf(prepared.Targets);
        if (targetsGroup is null)
        {
            return null;
        }

        List<CommandEntry> startableNow = prepared.Startable.Where(e => e.Process.CanStart).ToList();
        if (startableNow.Count == 0)
        {
            return null;
        }

        return StartCommands(targetsGroup, startableNow, Freeze(inputs));
    }

    /// <summary>
    /// Enter on a group: if anything below it is stopping or has leftover processes, force-kills those and starts nothing.
    /// Otherwise runs the group: a sequential group runs its startable items in order (sub-groups as sub-runs); a parallel
    /// group starts its startable commands now and its sequential sub-groups as runs, and tracks nothing itself. Items that
    /// cannot start are skipped, and groups left with nothing to start are dropped.
    /// </summary>
    /// <param name="inputs">Values for the input placeholders of the started commands. Null means no values.</param>
    /// <returns>
    /// A message when the group could not be started because it overlaps a running sequence, or a sequential group at or above a
    /// part of it has a command that is still running or stopping; otherwise null.
    /// </returns>
    /// <exception cref="ArgumentException">The group does not belong to this session.</exception>
    public string? Activate(GroupEntry group, IReadOnlyDictionary<string, string>? inputs = null) =>
        Activate(PrepareActivate(group), inputs);

    /// <summary>
    /// Decides what Enter on a group does, as <see cref="Activate(GroupEntry, IReadOnlyDictionary{string, string}?)"/> would, and the
    /// inputs it needs: if anything below the group is stopping or has leftover processes, the decision is to kill those; otherwise
    /// it is to start the group's startable commands, in the group's plan. Nothing is stopped, started or killed here.
    /// </summary>
    /// <exception cref="ArgumentException">The group does not belong to this session.</exception>
    public PreparedActivation PrepareActivate(GroupEntry group)
    {
        EnsureOwned(group);
        List<CommandEntry> killable;
        List<PlanNode> children;
        lock (_sync)
        {
            killable = group.AllCommands.Where(IsKillable).ToList();
            children = killable.Count > 0 ? new List<PlanNode>() : PlanChildren(group, e => e.Process.CanStart);
        }

        if (killable.Count > 0)
        {
            return new PreparedActivation(this, group, isGroup: true, Array.Empty<CommandEntry>(), killable, Array.Empty<CommandEntry>(), NoRequests);
        }

        List<CommandEntry> startable = CollectCommands(children);
        if (startable.Count == 0)
        {
            return new PreparedActivation(this, group, isGroup: true, Array.Empty<CommandEntry>(), Array.Empty<CommandEntry>(), startable, NoRequests);
        }

        bool refused;
        lock (_sync)
        {
            refused = GroupRefusal(group, children) is not null;
        }

        IReadOnlyList<InputPlaceholders.InputRequest> requests = refused ? NoRequests : RequestsFor(startable);
        return new PreparedActivation(this, group, isGroup: true, Array.Empty<CommandEntry>(), Array.Empty<CommandEntry>(), startable, requests);
    }

    /// <summary>
    /// Ctrl+C on command rows: cancels the innermost active run containing each target (that run fails, and so do the
    /// runs around it). Gracefully stops the targets that are running or stopping. Stop signals are sent in the background.
    /// A pending restart of any of the targets is revoked first: it completes with <see cref="OperationCanceledException"/>
    /// and starts nothing.
    /// </summary>
    /// <exception cref="ArgumentException">The targets are not all in the same group, or are not distinct commands in file order.</exception>
    public string? Stop(IReadOnlyList<CommandEntry> targets)
    {
        GroupOf(targets);
        RevokeRestarts(op => op.Commands.Overlaps(targets), force: false);
        CancelInnermostRuns(targets);

        List<CommandEntry> running;
        lock (_sync)
        {
            running = targets.Where(IsRunningOrStopping).ToList();
        }

        foreach (CommandEntry entry in running)
        {
            Task.Run(() => entry.Process.RequestStop());
        }

        return null;
    }

    /// <summary>
    /// Ctrl+C on a group: cancels every active run at or below the group, and the enclosing runs that have the group as
    /// a current or pending step (so they fail upward). Gracefully stops everything running below the group. A pending restart
    /// of any command below the group is revoked first: it completes with <see cref="OperationCanceledException"/> and starts nothing.
    /// </summary>
    /// <exception cref="ArgumentException">The group does not belong to this session.</exception>
    public string? Stop(GroupEntry group)
    {
        EnsureOwned(group);
        RevokeRestarts(op => op.Commands.Overlaps(group.AllCommands), force: false);
        CancelRunsFor(group);

        List<CommandEntry> running;
        lock (_sync)
        {
            running = group.AllCommands.Where(IsRunningOrStopping).ToList();
        }

        foreach (CommandEntry entry in running)
        {
            Task.Run(() => entry.Process.RequestStop());
        }

        return null;
    }

    /// <summary>
    /// Force-kills the process trees of the targets. Never automatic. A pending restart of any of the targets is revoked first
    /// (it completes with <see cref="OperationCanceledException"/>) and starts nothing, not even after the kill.
    /// </summary>
    /// <exception cref="ArgumentException">The targets are not all in the same group, or are not distinct commands in file order.</exception>
    public void ForceKill(IReadOnlyList<CommandEntry> targets)
    {
        GroupOf(targets);
        RevokeRestarts(op => op.Commands.Overlaps(targets), force: true);
        foreach (CommandEntry entry in targets)
        {
            entry.Process.ForceKill();
        }
    }

    /// <summary>
    /// The commands among <paramref name="commands"/> that a pending restart will start again (one that is still stopping
    /// them, or waiting for their runs to end), in the given order, with one handle per command. When several pending restarts
    /// wait for the same command, the first one found is used. Lets a second F5 ask instead of starting another restart.
    /// </summary>
    /// <exception cref="ArgumentException">A command does not belong to this session.</exception>
    public IReadOnlyList<PendingRestart> PendingRestartsOf(IReadOnlyList<CommandEntry> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        foreach (CommandEntry entry in commands)
        {
            EnsureOwned(entry);
        }

        var result = new List<PendingRestart>();
        lock (_sync)
        {
            foreach (CommandEntry entry in commands)
            {
                RestartOperation? op = _restarts.FirstOrDefault(o => !o.Cancelled && o.Commands.Contains(entry));
                if (op is not null)
                {
                    result.Add(new PendingRestart(entry, op, entry.Process.RunId));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Force-kills the processes that the pending restarts in <paramref name="pending"/> wait for. Unlike
    /// <see cref="ForceKill(IReadOnlyList{CommandEntry})"/>, the restarts are kept: they start the commands again as soon as
    /// the killed leaders have exited, and leftovers that would block the start are killed too. Commands with nothing to kill
    /// are skipped.
    /// </summary>
    /// <remarks>
    /// A handle is stale when its restart has finished or been revoked, or its command has started a newer run since the handle
    /// was taken. Stale handles are skipped, so a replacement run that a restart has already started is never killed.
    /// </remarks>
    /// <exception cref="ArgumentException">A command does not belong to this session.</exception>
    public void ForceKillForPendingRestart(IReadOnlyList<PendingRestart> pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        foreach (PendingRestart handle in pending)
        {
            ArgumentNullException.ThrowIfNull(handle);
            EnsureOwned(handle.Command);
        }

        var current = new List<PendingRestart>();
        lock (_sync)
        {
            foreach (PendingRestart handle in pending)
            {
                if (!handle.Operation.Cancelled && _restarts.Contains(handle.Operation))
                {
                    current.Add(handle);
                }
            }
        }

        foreach (PendingRestart handle in current)
        {
            handle.Command.Process.ForceKill(handle.RunId); // does nothing unless the run is still current and has something to kill
        }
    }

    /// <summary>
    /// F5 on command rows: cancels the innermost runs containing the targets (and waits for the runs around them to end),
    /// stops the running or stopping targets and waits for their leaders to exit, then starts the targets that can start.
    /// Same as <see cref="PrepareRestart(IReadOnlyList{CommandEntry}, IReadOnlyDictionary{string, string}?, CancellationToken)"/>
    /// followed by <see cref="RunRestartAsync"/>.
    /// </summary>
    /// <param name="inputs">Values for the input placeholders of the started commands. Null means no values.</param>
    /// <returns>A message naming targets that could not start, or the reason a sequence did not start; otherwise null.</returns>
    /// <exception cref="ArgumentException">The targets are not all in the same group, or are not distinct commands in file order.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="ct"/> was cancelled, or the restart was revoked by a Stop of these targets, a ForceKill, StopAll,
    /// ForceKillAll or Dispose, before it started anything. Nothing is started then. A revocation that arrives while the
    /// targets are being started stops what was started and throws too.
    /// </exception>
    public async Task<string?> RestartAsync(
        IReadOnlyList<CommandEntry> targets,
        IReadOnlyDictionary<string, string>? inputs = null,
        CancellationToken ct = default)
    {
        return await RunRestartAsync(PrepareRestart(targets, inputs, ct));
    }

    /// <summary>
    /// First half of <see cref="RestartAsync(IReadOnlyList{CommandEntry}, IReadOnlyDictionary{string, string}?, CancellationToken)"/>:
    /// registers a restart of the targets as pending, without stopping or starting anything. From then on, Stop, ForceKill,
    /// StopAll, ForceKillAll and Dispose revoke it until <see cref="RunRestartAsync"/> runs it. Does not raise events, wait
    /// or touch processes, so it is cheap enough to call on a UI thread. Returns a restart with nothing to do when the targets
    /// are empty; running that returns null.
    /// </summary>
    /// <exception cref="ArgumentException">The targets are not all in the same group, or are not distinct commands in file order.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was already cancelled.</exception>
    public PreparedRestart PrepareRestart(
        IReadOnlyList<CommandEntry> targets,
        IReadOnlyDictionary<string, string>? inputs = null,
        CancellationToken ct = default)
    {
        GroupEntry? group = GroupOf(targets);
        if (group is null)
        {
            return new PreparedRestart(this, null, null, Array.Empty<SequenceRun>(), NoInputs);
        }

        ct.ThrowIfCancellationRequested();
        IReadOnlyDictionary<string, string> values = Freeze(inputs);
        List<CommandEntry> targetList = targets.ToList();

        lock (_sync)
        {
            List<SequenceRun> affected = ActiveRuns().Where(run => targetList.Any(target => run.Contains(target))).ToList();
            RestartOperation op = RegisterRestartLocked(group, new HashSet<CommandEntry>(targetList), commandsMustStayInGroup: true, ct);
            return new PreparedRestart(this, op, targetList, affected, values);
        }
    }

    /// <summary>
    /// F5 on a group: cancels the runs at or below it and the enclosing runs that contain it, stops everything running below
    /// it and waits for the leaders to exit, then runs the group as Enter does (without the kill step). The restart covers the
    /// group's commands as they are when it is called.
    /// Same as <see cref="PrepareRestart(GroupEntry, IReadOnlyDictionary{string, string}?, CancellationToken)"/>
    /// followed by <see cref="RunRestartAsync"/>.
    /// </summary>
    /// <param name="inputs">Values for the input placeholders of the started commands. Null means no values.</param>
    /// <returns>Messages about refused or blocked starts, or null.</returns>
    /// <exception cref="ArgumentException">The group does not belong to this session.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="ct"/> was cancelled, or the restart was revoked by a Stop of a command below the group, a Stop of the
    /// group, a ForceKill, StopAll, ForceKillAll or Dispose, before it started anything. Nothing is started then. A revocation
    /// that arrives while the group is being started stops what was started and throws too.
    /// </exception>
    public async Task<string?> RestartAsync(
        GroupEntry group,
        IReadOnlyDictionary<string, string>? inputs = null,
        CancellationToken ct = default)
    {
        return await RunRestartAsync(PrepareRestart(group, inputs, ct));
    }

    /// <summary>
    /// First half of <see cref="RestartAsync(GroupEntry, IReadOnlyDictionary{string, string}?, CancellationToken)"/>: registers a
    /// restart of the group's commands as they are now, as pending, without stopping or starting anything. Revocation works
    /// as for <see cref="PrepareRestart(IReadOnlyList{CommandEntry}, IReadOnlyDictionary{string, string}?, CancellationToken)"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The group does not belong to this session.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was already cancelled.</exception>
    public PreparedRestart PrepareRestart(
        GroupEntry group,
        IReadOnlyDictionary<string, string>? inputs = null,
        CancellationToken ct = default)
    {
        EnsureOwned(group);
        ct.ThrowIfCancellationRequested();
        IReadOnlyDictionary<string, string> values = Freeze(inputs);

        lock (_sync)
        {
            RestartOperation op = RegisterRestartLocked(group, new HashSet<CommandEntry>(group.AllCommands), commandsMustStayInGroup: false, ct);
            return new PreparedRestart(this, op, null, Array.Empty<SequenceRun>(), values);
        }
    }

    /// <summary>
    /// Second half of a restart: runs a restart made by <see cref="PrepareRestart(IReadOnlyList{CommandEntry}, IReadOnlyDictionary{string, string}?, CancellationToken)"/>
    /// or <see cref="PrepareRestart(GroupEntry, IReadOnlyDictionary{string, string}?, CancellationToken)"/>. Stops what must stop,
    /// waits for it, and starts what can start, as <see cref="RestartAsync(IReadOnlyList{CommandEntry}, IReadOnlyDictionary{string, string}?, CancellationToken)"/>
    /// does. Call it once per prepared restart, from any thread, and not on a thread that must not wait.
    /// </summary>
    /// <returns>Messages about refused or blocked starts, or null. Null for a prepared restart with nothing to do.</returns>
    /// <exception cref="ArgumentException">The prepared restart was made by another session.</exception>
    /// <exception cref="InvalidOperationException">The prepared restart has already been run.</exception>
    /// <exception cref="OperationCanceledException">
    /// The restart was revoked (Stop, ForceKill, StopAll, ForceKillAll or Dispose) or its caller's token was cancelled after it was
    /// prepared, before it started anything. Nothing is started then. A revocation that arrives while the restart starts
    /// things stops what was started and throws too.
    /// </exception>
    public async Task<string?> RunRestartAsync(PreparedRestart prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (!ReferenceEquals(prepared.Owner, this))
        {
            throw new ArgumentException("The prepared restart was made by another session.", nameof(prepared));
        }

        if (!prepared.Claim())
        {
            throw new InvalidOperationException("The prepared restart has already been run.");
        }

        RestartOperation? op = prepared.Operation;
        if (op is null)
        {
            return null;
        }

        try
        {
            // Nothing is stopped or started by a restart that was revoked or made stale while it waited.
            lock (_sync)
            {
                ThrowIfRevokedLocked(op);
            }

            return prepared.Targets is null
                ? await RunGroupRestartAsync(op, prepared.Values)
                : await RunCommandRestartAsync(op, prepared.Targets, prepared.Affected, prepared.Values);
        }
        finally
        {
            EndRestart(op);
        }
    }

    /// <summary>The body of a restart of commands, after it was registered. Ends the restart's registration only in the caller.</summary>
    private async Task<string?> RunCommandRestartAsync(
        RestartOperation op,
        IReadOnlyList<CommandEntry> targets,
        IReadOnlyList<SequenceRun> affected,
        IReadOnlyDictionary<string, string> values)
    {
        CancelInnermostRuns(targets);
        await Task.WhenAll(affected.Select(run => run.Result)).WaitAsync(op.Token);

        List<CommandEntry> running;
        lock (_sync)
        {
            running = targets.Where(IsRunningOrStopping).ToList();
        }

        await StopAndAwaitExitAsync(running, op);

        var startable = new List<CommandEntry>();
        var blocked = new List<string>();
        lock (_sync)
        {
            ThrowIfRevokedLocked(op);
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
        }

        var messages = new List<string>();
        var starts = new RestartStarts();
        if (startable.Count > 0)
        {
            string? message = StartCommands(op.Group, startable, values, op, starts);
            if (message is not null)
            {
                messages.Add(message);
            }
        }

        await AwaitFirstLaunchAsync(op, starts);

        if (blocked.Count > 0)
        {
            messages.Add($"Cannot start {string.Join(", ", blocked)}: leftover processes from an earlier run are still running.");
        }

        return messages.Count == 0 ? null : string.Join(" ", messages);
    }

    /// <summary>The body of a restart of a group, after it was registered.</summary>
    private async Task<string?> RunGroupRestartAsync(RestartOperation op, IReadOnlyDictionary<string, string> values)
    {
        GroupEntry group = op.Group;
        List<SequenceRun> cancelled = CancelRunsFor(group);
        await Task.WhenAll(cancelled.Select(run => run.Result)).WaitAsync(op.Token);

        List<CommandEntry> running;
        lock (_sync)
        {
            running = group.AllCommands.Where(entry => op.Commands.Contains(entry) && IsRunningOrStopping(entry)).ToList();
        }

        await StopAndAwaitExitAsync(running, op);

        // Leftovers are checked before starting: a command started below is not a blocked one.
        List<string> blocked;
        lock (_sync)
        {
            ThrowIfRevokedLocked(op);
            blocked = group.AllCommands
                .Where(entry => op.Commands.Contains(entry) && !entry.Process.CanStart)
                .Select(entry => $"'{entry.Spec.Name}'")
                .ToList();
        }

        var messages = new List<string>();
        var starts = new RestartStarts();
        string? message = StartGroup(group, values, op, starts);
        if (message is not null)
        {
            messages.Add(message);
        }

        await AwaitFirstLaunchAsync(op, starts);

        if (blocked.Count > 0)
        {
            messages.Add($"Cannot start {string.Join(", ", blocked)}: leftover processes from an earlier run are still running.");
        }

        return messages.Count == 0 ? null : string.Join(" ", messages);
    }

    /// <summary>
    /// The inputs that <see cref="Activate(IReadOnlyList{CommandEntry}, IReadOnlyDictionary{string, string}?)"/> would need for
    /// these targets: none when it would only kill, or would start nothing. Empty when no input is needed.
    /// </summary>
    /// <exception cref="ArgumentException">The targets are not all in the same group, or are not distinct commands in file order.</exception>
    public IReadOnlyList<InputPlaceholders.InputRequest> InputsForActivate(IReadOnlyList<CommandEntry> targets) =>
        PrepareActivate(targets).Requests;

    /// <summary>
    /// The inputs that <see cref="Activate(GroupEntry, IReadOnlyDictionary{string, string}?)"/> would need for this group: none
    /// when it would only kill, or would be refused. Empty when no input is needed.
    /// </summary>
    /// <exception cref="ArgumentException">The group does not belong to this session.</exception>
    public IReadOnlyList<InputPlaceholders.InputRequest> InputsForActivate(GroupEntry group) =>
        PrepareActivate(group).Requests;

    /// <summary>
    /// The inputs that <see cref="RestartAsync(IReadOnlyList{CommandEntry}, IReadOnlyDictionary{string, string}?, CancellationToken)"/>
    /// would need for these targets. Restart stops the running targets first, so targets that are running or stopping are
    /// included as if they would start; whether they do depends on their exit. Empty when the restart's start would be refused,
    /// allowing for the runs the restart cancels and the commands it stops. Empty when no input is needed.
    /// </summary>
    /// <exception cref="ArgumentException">The targets are not all in the same group, or are not distinct commands in file order.</exception>
    public IReadOnlyList<InputPlaceholders.InputRequest> InputsForRestart(IReadOnlyList<CommandEntry> targets)
    {
        GroupEntry? group = GroupOf(targets);
        if (group is null)
        {
            return Array.Empty<InputPlaceholders.InputRequest>();
        }

        List<CommandEntry> willStart = targets.Where(WillStartAfterStop).ToList();
        if (willStart.Count == 0)
        {
            return NoRequests;
        }

        bool refused;
        lock (_sync)
        {
            List<SequenceRun> affected = ActiveRuns().Where(run => targets.Any(target => run.Contains(target))).ToList();
            var view = new RestartView(
                affected,
                new HashSet<CommandEntry>(targets.Where(IsRunningOrStopping)));
            refused = CommandsRefusal(group, willStart, view) is not null;
        }

        return refused ? NoRequests : RequestsFor(willStart);
    }

    /// <summary>
    /// The inputs that <see cref="RestartAsync(GroupEntry, IReadOnlyDictionary{string, string}?, CancellationToken)"/> would need for
    /// this group. Running and stopping commands below it are included as in <see cref="InputsForRestart(IReadOnlyList{CommandEntry})"/>.
    /// Empty when the restart's start would be refused, allowing for the runs the restart cancels and the commands it stops.
    /// </summary>
    /// <exception cref="ArgumentException">The group does not belong to this session.</exception>
    public IReadOnlyList<InputPlaceholders.InputRequest> InputsForRestart(GroupEntry group)
    {
        EnsureOwned(group);
        List<PlanNode> children;
        bool refused = false;
        lock (_sync)
        {
            children = PlanChildren(group, WillStartAfterStop);
            if (children.Count > 0)
            {
                var view = new RestartView(
                    RunsCancelledFor(group),
                    new HashSet<CommandEntry>(group.AllCommands.Where(IsRunningOrStopping)));
                refused = GroupRefusal(group, children, view) is not null;
            }
        }

        return refused ? NoRequests : RequestsFor(CollectCommands(children));
    }

    /// <summary>Toggles the log file of a command. Does not change the spec or the dirty flag.</summary>
    public void ToggleLog(CommandEntry entry)
    {
        string? logPath = null;
        lock (_sync)
        {
            EnsureOwned(entry);
            if (!entry.Process.Output.IsLogging)
            {
                logPath = ResolveLogPath(entry);
            }
        }

        if (logPath is null)
        {
            entry.Process.Output.DisableLog();
        }
        else
        {
            entry.Process.Output.EnableLog(logPath);
        }

        RaiseEntryChanged(entry);
    }

    /// <summary>All commands that are running, stopping, or have leftover processes.</summary>
    public IReadOnlyList<CommandEntry> ActiveEntries
    {
        get
        {
            lock (_sync)
            {
                return AllEntries.Where(e => e.IsActive).ToList();
            }
        }
    }

    /// <summary>
    /// Quit: revokes all pending restarts (they complete with <see cref="OperationCanceledException"/> and start nothing), cancels
    /// all runs and gracefully stops all running commands (in the background).
    /// </summary>
    public void StopAll()
    {
        RevokeRestarts(_ => true, force: false);
        CancelAllRuns();
        List<CommandEntry> running;
        lock (_sync)
        {
            running = AllEntries.Where(IsRunningOrStopping).ToList();
        }

        foreach (CommandEntry entry in running)
        {
            Task.Run(() => entry.Process.RequestStop());
        }
    }

    /// <summary>
    /// Quit: revokes all pending restarts (they complete with <see cref="OperationCanceledException"/> and start nothing), cancels
    /// all runs and force-kills all active commands.
    /// </summary>
    public void ForceKillAll()
    {
        RevokeRestarts(_ => true, force: true);
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
    /// Starts one command: substitutes the input values into a copy of its arguments, enables its log file if configured, then
    /// launches it with the launch spec of its ancestors. Returns false, with a system line in the output, if the launch could
    /// not be prepared, including when an input has no usable value. Nothing is launched or logged in that case.
    /// </summary>
    internal bool StartEntry(CommandEntry entry, IReadOnlyDictionary<string, string> inputs)
    {
        try
        {
            // The tree is read under the lock; the log file and the process are touched after it is released.
            LaunchSpec launch;
            string? logPath = null;
            lock (_sync)
            {
                CommandSpec spec = entry.Spec;
                launch = LaunchSpec.From(entry.Group.SpecChain(), spec, entry.Source.Dir) with
                {
                    Args = InputPlaceholders.Substitute(spec.Args, inputs),
                };

                if (spec.Log.Enabled)
                {
                    logPath = ResolveLogPath(entry);
                }
            }

            if (logPath is not null)
            {
                entry.Process.Output.EnableLog(logPath);
            }

            entry.Process.Start(launch);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ObjectDisposedException or NotSupportedException)
        {
            entry.Process.Output.WriteSystem($"failed to start: {ex.Message}");
            return false;
        }
    }

    private string ResolveLogPath(CommandEntry entry) =>
        ConfigPaths.ResolveLogPath(entry.Group.SpecChain(), entry.Spec, entry.Source.LogsDir);

    /// <summary>
    /// Starts the given commands of a group: in sequence for a sequential group, otherwise each at once. Refuses, with a
    /// message, when the group already has a sequence, a command is part of a running sequence, or a command of a sequential
    /// group at or above it is still running (see <see cref="CommandsRefusal"/>). With a restart operation,
    /// the restart is checked for staleness before each launch: when it fails, what this batch started is stopped again and
    /// <see cref="OperationCanceledException"/> is thrown. A run started for the restart is recorded in <paramref name="starts"/>.
    /// </summary>
    private string? StartCommands(
        GroupEntry group,
        IReadOnlyList<CommandEntry> startable,
        IReadOnlyDictionary<string, string> inputs,
        RestartOperation? op = null,
        RestartStarts? starts = null)
    {
        if (group.Mode == GroupMode.Sequential)
        {
            return StartSequence(group, startable, inputs, op, starts);
        }

        lock (_sync)
        {
            ThrowIfRevokedLocked(op);
            string? refusal = CommandsRefusal(group, startable);
            if (refusal is not null)
            {
                return refusal;
            }
        }

        var started = new List<CommandEntry>();
        bool abandoned = false;
        foreach (CommandEntry entry in startable)
        {
            if (IsStale(op))
            {
                abandoned = true;
                break; // FinishRestartStarts stops what was started and throws
            }

            if (StartEntry(entry, inputs))
            {
                started.Add(entry);
            }
        }

        if (op is not null)
        {
            FinishRestartStarts(op, started, Array.Empty<SequenceRun>(), abandoned);
        }

        return null;
    }

    /// <summary>
    /// Creates a sequence over the commands and starts it in the background. Returns a message if one cannot start. With a restart
    /// operation, the restart is taken out of the pending set, and the run is handed over to it (see <see cref="HandOver"/>).
    /// </summary>
    private string? StartSequence(
        GroupEntry group,
        IReadOnlyList<CommandEntry> entries,
        IReadOnlyDictionary<string, string> inputs,
        RestartOperation? op,
        RestartStarts? starts)
    {
        var run = new SequenceRun(this, group, entries.Select(e => new PlanNode(e)).ToList(), inputs);

        lock (_sync)
        {
            ThrowIfRevokedLocked(op);
            string? refusal = CommandsRefusal(group, entries);
            if (refusal is not null)
            {
                return refusal;
            }

            Register(run);
            UnregisterRestartLocked(op);
        }

        HandOver(run, op, starts);
        return null;
    }

    /// <summary>
    /// Starts a registered run. With a restart operation, the caller's token cancels the run until its first command launches
    /// (see <see cref="SequenceRun.WatchRestartUntilLaunch"/>), and the restart is given the run to wait for. The run is started
    /// outside the lock.
    /// </summary>
    private void HandOver(SequenceRun run, RestartOperation? op, RestartStarts? starts)
    {
        if (op is not null)
        {
            run.WatchRestartUntilLaunch(op.Token);
            starts?.Runs.Add(run);
        }

        Launch(run);
    }

    /// <summary>
    /// Runs a group (see <see cref="Activate(GroupEntry, IReadOnlyDictionary{string, string}?)"/>) without the kill step. Returns a message when
    /// it is refused. With a restart operation, the restart's staleness is checked before the plan starts, and what it started is recorded in
    /// <paramref name="starts"/>. With <paramref name="only"/>, just those commands may start.
    /// </summary>
    private string? StartGroup(
        GroupEntry group,
        IReadOnlyDictionary<string, string> inputs,
        RestartOperation? op = null,
        RestartStarts? starts = null,
        IReadOnlySet<CommandEntry>? only = null)
    {
        SequenceRun? run = null;
        List<PlanNode> children;
        lock (_sync)
        {
            ThrowIfRevokedLocked(op);
            children = PlanChildren(
                group,
                e => e.Process.CanStart && (op is null || op.Commands.Contains(e)) && (only is null || only.Contains(e)));
            if (children.Count == 0)
            {
                return null;
            }

            string? refusal = GroupRefusal(group, children);
            if (refusal is not null)
            {
                return refusal;
            }

            if (group.Mode == GroupMode.Sequential)
            {
                run = new SequenceRun(this, group, children, inputs);
                Register(run);
                UnregisterRestartLocked(op);
            }
        }

        if (run is not null)
        {
            HandOver(run, op, starts);
            return null;
        }

        // Untracked: the branches run on their own. A restart waits only for the first launch of the runs it created.
        var createdRuns = new List<SequenceRun>();
        StartBranches(children, owner: null, inputs, createdRuns, op);
        if (op is not null)
        {
            var planned = new List<CommandEntry>();
            foreach (PlanNode child in children)
            {
                child.CollectCommands(planned);
            }

            starts?.Commands.AddRange(planned);
            starts?.Runs.AddRange(createdRuns);
            FinishRestartStarts(op, planned, createdRuns);
        }

        return null;
    }

    /// <summary>
    /// The plan of a group's startable items: commands that <paramref name="startable"/> accepts, and sub-groups that still have
    /// something to start. Caller holds <c>_sync</c>.
    /// </summary>
    private static List<PlanNode> PlanChildren(GroupEntry group, Func<CommandEntry, bool> startable)
    {
        var nodes = new List<PlanNode>();
        foreach (ItemEntry item in group.Items)
        {
            if (item is CommandEntry command)
            {
                if (startable(command))
                {
                    nodes.Add(new PlanNode(command));
                }
            }
            else if (item is GroupEntry child)
            {
                List<PlanNode> children = PlanChildren(child, startable);
                if (children.Count > 0)
                {
                    nodes.Add(new PlanNode(child, children));
                }
            }
        }

        return nodes;
    }

    /// <summary>
    /// The reason a plan cannot start: a sequential group in it already has a run, a sequential group in it has a command that
    /// is still running or stopping, or a command in it is busy. Null when it can. With a <paramref name="view"/>, the runs it
    /// ends and the commands it stops are not counted (see <see cref="CommandsRefusal"/>). Caller holds <c>_sync</c>.
    /// </summary>
    private string? RefusalFor(IReadOnlyList<PlanNode> nodes, RestartView? view = null)
    {
        foreach (PlanNode node in nodes)
        {
            if (node.Command is { } command)
            {
                if (IsBusyCommand(command, view))
                {
                    return BusyMessage(command);
                }
            }
            else if (node.Group is { } group)
            {
                if (group.Mode == GroupMode.Sequential && ActiveSequenceOf(group, view) is not null)
                {
                    return $"A sequence is already running in group '{group.Name}'.";
                }

                if (RunningRefusalIn(group, target: null, view) is { } running)
                {
                    return running;
                }

                if (IsReservedSequentialGroup(group, view))
                {
                    return $"Group '{group.Name}' is already part of a running sequence.";
                }

                string? nested = RefusalFor(node.Children, view);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The reason <see cref="StartCommands"/> would refuse these commands, or null: a sequential group already has a run, a
    /// command is part of a running sequence, or a command of a sequential group at or above the group is still running or
    /// stopping. With a <paramref name="view"/> (a restart's prediction), the runs it ends do not count, a queued command only
    /// counts when no run it ends contains it, and a command it stops does not block. Caller holds <c>_sync</c>.
    /// </summary>
    private string? CommandsRefusal(GroupEntry group, IReadOnlyList<CommandEntry> startable, RestartView? view = null)
    {
        if (group.Mode == GroupMode.Sequential && ActiveSequenceOf(group, view) is not null)
        {
            return $"A sequence is already running in group '{group.Name}'.";
        }

        CommandEntry? busy = startable.FirstOrDefault(entry => IsBusyCommand(entry, view));
        if (busy is not null)
        {
            return BusyMessage(busy);
        }

        return startable.Count == 0 ? null : RunningRefusalAbove(group, startable[0].Spec.Name, view);
    }

    /// <summary>
    /// The reason <see cref="StartGroup"/> would refuse the plan of a group, or null: a sequential group already has a run, a
    /// group is part of a running sequence, a sequential group at or above the group has a command still running or stopping,
    /// or the plan is refused by <see cref="RefusalFor"/>. With a <paramref name="view"/>, as for <see cref="CommandsRefusal"/>.
    /// Caller holds <c>_sync</c>.
    /// </summary>
    private string? GroupRefusal(GroupEntry group, IReadOnlyList<PlanNode> children, RestartView? view = null)
    {
        if (group.Mode == GroupMode.Sequential && ActiveSequenceOf(group, view) is not null)
        {
            return $"A sequence is already running in group '{group.Name}'.";
        }

        if (IsReservedSequentialGroup(group, view))
        {
            return $"Group '{group.Name}' is already part of a running sequence.";
        }

        if (RunningRefusalAbove(group, target: null, view) is { } running)
        {
            return running;
        }

        return RefusalFor(children, view);
    }

    /// <summary>
    /// The reason a start in <paramref name="group"/> is refused because a sequential group at or above it has a command that is
    /// running or stopping, or null. Sequential groups run one command at a time, so a command that a cancelled run left running
    /// blocks new starts in its group until it has exited. <paramref name="target"/> names the command being started; null for a group.
    /// <paramref name="view"/> is passed on to <see cref="RunningRefusalIn"/>. Caller holds <c>_sync</c>.
    /// </summary>
    private static string? RunningRefusalAbove(GroupEntry group, string? target, RestartView? view = null)
    {
        for (GroupEntry? ancestor = group; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (RunningRefusalIn(ancestor, target, view) is { } refusal)
            {
                return refusal;
            }
        }

        return null;
    }

    /// <summary>
    /// The reason a new run of <paramref name="group"/> would be refused because one of its commands is running or stopping, or
    /// null. Commands in its sub-groups count too: they are steps of the group's run. Null for a parallel group. Only the group's
    /// own subtree is looked at, not its ancestors, so that a run's steps that are starting together do not block each other.
    /// With a <paramref name="view"/>, commands it stops do not count. Caller holds <c>_sync</c>.
    /// </summary>
    private static string? RunningRefusalIn(GroupEntry group, string? target, RestartView? view = null)
    {
        if (group.Mode != GroupMode.Sequential)
        {
            return null;
        }

        CommandEntry? running = group.AllCommands.FirstOrDefault(
            entry => IsRunningOrStopping(entry) && (view is null || !view.Stopped.Contains(entry)));
        if (running is null)
        {
            return null;
        }

        string blocker = $"'{running.Spec.Name}' in sequential group '{group.Name}' is still running.";
        return target is null ? $"Cannot start: {blocker}" : $"Cannot start '{target}': {blocker}";
    }

    /// <summary>
    /// Registers a sub-run on its group and starts it. Returns the reason it cannot start, or null when it did: the group has an
    /// active run, or a command below it is running or stopping (see <see cref="RunningRefusalIn"/>). With a restart operation, the
    /// caller's token cancels the run until its first command launches (see <see cref="HandOver"/>).
    /// </summary>
    internal string? TryStart(SequenceRun run, RestartOperation? op = null)
    {
        lock (_sync)
        {
            if (run.Group.ActiveSequence is not null)
            {
                return $"A sequence is already running in group '{run.Group.Name}'.";
            }

            if (RunningRefusalIn(run.Group, target: null) is { } running)
            {
                return running;
            }

            Register(run);
        }

        if (op is not null)
        {
            run.WatchRestartUntilLaunch(op.Token);
        }

        Launch(run);
        return null;
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

    /// <summary>
    /// Starts a registered run in the background. Events are raised outside the lock. Every caller that registers a run calls
    /// this, and it starts the run even if a handler throws while the planned commands are announced, so a registered run is
    /// never left without a task. The run's <see cref="SequenceRun.Result"/> settles only after <see cref="RunAsync"/> has
    /// returned, that is after the run has been removed from its group.
    /// </summary>
    private void Launch(SequenceRun run)
    {
        try
        {
            foreach (CommandEntry entry in run.PlannedCommands)
            {
                RaiseEntryChanged(entry);
            }
        }
        finally
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    run.SettleResult(await run.RunAsync().ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    run.FailResult(ex);
                }
            });
        }
    }

    /// <summary>
    /// Starts the branches of a parallel group's planned children now: a command is started, a sequential group becomes a
    /// run, and a parallel group recurses. Each branch completes with true on success. With an <paramref name="owner"/>
    /// run, a branch started after the owner was cancelled is stopped as the owner would stop it. With a restart operation
    /// <paramref name="op"/>, no further branch is started once it is revoked or its caller cancelled it.
    /// </summary>
    internal List<Task<bool>> StartBranches(
        IReadOnlyList<PlanNode> children,
        SequenceRun? owner,
        IReadOnlyDictionary<string, string> inputs,
        List<SequenceRun>? createdRuns = null,
        RestartOperation? op = null)
    {
        var branches = new List<Task<bool>>();
        foreach (PlanNode child in children)
        {
            if (IsStale(op))
            {
                Unqueue(child); // not started: the restart that planned it was revoked or made stale
                continue;
            }

            if (child.Command is { } command)
            {
                branches.Add(StartBranchCommand(command, owner, inputs));
            }
            else if (child.Group is { } group)
            {
                if (group.Mode == GroupMode.Sequential)
                {
                    var run = new SequenceRun(this, group, child.Children, inputs, owner);
                    string? refusal = TryStart(run, op);
                    if (refusal is null)
                    {
                        createdRuns?.Add(run);
                        if (owner is { IsCancelled: true })
                        {
                            run.Cancel(owner.IsForced);
                        }

                        branches.Add(run.Result);
                    }
                    else
                    {
                        Unqueue(child);
                        WriteRefusal(child, refusal);
                        branches.Add(Task.FromResult(false));
                    }
                }
                else
                {
                    branches.Add(AllSucceedAsync(StartBranches(child.Children, owner, inputs, createdRuns, op)));
                }
            }
        }

        return branches;
    }

    /// <summary>
    /// Starts a branch command and completes when its leader exits: true when <see cref="Succeeded"/>. A branch outside any
    /// run (<paramref name="owner"/> is null) writes no "sequence cancelled" line.
    /// </summary>
    private Task<bool> StartBranchCommand(CommandEntry entry, SequenceRun? owner, IReadOnlyDictionary<string, string> inputs)
    {
        entry.IsQueued = false;
        if (owner is { IsCancelled: true })
        {
            return Task.FromResult(false);
        }

        if (!entry.Process.CanStart)
        {
            if (owner is not null)
            {
                entry.Process.Output.WriteSystem("sequence cancelled: command cannot start");
            }

            return Task.FromResult(false);
        }

        if (owner is not null && !owner.TryBeginLaunch())
        {
            return Task.FromResult(false);
        }

        if (!StartEntry(entry, inputs) || entry.Process.Snapshot.State == ProcessState.FailedToStart)
        {
            return Task.FromResult(false);
        }

        owner?.StopIfCancelled(entry);
        return WaitBranchAsync(entry, owner is not null);
    }

    private static async Task<bool> WaitBranchAsync(CommandEntry entry, bool inRun)
    {
        await entry.Process.WaitForExitAsync().ConfigureAwait(false);

        ProcessSnapshot snapshot = entry.Process.Snapshot;
        if (Succeeded(snapshot))
        {
            return true;
        }

        if (inRun)
        {
            entry.Process.Output.WriteSystem(CancelledMessage(snapshot));
        }

        return false;
    }

    /// <summary>
    /// True when a finished run counts as a success: it was not stopped and its leader exited with code 0. A stopped run
    /// ends its sequence whatever its exit code, because such codes (130, 255, 0xC000013A, ...) are not verdicts.
    /// </summary>
    internal static bool Succeeded(ProcessSnapshot snapshot) => !snapshot.StopRequested && snapshot.ExitCode == 0;

    /// <summary>The "sequence cancelled" line for a run that did not <see cref="Succeeded"/>.</summary>
    internal static string CancelledMessage(ProcessSnapshot snapshot) => snapshot.StopRequested
        ? "sequence cancelled: stopped"
        : $"sequence cancelled: exit code {snapshot.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}";

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

    /// <summary>Adds a restart to the pending set. Caller holds <c>_sync</c>.</summary>
    private RestartOperation RegisterRestartLocked(GroupEntry group, HashSet<CommandEntry> commands, bool commandsMustStayInGroup, CancellationToken ct)
    {
        var op = new RestartOperation(group, commands, commandsMustStayInGroup, ct);
        _restarts.Add(op);
        return op;
    }

    /// <summary>Takes a restart out of the pending set, so that no later revocation finds it. Caller holds <c>_sync</c>.</summary>
    private void UnregisterRestartLocked(RestartOperation? op)
    {
        if (op is not null)
        {
            _restarts.Remove(op);
        }
    }

    /// <summary>
    /// Ends a restart: takes it out of the pending set, and disposes its source unless a revocation still has to cancel it.
    /// Safe to call more than once.
    /// </summary>
    private void EndRestart(RestartOperation op)
    {
        bool dispose;
        lock (_sync)
        {
            _restarts.Remove(op);
            dispose = op.Finish();
        }

        if (dispose)
        {
            op.Dispose();
        }
    }

    /// <summary>
    /// Revokes the pending restarts that match, so that they start nothing. The matches are marked under <c>_sync</c>; the tokens
    /// are cancelled after it is released, since cancelling runs callbacks. Call before cancelling runs or stopping processes,
    /// so that a restart cannot start what they stop.
    /// </summary>
    private void RevokeRestarts(Func<RestartOperation, bool> matches, bool force)
    {
        var revoked = new List<RestartOperation>();
        lock (_sync)
        {
            foreach (RestartOperation op in _restarts)
            {
                if (matches(op))
                {
                    op.Revoke(force);
                    revoked.Add(op);
                }
            }
        }

        foreach (RestartOperation op in revoked)
        {
            op.CancelToken();
            bool dispose;
            lock (_sync)
            {
                dispose = op.EndCancel();
            }

            if (dispose)
            {
                op.Dispose();
            }
        }
    }

    /// <summary>
    /// Throws if the restart may not start anything. A restart is stale when it was revoked, its caller cancelled it, or its group
    /// was removed; a restart of commands also when a target was moved out of the group or removed while it waited; a group restart
    /// also when one of its commands was moved out of the group's subtree or removed. A null operation never throws. Caller holds
    /// <c>_sync</c>, and registers or starts in the same lock section, so that edits cannot intervene.
    /// </summary>
    private void ThrowIfRevokedLocked(RestartOperation? op)
    {
        if (IsStaleLocked(op))
        {
            throw new OperationCanceledException(op!.Token);
        }
    }

    /// <summary>The stale check of <see cref="ThrowIfRevokedLocked"/>. False for a null operation. Caller holds <c>_sync</c>.</summary>
    private bool IsStaleLocked(RestartOperation? op)
    {
        if (op is null)
        {
            return false;
        }

        if (IsRevokedLocked(op) || !IsAttached(op.Group))
        {
            return true;
        }

        return op.CommandsMustStayInGroup
            ? op.Commands.Any(entry => entry.IsRemoved || !ReferenceEquals(entry.Group, op.Group) || !op.Group.HasItem(entry))
            : op.Commands.Any(entry => !IsInSubtreeLocked(op.Group, entry));
    }

    /// <summary>True if the command is not removed and is linked from the group down to it. Caller holds <c>_sync</c>.</summary>
    private static bool IsInSubtreeLocked(GroupEntry root, CommandEntry entry)
    {
        if (entry.IsRemoved)
        {
            return false;
        }

        ItemEntry child = entry;
        while (child.Parent is { } parent)
        {
            if (!parent.HasItem(child))
            {
                return false;
            }

            if (ReferenceEquals(parent, root))
            {
                return true;
            }

            child = parent;
        }

        return false;
    }

    /// <summary>True if the restart was revoked or its caller cancelled it. False for a null operation. Caller holds <c>_sync</c>.</summary>
    private static bool IsRevokedLocked(RestartOperation? op) =>
        op is not null && (op.Cancelled || op.Token.IsCancellationRequested);

    /// <summary>As <see cref="IsStaleLocked"/>, taking the lock. False for a null operation.</summary>
    private bool IsStale(RestartOperation? op)
    {
        if (op is null)
        {
            return false;
        }

        lock (_sync)
        {
            return IsStaleLocked(op);
        }
    }

    /// <summary>
    /// Sends the stop to each running command in the background, then waits for their leaders to exit. The stops are sent even
    /// if the restart is revoked meanwhile (a stop write can block on a full stdin pipe); only the waiting is cancellable.
    /// </summary>
    private static async Task StopAndAwaitExitAsync(IReadOnlyList<CommandEntry> running, RestartOperation op)
    {
        Task[] dispatches = running.Select(entry => Task.Run(() => entry.Process.RequestStop())).ToArray();
        // If the wait below is abandoned, a fault of a stop dispatch must still be observed rather than escape unobserved.
        Task dispatched = ObserveFaults(Task.WhenAll(dispatches));

        await dispatched.WaitAsync(op.Token);
        await Task.WhenAll(running.Select(entry => entry.Process.WaitForExitAsync(op.Token)));
    }

    /// <summary>
    /// Runs after a restart has started processes outside the lock. A revocation, the caller's cancellation or another change that makes
    /// the restart stale may have missed what was started after its own pass, so the processes started here and the runs created here
    /// are stopped as the revoker would stop them (a graceful stop is sent in the background, since an input stop can block on a full
    /// stdin pipe), and the restart throws. Otherwise nothing happens, unless <paramref name="abandoned"/> is set: a batch that gave up
    /// before its last start is undone and throws in any case. A revocation that arrives after this check finds the processes already
    /// started and stops them itself.
    /// </summary>
    private void FinishRestartStarts(
        RestartOperation op,
        IReadOnlyList<CommandEntry> started,
        IReadOnlyList<SequenceRun> createdRuns,
        bool abandoned = false)
    {
        bool stale;
        bool forced;
        lock (_sync)
        {
            stale = IsStaleLocked(op);
            forced = op.Forced;
        }

        if (!stale && !abandoned)
        {
            return;
        }

        foreach (SequenceRun run in createdRuns)
        {
            run.Cancel(forced);
        }

        foreach (CommandEntry entry in started)
        {
            StopAsRevoked(entry, forced);
        }

        foreach (SequenceRun run in createdRuns)
        {
            foreach (CommandEntry entry in run.PlannedCommands)
            {
                StopAsRevoked(entry, forced);
            }
        }

        throw new OperationCanceledException(op.Token);
    }

    /// <summary>
    /// Waits until each run that a restart handed over has launched its first command, or has ended without one. A run that the
    /// restart's token cancelled before that launched nothing; then the restart undoes what it started and throws, as
    /// <see cref="FinishRestartStarts"/> does. Once a first command has launched, the token no longer reaches the run.
    /// </summary>
    private async Task AwaitFirstLaunchAsync(RestartOperation op, RestartStarts starts)
    {
        if (starts.Runs.Count == 0)
        {
            return;
        }

        await Task.WhenAll(starts.Runs.Select(run => run.FirstLaunch));
        if (!starts.Runs.Any(run => run.CancelledByRestartToken))
        {
            return;
        }

        List<CommandEntry> planned = starts.Commands
            .Concat(starts.Runs.SelectMany(run => run.PlannedCommands))
            .Distinct()
            .ToList();
        FinishRestartStarts(op, planned, starts.Runs, abandoned: true);
    }

    /// <summary>What one restart started: the commands of its batch, and the runs it handed over.</summary>
    private sealed class RestartStarts
    {
        public List<CommandEntry> Commands { get; } = new();

        public List<SequenceRun> Runs { get; } = new();
    }

    /// <summary>
    /// Stops a command started by a revoked restart, as a Stop (or a ForceKill when forced) would. Mirrors SequenceRun.StopIfCancelled,
    /// except that the graceful stop is sent in the background and not waited for.
    /// </summary>
    private static void StopAsRevoked(CommandEntry entry, bool forced)
    {
        if (forced)
        {
            entry.Process.ForceKill();
        }
        else if (entry.Process.Snapshot.State == ProcessState.Running)
        {
            ObserveFaults(Task.Run(() => entry.Process.RequestStop()));
        }
    }

    /// <summary>Makes sure a fault of a task nobody awaits is observed rather than escaping as an unobserved exception.</summary>
    private static Task ObserveFaults(Task task)
    {
        _ = task.ContinueWith(
            t => { _ = t.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
        return task;
    }

    /// <summary>
    /// Every active run in the session, nested ones included, except those that <paramref name="view"/> ends. Caller holds <c>_sync</c>.
    /// </summary>
    private List<SequenceRun> ActiveRuns(RestartView? view = null) =>
        GroupEntry.Walk(_groups)
            .OfType<GroupEntry>()
            .Select(g => g.ActiveSequence)
            .OfType<SequenceRun>()
            .Where(run => view is null || !view.Ending.Contains(run))
            .ToList();

    /// <summary>The group's active run, unless <paramref name="view"/> ends it. Caller holds <c>_sync</c>.</summary>
    private static SequenceRun? ActiveSequenceOf(GroupEntry group, RestartView? view) =>
        group.ActiveSequence is { } run && (view is null || !view.Ending.Contains(run)) ? run : null;

    /// <summary>
    /// The innermost active run containing each target is cancelled. Innermost is the deepest group among the runs that
    /// contain the target; runs around it fail through their awaited steps. The runs are chosen under the lock and cancelled
    /// after it is released, since cancelling raises events.
    /// </summary>
    private List<SequenceRun> CancelInnermostRuns(IReadOnlyList<CommandEntry> targets)
    {
        var innermost = new List<SequenceRun>();
        lock (_sync)
        {
            List<SequenceRun> runs = ActiveRuns();
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
        }

        foreach (SequenceRun run in innermost)
        {
            run.Cancel();
        }

        return innermost;
    }

    /// <summary>
    /// Cancels the runs at or below the group, and the enclosing runs that have the group as a step. Returns the runs cancelled.
    /// The runs are chosen under the lock and cancelled after it is released.
    /// </summary>
    private List<SequenceRun> CancelRunsFor(GroupEntry group)
    {
        List<SequenceRun> runs;
        lock (_sync)
        {
            runs = RunsCancelledFor(group);
        }

        foreach (SequenceRun run in runs)
        {
            run.Cancel();
        }

        return runs;
    }

    /// <summary>
    /// The runs that <see cref="CancelRunsFor"/> cancels for the group: the active runs at or below it, and the enclosing runs that
    /// have it as a step. Caller holds <c>_sync</c>.
    /// </summary>
    private static List<SequenceRun> RunsCancelledFor(GroupEntry group)
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

        return runs;
    }

    private void CancelAllRuns(bool force = false)
    {
        List<SequenceRun> runs;
        lock (_sync)
        {
            runs = ActiveRuns();
        }

        foreach (SequenceRun run in runs)
        {
            run.Cancel(force);
        }
    }

    /// <summary>
    /// True if the command is queued, or is the current or a pending step of an active run. With a <paramref name="view"/>, a
    /// queued command does not count when a run it ends contains it, and runs it ends do not count. Caller holds <c>_sync</c>.
    /// </summary>
    private bool IsBusyCommand(CommandEntry entry, RestartView? view = null) =>
        (entry.IsQueued && (view is null || !view.Ending.Any(run => run.Contains(entry))))
        || ActiveRuns(view).Any(run => run.Contains(entry))
        || IsReservedSequentialGroup(entry.Group, view);

    /// <summary>
    /// Sequential groups planned by another run are reserved even for commands added after its plan was made. With a
    /// <paramref name="view"/>, runs it ends do not reserve anything. Caller holds <c>_sync</c>.
    /// </summary>
    private bool IsReservedSequentialGroup(GroupEntry group, RestartView? view = null) =>
        group.Mode == GroupMode.Sequential && ActiveRuns(view).Any(run => run.ContainsGroup(group));

    /// <summary>
    /// True if the group has an active run, is a step of one, contains a busy command, or contains a group that is busy.
    /// Caller holds <c>_sync</c>.
    /// </summary>
    private bool IsBusyGroup(GroupEntry group) =>
        group.ActiveSequence is not null
        || ActiveRuns().Any(run => run.ContainsGroup(group))
        || group.Items.Any(item => item is GroupEntry child ? IsBusyGroup(child) : IsBusyCommand((CommandEntry)item));

    private static string BusyMessage(CommandEntry entry) =>
        $"Cannot start '{entry.Spec.Name}': it is part of a running sequence.";

    /// <summary>
    /// The group of the targets. Throws when targets were removed, span several groups, or are not distinct commands in the
    /// order of their group's file order.
    /// </summary>
    private GroupEntry? GroupOf(IReadOnlyList<CommandEntry> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0)
        {
            return null;
        }

        lock (_sync)
        {
            // Validate the full request before callers can mutate process, queue, or log state.
            foreach (CommandEntry entry in targets)
            {
                EnsureOwned(entry);
            }

            GroupEntry group = targets[0].Group;
            foreach (CommandEntry entry in targets)
            {
                if (!ReferenceEquals(entry.Group, group))
                {
                    throw new ArgumentException("All targets must belong to the same group.", nameof(targets));
                }
            }

            // One pass over the group's commands in file order: each target must be found after the previous one. A repeated
            // target cannot be matched again, so it is rejected too.
            int next = 0;
            for (int position = 0; position < group.Commands.Count && next < targets.Count; position++)
            {
                if (ReferenceEquals(group.Commands[position], targets[next]))
                {
                    next++;
                }
            }

            if (next != targets.Count)
            {
                throw new ArgumentException("Targets must be distinct commands in file order.", nameof(targets));
            }

            return group;
        }
    }

    private static bool IsKillable(CommandEntry entry)
    {
        ProcessSnapshot snapshot = entry.Process.Snapshot;
        return snapshot.State == ProcessState.Stopping || (snapshot.State == ProcessState.Exited && snapshot.HasLeftovers);
    }

    private static bool IsRunningOrStopping(CommandEntry entry)
    {
        ProcessState state = entry.Process.Snapshot.State;
        return state is ProcessState.Running or ProcessState.Stopping;
    }

    /// <summary>True if a restart would start the command: it can start now, or it is running or stopping and is stopped first.</summary>
    private static bool WillStartAfterStop(CommandEntry entry) => entry.Process.CanStart || IsRunningOrStopping(entry);

    /// <summary>
    /// What a restart clears before it starts, for predicting its refusals: the runs it ends (they are not counted as active)
    /// and the commands it stops (running or stopping ones that do not block a start). Only the input prediction passes one;
    /// every other caller sees the session as it is.
    /// </summary>
    private sealed record RestartView(IReadOnlyCollection<SequenceRun> Ending, IReadOnlySet<CommandEntry> Stopped);

    private static readonly IReadOnlyDictionary<string, string> NoInputs = new Dictionary<string, string>();

    private static readonly IReadOnlyList<InputPlaceholders.InputRequest> NoRequests = Array.Empty<InputPlaceholders.InputRequest>();

    /// <summary>A private copy of the input values, so that a running action does not see later changes. Null means no values.</summary>
    private static IReadOnlyDictionary<string, string> Freeze(IReadOnlyDictionary<string, string>? inputs) =>
        inputs is null ? NoInputs : new Dictionary<string, string>(inputs);

    /// <summary>The distinct inputs the commands' arguments need, in first-seen order.</summary>
    private static IReadOnlyList<InputPlaceholders.InputRequest> RequestsFor(IEnumerable<CommandEntry> commands) =>
        InputPlaceholders.Requests(commands.Select(c => (IReadOnlyList<string>)c.Spec.Args).ToList());

    /// <summary>The commands planned at or below the steps, in order.</summary>
    private static List<CommandEntry> CollectCommands(IEnumerable<PlanNode> steps)
    {
        var commands = new List<CommandEntry>();
        foreach (PlanNode step in steps)
        {
            step.CollectCommands(commands);
        }

        return commands;
    }
}
