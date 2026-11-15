using Cmdr.Core.Config;
using Cmdr.Core.Processes;

namespace Cmdr.Core.Session;

/// <summary>
/// One run of a sequential group. Runs its planned steps in order: a command step starts the command and waits for its
/// leader; a sequential child group becomes a sub-run; a parallel child group starts its branches and fails as soon as one
/// of them fails. A failure or cancellation cancels the rest of this run and unqueues it. It never stops or kills processes
/// on its own: siblings of a failed branch keep running.
/// </summary>
/// <remarks>
/// Queue state is guarded by <c>_sync</c>; events are raised after it is released. Lock order: the session lock may be held
/// while taking a run lock, never the other way round.
/// </remarks>
internal sealed class SequenceRun
{
    private readonly Session _session;
    private readonly object _sync = new();
    private readonly List<PlanNode> _pending;
    private readonly List<CommandEntry> _planned;
    private readonly IReadOnlyDictionary<string, string> _inputs;
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<bool> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _cancelled;
    private bool _forced;
    private PlanNode? _current;
    private SequenceRun? _childRun;

    /// <param name="inputs">The input values of the action that started the run; every command step and sub-run gets them.</param>
    internal SequenceRun(Session session, GroupEntry group, IReadOnlyList<PlanNode> steps, IReadOnlyDictionary<string, string> inputs)
    {
        _session = session;
        Group = group;
        _pending = steps.ToList();
        _inputs = inputs;

        var planned = new List<CommandEntry>();
        foreach (PlanNode step in steps)
        {
            step.CollectCommands(planned);
        }

        _planned = planned;
    }

    /// <summary>The group this run belongs to.</summary>
    internal GroupEntry Group { get; }

    /// <summary>
    /// Completes with true when every step succeeded, false when the run failed or was cancelled, or faults if running it threw.
    /// It completes only after the run has left its group (<see cref="Session.FinishSequence"/>), so waiting for it never
    /// sees a run that is still registered. Settled by <see cref="SettleResult"/> or <see cref="FailResult"/> from
    /// <see cref="Session"/>'s launch, which is the only place a run is started.
    /// </summary>
    internal Task<bool> Result => _result.Task;

    /// <summary>Completes <see cref="Result"/> with the outcome of <see cref="RunAsync"/>. Called once, after the run has ended.</summary>
    internal void SettleResult(bool completed) => _result.TrySetResult(completed);

    /// <summary>Faults <see cref="Result"/> when <see cref="RunAsync"/> threw. Called once, after the run has ended.</summary>
    internal void FailResult(Exception exception) => _result.TrySetException(exception);

    /// <summary>Every command planned in this run, in order (including inside group steps).</summary>
    internal IReadOnlyList<CommandEntry> PlannedCommands => _planned;

    /// <summary>True once the run has been cancelled.</summary>
    internal bool IsCancelled
    {
        get
        {
            lock (_sync)
            {
                return _cancelled;
            }
        }
    }

    /// <summary>True if the run was cancelled with force, so processes it starts are force-killed.</summary>
    internal bool IsForced
    {
        get
        {
            lock (_sync)
            {
                return _forced;
            }
        }
    }

    /// <summary>True if the command is the current step or is planned in a step that has not started yet.</summary>
    internal bool Contains(CommandEntry entry)
    {
        lock (_sync)
        {
            return _current?.ContainsCommand(entry) == true || _pending.Any(step => step.ContainsCommand(entry));
        }
    }

    /// <summary>True if the group is the current step or a pending step, or is planned inside one.</summary>
    internal bool ContainsGroup(GroupEntry group)
    {
        lock (_sync)
        {
            return _current?.ContainsGroup(group) == true || _pending.Any(step => step.ContainsGroup(group));
        }
    }

    /// <summary>
    /// Cancels the run: pending steps are dropped and unqueued, and the wait for the current step ends. A sequential child
    /// run that is the current step is cancelled too. Processes are left alone; the caller decides whether to stop them.
    /// With <paramref name="force"/>, a process the run starts after this call is force-killed instead of stopped.
    /// </summary>
    internal void Cancel(bool force = false)
    {
        var unqueued = new List<CommandEntry>();
        SequenceRun? child;
        lock (_sync)
        {
            _forced |= force;
            if (_cancelled)
            {
                return;
            }

            _cancelled = true;
            foreach (PlanNode step in _pending)
            {
                step.CollectCommands(unqueued);
            }

            _pending.Clear();
            foreach (CommandEntry entry in unqueued)
            {
                entry.IsQueued = false;
            }

            child = _childRun;
        }

        _cts.Cancel();
        child?.Cancel(force);
        RaiseAll(unqueued);
    }

