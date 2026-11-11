using Cmdr.Core.Config;
using Cmdr.Core.Processes;

namespace Cmdr.Core.Session;

public sealed partial class Session
{
    /// <summary>
    /// Enter: force-kills stopping commands and commands that exited with leftover processes, then starts the others
    /// that can start. Parallel groups start each command now; sequential groups start a sequence of them in file order.
    /// Running commands are left alone.
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

        var startable = new List<CommandEntry>();
        foreach (CommandEntry entry in targets)
        {
            ProcessSnapshot snapshot = entry.Process.Snapshot;
            if (snapshot.State == ProcessState.Stopping || (snapshot.State == ProcessState.Exited && snapshot.LeftoverCount > 0))
            {
                entry.Process.ForceKill();
            }
            else if (entry.Process.CanStart)
            {
                startable.Add(entry);
            }
        }

        if (startable.Count == 0)
        {
            return null;
        }

        if (group.Mode == GroupMode.Parallel)
        {
            foreach (CommandEntry entry in startable)
            {
                StartEntry(entry);
            }

            return null;
        }

        return StartSequence(group, startable);
    }

    /// <summary>
    /// Esc / Ctrl+C: if a target is part of the group's running sequence, cancels the sequence (the current command is
    /// stopped too). Gracefully stops the targets that are running or stopping. Stop signals are sent in the background.
    /// </summary>
    /// <exception cref="ArgumentException">The targets are not all in the same group.</exception>
    public string? Stop(IReadOnlyList<CommandEntry> targets)
    {
        GroupEntry? group = GroupOf(targets);
        if (group is null)
        {
            return null;
        }

        SequenceRun? run = group.ActiveSequence;
        if (run is not null && targets.Any(run.Contains))
        {
            run.Cancel();
        }

        foreach (CommandEntry entry in targets.Where(IsRunningOrStopping).ToList())
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
    /// F5: cancels the group's sequence if a target is part of it, stops the running or stopping targets and waits for
    /// their leaders to exit, then starts the targets that can start (in sequence for sequential groups).
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

        SequenceRun? run = group.ActiveSequence;
        if (run is not null && targets.Any(run.Contains))
        {
            run.Cancel();
            await run.Completion.WaitAsync(ct);
        }

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
            if (group.Mode == GroupMode.Parallel)
            {
                foreach (CommandEntry entry in startable)
                {
                    StartEntry(entry);
                }
            }
            else
            {
                string? busy = StartSequence(group, startable);
                if (busy is not null)
                {
                    messages.Add(busy);
                }
            }
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

    /// <summary>Quit: cancels all sequences and gracefully stops all running commands (in the background).</summary>
    public void StopAll()
    {
        CancelAllSequences();
        foreach (CommandEntry entry in AllEntries.Where(IsRunningOrStopping))
        {
            Task.Run(() => entry.Process.RequestStop());
        }
    }

    /// <summary>Quit: cancels all sequences and force-kills all active commands.</summary>
    public void ForceKillAll()
    {
        CancelAllSequences(force: true);
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
    /// Starts one command: enables its log file if configured, then launches it. Called by sequences on a background
    /// thread. Returns false, with a system line in the output, if the launch could not be prepared.
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

            entry.Process.Start(LaunchSpec.From(spec, ConfigDir));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ObjectDisposedException or NotSupportedException)
        {
            entry.Process.Output.WriteSystem($"failed to start: {ex.Message}");
            return false;
        }
    }

    private string ResolveLogPath(CommandEntry entry) =>
        ConfigPaths.ResolveLogPath(
            new GroupSpec { Name = entry.Group.Name, Description = entry.Group.Description, Mode = entry.Group.Mode },
            entry.Spec,
            LogsDir);

    /// <summary>Creates a sequence over the entries and starts it in the background. Returns a message if one is already running.</summary>
    private string? StartSequence(GroupEntry group, IReadOnlyList<CommandEntry> entries)
    {
        var run = new SequenceRun(this, group, entries);

        lock (_sync)
        {
            if (group.ActiveSequence is not null)
            {
                return $"A sequence is already running in group '{group.Name}'.";
            }

            foreach (CommandEntry entry in entries)
            {
                entry.IsQueued = true;
            }

            group.ActiveSequence = run;
        }

        foreach (CommandEntry entry in entries)
        {
            RaiseEntryChanged(entry);
        }

        run.Completion = Task.Run(() => run.RunAsync());
        return null;
    }

    /// <summary>Called by a sequence when it ends: clears the group's sequence and refreshes its entries.</summary>
    internal void FinishSequence(SequenceRun run)
    {
        lock (_sync)
        {
            if (ReferenceEquals(run.Group.ActiveSequence, run))
            {
                run.Group.ActiveSequence = null;
            }
        }

        foreach (CommandEntry entry in run.Entries)
        {
            RaiseEntryChanged(entry);
        }
    }

    private void CancelAllSequences(bool force = false)
    {
        foreach (GroupEntry group in _groups)
        {
            group.ActiveSequence?.Cancel(force);
        }
    }

    /// <summary>
    /// The group of the targets, or null when there are none. Throws when targets span several groups or were removed.
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

    private static bool IsRunningOrStopping(CommandEntry entry)
    {
        ProcessState state = entry.Process.Snapshot.State;
        return state is ProcessState.Running or ProcessState.Stopping;
    }
}
