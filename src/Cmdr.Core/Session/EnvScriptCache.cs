using System.Diagnostics;
using System.Globalization;
using System.Text;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;

namespace Cmdr.Core.Session;

/// <summary>
/// The runs of environment scripts for one session. A run is identified by its owner (a file or a group, by reference) and its
/// fingerprint (what the script depends on, see <see cref="Fingerprint"/>). Thread-safe.
/// </summary>
/// <remarks>
/// A <see cref="EnvScriptRerun.Once"/> run that succeeded is the owner's completed result: a later request with the same fingerprint
/// gets it without a run, and a request with another fingerprint runs the script again and replaces it. <see cref="EnvScriptRerun.EachStart"/>
/// results are never reused. Failures are never reused. Concurrent requests with the same owner and fingerprint share one in-flight run.
/// A waiter whose token is cancelled stops waiting; when the last waiter of an unfinished run leaves, the run itself is cancelled, which
/// kills the script. The last run of each owner is kept for display, whether it succeeded or not. Cancelled runs are not recorded.
/// </remarks>
internal sealed class EnvScriptCache : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<object, Dictionary<string, Run>> _inFlight = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, Completed> _completed = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, (DateTimeOffset FinishedAt, EnvScriptResult Result)> _last = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    /// <summary>
    /// The result of the run for <paramref name="owner"/> and <paramref name="fingerprint"/>: the owner's completed result when it can be
    /// reused (see the type's remarks), else the in-flight run's result, else the result of a new run started with <paramref name="run"/>.
    /// </summary>
    /// <param name="owner">The file or group whose script this is. Compared by reference.</param>
    /// <param name="fingerprint">What the run depends on. Two requests share a result only with equal fingerprints.</param>
    /// <param name="rerun">When the script runs again: a successful <see cref="EnvScriptRerun.Once"/> result is reused.</param>
    /// <param name="run">Starts the script, with the token that cancels it. Called at most once per new run, outside the cache's lock.</param>
    /// <param name="waiter">The caller's token: when it is cancelled, the returned task is cancelled and this caller stops waiting.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ObjectDisposedException">The cache is disposed.</exception>
    public Task<EnvScriptResult> GetAsync(
        object owner,
        string fingerprint,
        EnvScriptRerun rerun,
        Func<CancellationToken, Task<EnvScriptResult>> run,
        CancellationToken waiter)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(run);

        if (waiter.IsCancellationRequested)
        {
            return Task.FromCanceled<EnvScriptResult>(waiter);
        }

        Run joined;
        Run? started = null;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (rerun == EnvScriptRerun.Once
                && _completed.TryGetValue(owner, out Completed? done)
                && done.Fingerprint == fingerprint)
            {
                return Task.FromResult(done.Result);
            }

            if (!_inFlight.TryGetValue(owner, out Dictionary<string, Run>? byFingerprint))
            {
                byFingerprint = new Dictionary<string, Run>(StringComparer.Ordinal);
                _inFlight[owner] = byFingerprint;
            }

            if (!byFingerprint.TryGetValue(fingerprint, out Run? existing))
            {
                existing = new Run(owner, fingerprint);
                byFingerprint[fingerprint] = existing;
                started = existing;
            }

            existing.Waiters++;
            joined = existing;
        }

        // Started outside the lock: the run may start a process, and it may complete synchronously.
        if (started is not null)
        {
            _ = ExecuteAsync(started, rerun, run);
        }

        return Join(joined, waiter);
    }

    /// <summary>Drops the owner's completed result, so that its next request runs the script. In-flight runs are not affected.</summary>
    public void Invalidate(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        lock (_sync)
        {
            _completed.Remove(owner);
        }
    }

    /// <summary>The last finished run of the owner (successful or not) and when it finished, or null when it has none.</summary>
    public (DateTimeOffset FinishedAt, EnvScriptResult Result)? LastRun(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        lock (_sync)
        {
            return _last.TryGetValue(owner, out var last) ? last : null;
        }
    }

    /// <summary>Cancels every in-flight run (their scripts are killed) and refuses new requests. Idempotent.</summary>
    public void Dispose()
    {
        var runs = new List<Run>();
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (Dictionary<string, Run> byFingerprint in _inFlight.Values)
            {
                runs.AddRange(byFingerprint.Values);
            }

            _inFlight.Clear();
            _completed.Clear();
        }

        foreach (Run run in runs)
        {
            run.Source.Cancel();
        }
    }

    /// <summary>
    /// The fingerprint of a run: the fields in order, each written as its length and a colon followed by its text, or as a dash when
    /// it is null. Length prefixes make the encoding unambiguous whatever the fields contain.
    /// </summary>
    internal static string Fingerprint(IEnumerable<string?> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var text = new StringBuilder();
        foreach (string? field in fields)
        {
            if (field is null)
            {
                text.Append('-');
            }
            else
            {
                text.Append(field.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(field);
            }
        }

        return text.ToString();
    }

    /// <summary>Runs the script and records its outcome. Never throws except for cancellation, which cancels the run's task.</summary>
    private async Task ExecuteAsync(Run run, EnvScriptRerun rerun, Func<CancellationToken, Task<EnvScriptResult>> runFunc)
    {
        var clock = Stopwatch.StartNew();
        EnvScriptResult result;
        try
        {
            result = await runFunc(run.Source.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (run.Source.IsCancellationRequested)
        {
            Finish(run, rerun, null);
            run.Completion.TrySetCanceled(run.Source.Token);
            return;
        }
        catch (Exception ex)
        {
            result = EnvScriptResult.Failure($"could not run the script: {ex.Message}", "", null, clock.Elapsed);
        }

        Finish(run, rerun, result);
        run.Completion.TrySetResult(result);
    }

    /// <summary>Marks the run finished, removes it from the in-flight runs, and records its result when it has one.</summary>
    private void Finish(Run run, EnvScriptRerun rerun, EnvScriptResult? result)
    {
        lock (_sync)
        {
            run.Finished = true;
            if (_inFlight.TryGetValue(run.Owner, out Dictionary<string, Run>? byFingerprint)
                && byFingerprint.TryGetValue(run.Fingerprint, out Run? current)
                && ReferenceEquals(current, run))
            {
                byFingerprint.Remove(run.Fingerprint);
                if (byFingerprint.Count == 0)
                {
                    _inFlight.Remove(run.Owner);
                }
            }

            if (result is null)
            {
                return;
            }

            _last[run.Owner] = (DateTimeOffset.UtcNow, result);
            if (rerun == EnvScriptRerun.Once && result.Succeeded)
            {
                _completed[run.Owner] = new Completed(run.Fingerprint, result);
            }
        }
    }

    /// <summary>
    /// A task for one waiter of <paramref name="run"/>: it completes with the run's result, or is cancelled when <paramref name="waiter"/>
    /// is cancelled first (the waiter then leaves the run).
    /// </summary>
    private Task<EnvScriptResult> Join(Run run, CancellationToken waiter)
    {
        var own = new TaskCompletionSource<EnvScriptResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration = default;
        if (waiter.CanBeCanceled)
        {
            registration = waiter.Register(() =>
            {
                if (own.TrySetCanceled(waiter))
                {
                    Leave(run);
                }
            });
        }

        run.Completion.Task.ContinueWith(
            task =>
            {
                registration.Dispose();
                if (task.IsCanceled)
                {
                    own.TrySetCanceled();
                }
                else if (task.IsFaulted)
                {
                    own.TrySetException(task.Exception!.InnerExceptions);
                }
                else
                {
                    own.TrySetResult(task.Result);
                }
            },
            TaskContinuationOptions.ExecuteSynchronously);

        return own.Task;
    }

    /// <summary>A waiter stops waiting. When it was the last one of an unfinished run, the run is cancelled and forgotten.</summary>
    private void Leave(Run run)
    {
        lock (_sync)
        {
            if (run.Finished)
            {
                return;
            }

            run.Waiters--;
            if (run.Waiters > 0)
            {
                return;
            }

            if (_inFlight.TryGetValue(run.Owner, out Dictionary<string, Run>? byFingerprint)
                && byFingerprint.TryGetValue(run.Fingerprint, out Run? current)
                && ReferenceEquals(current, run))
            {
                byFingerprint.Remove(run.Fingerprint);
                if (byFingerprint.Count == 0)
                {
                    _inFlight.Remove(run.Owner);
                }
            }
        }

        run.Source.Cancel();
    }

    /// <summary>One run of a script: its owner, fingerprint, token source and the number of waiters still waiting for it.</summary>
    private sealed class Run
    {
        public Run(object owner, string fingerprint)
        {
            Owner = owner;
            Fingerprint = fingerprint;
        }

        public object Owner { get; }

        public string Fingerprint { get; }

        public CancellationTokenSource Source { get; } = new();

        public TaskCompletionSource<EnvScriptResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Waiters still waiting. Guarded by the cache's lock.</summary>
        public int Waiters { get; set; }

        /// <summary>True once the run has ended (completed, failed or cancelled). Guarded by the cache's lock.</summary>
        public bool Finished { get; set; }
    }

    /// <summary>The completed result of an owner: the fingerprint it was run with, and a successful result.</summary>
    private sealed record Completed(string Fingerprint, EnvScriptResult Result);
}