    /// <summary>Runs the steps. Called on a background task. Returns true when every step succeeded.</summary>
    internal async Task<bool> RunAsync()
    {
        bool completed = false;
        try
        {
            completed = await RunStepsAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelled while waiting for a step. The step's processes are left alone.
        }
        finally
        {
            Finish();
        }

        return completed;
    }

    /// <summary>Stops a command started by this run if the run was cancelled while it was being started.</summary>
    internal void StopIfCancelled(CommandEntry entry)
    {
        bool cancelled;
        bool forced;
        lock (_sync)
        {
            cancelled = _cancelled;
            forced = _forced;
        }

        if (!cancelled)
        {
            return;
        }

        // A Stop or quit that arrived while the entry was being started could not reach it yet.
        if (forced)
        {
            entry.Process.ForceKill();
        }
        else if (entry.Process.Snapshot.State == ProcessState.Running)
        {
            entry.Process.RequestStop();
        }
    }

    private async Task<bool> RunStepsAsync()
    {
        PlanNode? step;
        while ((step = Dequeue()) is not null)
        {
            if (step.Command is { } command)
            {
                _session.RaiseEntryChanged(command);
            }

            if (!await ExecuteAsync(step).ConfigureAwait(false))
            {
                Cancel();
                return false;
            }

            lock (_sync)
            {
                _current = null;
                _childRun = null;
            }
        }

        return !IsCancelled;
    }

    private async Task<bool> ExecuteAsync(PlanNode step)
    {
        if (step.Command is { } command)
        {
            return await RunCommandStepAsync(command).ConfigureAwait(false);
        }

        if (IsCancelled)
        {
            _session.Unqueue(step);
            return false;
        }

        GroupEntry group = step.Group!;
        if (group.Mode == GroupMode.Sequential)
        {
            var child = new SequenceRun(_session, group, step.Children, _inputs);
            if (!_session.TryStart(child))
            {
                _session.Unqueue(step);
                Session.WriteRefusal(step, $"A sequence is already running in group '{group.Name}'.");
                return false;
            }

            bool cancelledNow;
            lock (_sync)
            {
                _childRun = child;
                cancelledNow = _cancelled;
            }

            // A Cancel that raced with the start did not see the child yet.
            if (cancelledNow)
            {
                child.Cancel();
            }

            return await child.Result.ConfigureAwait(false);
        }

        // Parallel: all branches start now; the step fails as soon as one fails, and siblings are not cancelled.
        List<Task<bool>> branches = _session.StartBranches(step.Children, this, _inputs);
        return await Session.AllSucceedAsync(branches, _cts.Token).ConfigureAwait(false);
    }

    private async Task<bool> RunCommandStepAsync(CommandEntry entry)
    {
        if (IsCancelled)
        {
            return false;
        }

        if (!entry.Process.CanStart)
        {
            entry.Process.Output.WriteSystem("sequence cancelled: command cannot start");
            return false;
        }

        if (!_session.StartEntry(entry, _inputs) || entry.Process.Snapshot.State == ProcessState.FailedToStart)
        {
            return false;
        }

        StopIfCancelled(entry);

        await entry.Process.WaitForExitAsync(_cts.Token).ConfigureAwait(false);

        ProcessSnapshot snapshot = entry.Process.Snapshot;
        if (Session.Succeeded(snapshot))
        {
            return true;
        }

        entry.Process.Output.WriteSystem(Session.CancelledMessage(snapshot));
        return false;
    }

    /// <summary>Takes the next step off the queue and makes it the current one. Returns null when cancelled or empty.</summary>
    private PlanNode? Dequeue()
    {
        lock (_sync)
        {
            if (_cancelled || _pending.Count == 0)
            {
                _current = null;
                return null;
            }

            PlanNode step = _pending[0];
            _pending.RemoveAt(0);
            _current = step;
            if (step.Command is { } command)
            {
                command.IsQueued = false;
            }

            return step;
        }
    }

    /// <summary>Called once when the run ends: unqueues what is still pending and removes the run from its group.</summary>
    private void Finish()
    {
        lock (_sync)
        {
            var leftover = new List<CommandEntry>();
            foreach (PlanNode step in _pending)
            {
                step.CollectCommands(leftover);
            }

            _pending.Clear();
            foreach (CommandEntry entry in leftover)
            {
                entry.IsQueued = false;
            }

            _current = null;
            _childRun = null;
        }

        _session.FinishSequence(this);
    }

    private void RaiseAll(IEnumerable<CommandEntry> entries)
    {
        foreach (CommandEntry entry in entries)
        {
            _session.RaiseEntryChanged(entry);
        }
    }
}
