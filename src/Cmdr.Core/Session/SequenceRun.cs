using Cmdr.Core.Processes;

namespace Cmdr.Core.Session;

/// <summary>
/// One run of a sequential group's queue. Starts each entry after the previous leader exits with code 0; a failure
/// to start, a non-zero exit or a cancellation ends the run. Never stops or kills processes on its own.
/// </summary>
/// <remarks>
/// Queue state is guarded by <c>_sync</c>; events are raised after it is released.
/// </remarks>
internal sealed class SequenceRun
{
    private readonly Session _session;
    private readonly object _sync = new();
    private readonly List<CommandEntry> _entries;
    private readonly List<CommandEntry> _queue;
    private readonly CancellationTokenSource _cts = new();
    private bool _cancelled;
    private bool _forced;
    private CommandEntry? _current;

    internal SequenceRun(Session session, GroupEntry group, IReadOnlyList<CommandEntry> entries)
    {
        _session = session;
        Group = group;
        _entries = entries.ToList();
        _queue = entries.ToList();
    }

    /// <summary>The group this sequence belongs to.</summary>
    internal GroupEntry Group { get; }

    /// <summary>Completes when the run has finished (after cancellation it finishes without waiting for processes).</summary>
    internal Task Completion { get; set; } = Task.CompletedTask;

    /// <summary>All entries this run was created with, in order.</summary>
    internal IReadOnlyList<CommandEntry> Entries => _entries;

    /// <summary>True if the entry is still queued or is the entry the run is currently on.</summary>
    internal bool Contains(CommandEntry entry)
    {
        lock (_sync)
        {
            return _queue.Contains(entry) || ReferenceEquals(_current, entry);
        }
    }

    /// <summary>
    /// Cancels the run: waiting entries are removed from the queue, and the wait for the current leader ends.
    /// The current process is left alone; the caller decides whether to stop it. With <paramref name="force"/>, an
    /// entry the run starts after this call (a start that raced with the cancel) is force-killed instead of stopped.
    /// </summary>
    internal void Cancel(bool force = false)
    {
        var unqueued = new List<CommandEntry>();
        lock (_sync)
        {
            _forced |= force;
            if (_cancelled)
            {
                return;
            }

            _cancelled = true;
            foreach (CommandEntry entry in _queue)
            {
                entry.IsQueued = false;
                unqueued.Add(entry);
            }

            _queue.Clear();
        }

        _cts.Cancel();
        foreach (CommandEntry entry in unqueued)
        {
            _session.RaiseEntryChanged(entry);
        }
    }

    /// <summary>Runs the queue. Called on a background task.</summary>
    internal async Task RunAsync()
    {
        try
        {
            CommandEntry? entry;
            while ((entry = Dequeue()) is not null)
            {
                _session.RaiseEntryChanged(entry);

                if (!entry.Process.CanStart)
                {
                    entry.Process.Output.WriteSystem("sequence cancelled: command cannot start");
                    Cancel();
                    break;
                }

                if (!await RunEntryAsync(entry).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled while waiting for the current leader. Nothing further is started.
        }
        finally
        {
            List<CommandEntry> leftover;
            lock (_sync)
            {
                leftover = _queue.ToList();
                foreach (CommandEntry entry in leftover)
                {
                    entry.IsQueued = false;
                }

                _queue.Clear();
                _current = null;
            }

            foreach (CommandEntry entry in leftover)
            {
                _session.RaiseEntryChanged(entry);
            }

            _session.FinishSequence(this);
        }
    }

    /// <summary>Starts one entry and waits for its leader. Returns true if the sequence should continue.</summary>
    private async Task<bool> RunEntryAsync(CommandEntry entry)
    {
        if (!_session.StartEntry(entry) || entry.Process.Snapshot.State == ProcessState.FailedToStart)
        {
            Cancel();
            return false;
        }

        // A Stop or quit that arrived while the entry was being started could not reach it yet.
        if (IsCancelled)
        {
            bool forced;
            lock (_sync)
            {
                forced = _forced;
            }

            if (forced)
            {
                entry.Process.ForceKill();
            }
            else if (entry.Process.Snapshot.State == ProcessState.Running)
            {
                entry.Process.RequestStop();
            }
        }

        await entry.Process.WaitForExitAsync(_cts.Token).ConfigureAwait(false);

        int? code = entry.Process.Snapshot.ExitCode;
        if (code != 0)
        {
            entry.Process.Output.WriteSystem($"sequence cancelled: exit code {code?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}");
            Cancel();
            return false;
        }

        return true;
    }

    private bool IsCancelled
    {
        get
        {
            lock (_sync)
            {
                return _cancelled;
            }
        }
    }

    /// <summary>Takes the next entry off the queue and makes it the current one. Returns null when cancelled or empty.</summary>
    private CommandEntry? Dequeue()
    {
        lock (_sync)
        {
            if (_cancelled || _queue.Count == 0)
            {
                _current = null;
                return null;
            }

            CommandEntry entry = _queue[0];
            _queue.RemoveAt(0);
            entry.IsQueued = false;
            _current = entry;
            return entry;
        }
    }
}
