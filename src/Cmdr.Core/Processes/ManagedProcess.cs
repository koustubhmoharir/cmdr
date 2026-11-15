using System.ComponentModel;
using Cmdr.Core.Config;
using Cmdr.Core.Output;

namespace Cmdr.Core.Processes;

/// <summary>
/// Supervises one command across runs: launches it, pumps its output into <see cref="Output"/>, stops it, and
/// tracks processes that outlive their leader. One instance is reused for every run of the command.
/// </summary>
/// <remarks>
/// All public members are thread-safe. <see cref="Changed"/> is raised on the thread that changed the state,
/// never while the internal lock is held.
/// </remarks>
public sealed class ManagedProcess : IDisposable
{
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LeftoverPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LeftoverRecheckAfterKill = TimeSpan.FromMilliseconds(200);
    private const int ReadBufferChars = 4096;

    private readonly object _sync = new();
    private readonly IProcessPlatform _platform;

    private ProcessSnapshot _snapshot = new(ProcessState.NotStarted, null, null, null, null, null, null, 0);
    private ILaunchedProcess? _process;
    private IProcessGroup? _group;
    private RunPipes? _pipes;
    private readonly List<RunPipes> _livePipes = new();
    private long _outputRun;
    private TaskCompletionSource? _runExited;
    private StopMethod _stopMethod;
    private string? _stopInput;
    private LeftoverMode _leftoverMode;
    private bool _stopOrKillRequested;
    private bool _forceKillRequested;
    private string? _inputAfterInterrupt;
    private int _runId;
    private ReadinessMatcher? _readiness;
    private Timer? _leftoverTimer;
    private bool _leftoverDetachPending;
    private bool _disposed;

    /// <summary>Creates a supervisor whose output buffer keeps up to <paramref name="bufferLines"/> lines.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bufferLines"/> is not positive.</exception>
    public ManagedProcess(int bufferLines)
        : this(bufferLines, ProcessPlatform.Current)
    {
    }

    internal ManagedProcess(int bufferLines, IProcessPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        Output = new CommandOutput(bufferLines);
        _platform = platform;
        Output.ProcessLinesCompleted += OnProcessLinesCompleted;
    }

    /// <summary>Output of this command. Each run clears the in-memory buffer and appends a start marker; the log file keeps all runs.</summary>
    public CommandOutput Output { get; }

    /// <summary>Raised after every change to <see cref="Snapshot"/>, from the thread that made the change.</summary>
    public event Action<ManagedProcess>? Changed;

    /// <summary>The current state snapshot.</summary>
    public ProcessSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    /// <summary>True when the command is not running and no processes from an earlier run remain.</summary>
    public bool CanStart
    {
        get
        {
            lock (_sync)
            {
                return CanStartLocked;
            }
        }
    }

    /// <summary>
    /// Launches a new run.
    /// </summary>
    /// <remarks>
    /// Launch failures do not throw: the state becomes <see cref="ProcessState.FailedToStart"/> and a system line is written.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The command is running, stopping, or has leftover processes.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
    public void Start(LaunchSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ReleasedRun previous;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!CanStartLocked)
            {
                throw new InvalidOperationException(
                    $"Cannot start: state is {_snapshot.State}, {LeftoverTextLocked()} leftover process(es) running.");
            }

            previous = StartLocked(spec);
        }

        previous.Dispose();
        RaiseChanged();
    }

    /// <summary>
    /// Requests a graceful stop. Valid while running or stopping; otherwise does nothing. Calling it again while
    /// stopping sends the stop signal or input again.
    /// </summary>
    public void RequestStop()
    {
        IProcessGroup? group;
        StreamWriter? stdin;
        object? stdinWriteLock;
        string? input;
        string? inputAfterInterrupt;
        StopMethod method;
        long outputRun;
        bool stateChanged;

        lock (_sync)
        {
            if (_disposed || !IsActiveLocked)
            {
                return;
            }

            stateChanged = _snapshot.State == ProcessState.Running;
            _stopOrKillRequested = true;
            _snapshot = _snapshot with
            {
                State = ProcessState.Stopping,
                StopRequestedAt = _snapshot.StopRequestedAt ?? DateTimeOffset.Now,
            };

            group = _group;
            stdin = _pipes?.Stdin;
            stdinWriteLock = _pipes?.StdinWriteLock;
            input = _stopInput;
            inputAfterInterrupt = _inputAfterInterrupt;
            method = _stopMethod;
            outputRun = _outputRun;

            Output.WriteSystem(method == StopMethod.CtrlC ? "stop requested (Ctrl+C)" : "stop requested (input)");
        }

        // Signals and pipe writes happen outside the lock: a write can block if the child is not reading stdin.
        if (method == StopMethod.CtrlC)
        {
            bool interrupted = SignalGroup(group, g => g.Interrupt(), "stop failed", outputRun);
            if (interrupted && inputAfterInterrupt is not null)
            {
                WriteStopInput(stdin, stdinWriteLock, inputAfterInterrupt, outputRun);
            }
        }
        else
        {
            WriteStopInput(stdin, stdinWriteLock, input, outputRun);
        }

        if (stateChanged)
        {
            RaiseChanged();
        }
    }

    /// <summary>
    /// Force-kills the whole process tree. Valid while running, stopping, or exited with leftover processes;
    /// otherwise does nothing. Never called automatically. It does not wait for a graceful stop in progress, and it cancels a
    /// pending detach of leftovers (<see cref="LeftoverMode.Detach"/>).
    /// </summary>
    public void ForceKill()
    {
        ForceKillCore(expectedRunId: null);
    }

    /// <summary>The number of the current run; each start gets a new number. Used to act on one run only.</summary>
    internal int RunId
    {
        get
        {
            lock (_sync)
            {
                return _runId;
            }
        }
    }

    /// <summary>
    /// Force-kills the process tree like <see cref="ForceKill()"/>, but only if run <paramref name="expectedRunId"/> is still the
    /// current run when the decision is made. Does nothing when a newer run has started since the caller read <see cref="RunId"/>.
    /// </summary>
    internal void ForceKill(int expectedRunId)
    {
        ForceKillCore(expectedRunId);
    }

    private void ForceKillCore(int? expectedRunId)
    {
        IProcessGroup? group;
        long outputRun;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (expectedRunId is int expected && _runId != expected)
            {
                return;
            }

            bool valid = IsActiveLocked
                || (_snapshot.State == ProcessState.Exited && _snapshot.HasLeftovers);
            if (!valid)
            {
                return;
            }

            if (IsActiveLocked)
            {
                _stopOrKillRequested = true;
            }

            // A force kill means "kill everything", also after the leader exited: leftovers whose detach is still waiting for a
            // successful count are killed, not detached when the count recovers.
            _forceKillRequested = true;
            _leftoverDetachPending = false;

            group = _group;
            outputRun = _outputRun;
            Output.WriteSystem("force kill");

            // Re-check leftovers soon so the state reflects the kill without waiting for the next poll.
            _leftoverTimer?.Change(LeftoverRecheckAfterKill, LeftoverPollInterval);
        }

        SignalGroup(group, g => g.Kill(), "force kill failed", outputRun);
    }

    /// <summary>
    /// Completes when the leader of the current run has exited. Completes immediately if no run is in progress.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before the leader exited.</exception>
    public Task WaitForExitAsync(CancellationToken ct = default)
    {
        Task task;
        lock (_sync)
        {
            task = _runExited?.Task ?? Task.CompletedTask;
        }

        return ct.CanBeCanceled ? task.WaitAsync(ct) : task;
    }

    /// <summary>
    /// Stops the leader/leftover tracking, closes the process handle, the redirected pipes of every run (ending their output
    /// readers) and the output log. Output is no longer written afterwards.
    /// </summary>
    /// <remarks>
    /// Does not kill anything on Linux; a surviving process that writes to a closed pipe gets the usual SIGPIPE or write error.
    /// On Windows, disposing the group closes its job object, which kills any processes still in it (KILL_ON_JOB_CLOSE); this
    /// is intentional.
    /// </remarks>
    public void Dispose()
    {
        ILaunchedProcess? process;
        IProcessGroup? group;
        var pipes = new List<RunPipes>();
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopLeftoverTimerLocked();
            _runExited?.TrySetResult();
            process = _process;
            _process = null;
            group = _group;
            _group = null;

            // The current run's pipes too: once both of its readers ended it is no longer live, but its stdin is still open.
            if (_pipes is not null)
            {
                pipes.Add(_pipes);
            }

            _pipes = null;
            pipes.AddRange(_livePipes.Where(live => !pipes.Contains(live)));
            Output.Dispose();
        }

        // Released outside the lock: the platform's exit notification can hold its own lock while it calls OnExited, which takes
        // this one, so disposing the process under this lock could deadlock with an exit that is being reported (on Linux,
        // Process.Dispose waits for the lock that Process holds while raising Exited). OnExited sees _disposed and returns.
        process?.Dispose();
        group?.Dispose();
        foreach (RunPipes run in pipes)
        {
            run.Abort();
        }
    }

    /// <summary>Runs whose output readers have not both ended yet. For tests.</summary>
    internal int LivePipeCount
    {
        get
        {
            lock (_sync)
            {
                return _livePipes.Count;
            }
        }
    }

    private bool CanStartLocked =>
        _snapshot.State is ProcessState.NotStarted or ProcessState.Exited or ProcessState.FailedToStart
        && !_snapshot.HasLeftovers;

    private bool IsActiveLocked => _snapshot.State is ProcessState.Running or ProcessState.Stopping;

    /// <summary>
    /// Launches the process. Caller holds the lock, has verified <see cref="CanStartLocked"/>, and disposes the returned
    /// resources of the previous run after releasing the lock.
    /// </summary>
    private ReleasedRun StartLocked(LaunchSpec spec)
    {
        int runId = ++_runId;
        ReleasedRun previous = ReleaseRunResourcesLocked();
        StopLeftoverTimerLocked();
        _leftoverDetachPending = false;
        _readiness = null;

        _stopMethod = spec.StopMethod;
        _stopInput = spec.StopInput;
        _leftoverMode = spec.Leftovers;
        _stopOrKillRequested = false;
        _forceKillRequested = false;
        _runExited = null;

        DateTimeOffset now = DateTimeOffset.Now;
        long outputRun = Output.BeginRun(now);
        _outputRun = outputRun;

        ReadinessMatcher? readiness = null;
        if (spec.Ready is not null)
        {
            try
            {
                readiness = new ReadinessMatcher(spec.Ready);
            }
            catch (ArgumentException ex)
            {
                // Configuration validation rejects this; a launch that skipped it must not crash the supervisor.
                Output.WriteSystem($"ready patterns ignored: {ex.Message}");
            }
        }

        LaunchedTree launched;
        string? inputAfterInterrupt;
        string? notice;
        try
        {
            PreparedLaunch launch = _platform.Prepare(spec);
            inputAfterInterrupt = launch.InputAfterInterrupt;
            notice = launch.Notice;

            // The exit callback may run before Launch returns. It waits for the lock, which this method holds until the run
            // state below is in place, so an immediate exit is still recorded. A launch that fails after the leader started (the
            // group never formed) may still call it: the leader is killed, and OnExited ignores the call because this run never
            // becomes current.
            launched = _platform.Launch(launch, exited => OnExited(runId, exited));
        }
        catch (Exception ex)
        {
            _snapshot = new ProcessSnapshot(ProcessState.FailedToStart, null, null, null, null, null, ex.Message, 0);
            Output.EndRun($"failed to start: {ex.Message}");
            return previous;
        }

        ILaunchedProcess process = launched.Process;
        IProcessGroup group = launched.Group;
        _process = process;
        _group = group;
        _inputAfterInterrupt = inputAfterInterrupt;
        // The writer is null when stdin is not redirected (the Console stdin mode): then there is no writer.
        StreamWriter? stdin = process.StandardInput;
        if (stdin is not null)
        {
            stdin.AutoFlush = true;
        }

        var pipes = new RunPipes(outputRun, process.StandardOutput, process.StandardError, stdin);
        _pipes = pipes;
        _livePipes.Add(pipes);

        _runExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _readiness = readiness;
        _snapshot = new ProcessSnapshot(
            ProcessState.Running,
            process.Id,
            now,
            null,
            null,
            null,
            null,
            0,
            StopRequested: false,
            Readiness: readiness is null ? ProcessReadiness.None : ProcessReadiness.Starting,
            ReadinessSince: readiness is null ? null : now);
        // The pumps run on the thread pool, never on this thread. A pump's output callbacks take this object's lock, so a pump
        // that read synchronously here would take the output's notify lock while holding this one, against the lock order.
        pipes.StdOutPump = Task.Run(() => PumpAsync(pipes, pipes.StdOut, isStdErr: false));
        pipes.StdErrPump = Task.Run(() => PumpAsync(pipes, pipes.StdErr, isStdErr: true));
        if (notice is not null)
        {
            Output.WriteSystem(notice);
        }

        return previous;
    }

    /// <summary>
    /// Applies the readiness patterns to the lines a run just completed. Runs on the pump thread, outside every lock of this
    /// object. Matching happens before taking the lock, so a slow pattern does not hold it.
    /// </summary>
    private void OnProcessLinesCompleted(long outputRun, IReadOnlyList<OutputLine> lines)
    {
        ReadinessMatcher? matcher;
        lock (_sync)
        {
            matcher = outputRun == _outputRun ? _readiness : null;
        }

        if (matcher is null)
        {
            return;
        }

        ProcessReadiness? verdict = matcher.Evaluate(lines);
        if (verdict is not { } phase)
        {
            return;
        }

        lock (_sync)
        {
            // The run check drops lines of an earlier run, in case a new run began while the lines were being matched.
            if (_disposed || outputRun != _outputRun || !ReferenceEquals(matcher, _readiness) || !IsActiveLocked
                || _snapshot.Readiness == phase)
            {
                return;
            }

            _snapshot = _snapshot with { Readiness = phase, ReadinessSince = DateTimeOffset.Now };
        }

        RaiseChanged();
    }

    /// <summary>
    /// Reads one output stream of a run until EOF, forwarding raw chunks (no line splitting) to <see cref="Output"/> tagged
    /// with the run, so the output drops them once a newer run has begun. Keeps reading after that: a process left over from
    /// the run may still hold the pipe, and closing it would make that process's writes fail. Closes the stream at the end.
    /// </summary>
    private async Task PumpAsync(RunPipes pipes, StreamReader reader, bool isStdErr)
    {
        char[] buffer = new char[ReadBufferChars];
        CancellationToken ct = pipes.Aborted;
        try
        {
            while (true)
            {
                int read = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                if (read == 0 || ct.IsCancellationRequested)
                {
                    break;
                }

                if (isStdErr)
                {
                    Output.WriteStdErr(pipes.OutputRun, buffer.AsSpan(0, read));
                }
                else
                {
                    Output.WriteStdOut(pipes.OutputRun, buffer.AsSpan(0, read));
                }
            }
        }
        catch (IOException)
        {
            // The pipe broke. Nothing more to read.
        }
        catch (ObjectDisposedException)
        {
            // The stream was closed by Dispose while reading.
        }
        catch (OperationCanceledException)
        {
            // Dispose aborted the read.
        }
        finally
        {
            reader.Dispose();
            if (pipes.PumpFinished())
            {
                lock (_sync)
                {
                    _livePipes.Remove(pipes);
                }
            }
        }
    }

    /// <summary>Handles the leader's exit: waits briefly for output to drain, then records the exit and checks for leftovers.</summary>
    private void OnExited(int runId, ILaunchedProcess process)
    {
        DateTimeOffset exitedAt = DateTimeOffset.Now;
        Task? stdOut;
        Task? stdErr;
        lock (_sync)
        {
            if (!IsCurrentRunLocked(runId, process))
            {
                return;
            }

            stdOut = _pipes?.StdOutPump;
            stdErr = _pipes?.StdErrPump;
        }

        // Drain before writing the exit marker so output is not written after it. Bounded, because an orphaned
        // child can hold the pipes open indefinitely.
        try
        {
            Task.WaitAll(new[] { stdOut!, stdErr! }, OutputDrainTimeout);
        }
        catch (AggregateException)
        {
            // Pump faults are already handled inside PumpAsync.
        }

        lock (_sync)
        {
            if (!IsCurrentRunLocked(runId, process))
            {
                return;
            }

            int exitCode = process.ExitCode;
            _readiness = null;
            _snapshot = _snapshot with
            {
                State = ProcessState.Exited,
                ExitedAt = exitedAt,
                ExitCode = exitCode,
                StopRequested = _stopOrKillRequested,
                Readiness = ProcessReadiness.None,
                ReadinessSince = null,
            };

            // A failed count must not prevent recording the exit: the run is finished either way. The leftovers are then
            // unknown, and the poll retries; detaching is decided when a count first succeeds.
            int? leftovers = TryCountLeftoversLocked();
            bool detached = leftovers > 0 && TryDetachLeftoversLocked(leftovers.Value);
            _leftoverDetachPending = leftovers is null;

            Output.EndRun($"exited with code {exitCode}");
            _runExited?.TrySetResult();

            SetLeftoverCountLocked(detached ? 0 : leftovers);
        }

        RaiseChanged();
    }

    /// <summary>
    /// Stops supervising the leftover processes of a run whose leader exited, when the command asks for it
    /// (<see cref="LeftoverMode.Detach"/>). A graceful stop (Ctrl+C or input) still detaches them, so a command such as
    /// <c>dotnet watch</c> does not leave its build servers tracked after the user stops it. Only a force kill prevents
    /// detaching, because it means "kill everything": one during the run, or one after the leader exited while the detach
    /// waits for a successful count. Writes the outcome to the output before the end marker.
    /// Returns true if they were detached; on failure they stay tracked.
    /// Caller holds the lock and has verified that <paramref name="leftovers"/> is positive.
    /// </summary>
    private bool TryDetachLeftoversLocked(int leftovers)
    {
        if (_leftoverMode != LeftoverMode.Detach || _forceKillRequested || _group is null)
        {
            return false;
        }

        try
        {
            _group.Detach();
        }
        catch (Exception ex)
        {
            Output.WriteSystem($"detach failed: {ex.Message}");
            return false;
        }

        _group.Dispose();
        _group = null;
        Output.WriteSystem($"{leftovers} leftover process(es) detached");
        return true;
    }

    private bool IsCurrentRunLocked(int runId, ILaunchedProcess process) =>
        !_disposed && runId == _runId && ReferenceEquals(process, _process) && IsActiveLocked;

    /// <summary>
    /// Counts the live processes in the group. Returns null when the count fails; the first failure of a streak writes a
    /// system line. A missing group (released or detached) has none. Caller holds the lock.
    /// </summary>
    private int? TryCountLeftoversLocked()
    {
        if (_group is null)
        {
            return 0;
        }

        try
        {
            return _group.CountLiveProcesses();
        }
        catch (Exception ex)
        {
            // Any failure is contained: this runs on a timer or process-exit callback, where an exception ends the host.
            if (!_snapshot.LeftoverCountUnknown)
            {
                Output.WriteSystem($"leftover check failed: {ex.Message}, retrying");
            }

            return null;
        }
    }

    /// <summary>
    /// Stores the leftover count (null: unknown, keeping the last known count), starting or stopping the poll timer as needed.
    /// Returns true if the snapshot changed.
    /// </summary>
    private bool SetLeftoverCountLocked(int? count)
    {
        ProcessSnapshot before = _snapshot;
        _snapshot = count is int known
            ? _snapshot with { LeftoverCount = known, LeftoverCountUnknown = false }
            : _snapshot with { LeftoverCountUnknown = true };
        bool changed = _snapshot != before;

        if (_snapshot.HasLeftovers)
        {
            _leftoverTimer ??= new Timer(OnLeftoverTick, _runId, LeftoverPollInterval, LeftoverPollInterval);
        }
        else
        {
            StopLeftoverTimerLocked();
        }

        return changed;
    }

    private void OnLeftoverTick(object? state)
    {
        bool changed;
        lock (_sync)
        {
            if (_disposed || _leftoverTimer is null || _group is null || state is not int runId || runId != _runId)
            {
                return;
            }

            int? count = TryCountLeftoversLocked();
            if (count > 0 && _leftoverDetachPending && TryDetachLeftoversLocked(count.Value))
            {
                count = 0;
            }

            if (count is not null)
            {
                _leftoverDetachPending = false;
            }

            changed = SetLeftoverCountLocked(count);
        }

        if (changed)
        {
            RaiseChanged();
        }
    }

    private string LeftoverTextLocked() =>
        _snapshot.LeftoverCountUnknown ? "an unknown number of" : _snapshot.LeftoverCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void StopLeftoverTimerLocked()
    {
        _leftoverTimer?.Dispose();
        _leftoverTimer = null;
    }

    /// <summary>
    /// Detaches the previous run's process handle, group and stdin pipe; the caller disposes them with
    /// <see cref="ReleasedRun.Dispose"/> after releasing the lock (see <see cref="Dispose"/> for why not under it). Its output
    /// readers keep draining until the pipes reach EOF (see <see cref="PumpAsync"/>); the output drops what they read from now
    /// on. Caller holds the lock.
    /// </summary>
    private ReleasedRun ReleaseRunResourcesLocked()
    {
        var released = new ReleasedRun(_process, _group, _pipes);
        _process = null;
        _group = null;
        _pipes = null;
        return released;
    }

    /// <summary>A previous run's resources, to dispose outside the lock.</summary>
    private readonly record struct ReleasedRun(ILaunchedProcess? Process, IProcessGroup? Group, RunPipes? Pipes)
    {
        public void Dispose()
        {
            Process?.Dispose();
            Group?.Dispose();
            Pipes?.CloseStdin();
        }
    }

    /// <summary>
    /// Delivers a signal to the group. Returns true if it was delivered without error. A failure is written for run
    /// <paramref name="outputRun"/> only, so it does not show in a newer run's output. Caller holds no lock.
    /// </summary>
    private bool SignalGroup(IProcessGroup? group, Action<IProcessGroup> signal, string failurePrefix, long outputRun)
    {
        if (group is null)
        {
            return false;
        }

        try
        {
            signal(group);
            return true;
        }
        catch (Win32Exception ex)
        {
            Output.WriteSystem(outputRun, $"{failurePrefix}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Writes stop input to the run's stdin. A failure is written for run <paramref name="outputRun"/> only. Caller holds no lock.</summary>
    private void WriteStopInput(StreamWriter? stdin, object? stdinWriteLock, string? input, long outputRun)
    {
        try
        {
            if (stdin is null)
            {
                throw new IOException("stdin is not available");
            }

            // StreamWriter is not safe for concurrent writes. Keep this gate separate from _sync because a
            // write can block on a full pipe and force kill must remain available while it does.
            lock (stdinWriteLock ?? stdin)
            {
                stdin.Write(input ?? string.Empty);
            }
        }
        catch (IOException ex)
        {
            Output.WriteSystem(outputRun, $"stop input failed: {ex.Message}");
        }
        catch (ObjectDisposedException ex)
        {
            Output.WriteSystem(outputRun, $"stop input failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Raises <see cref="Changed"/> outside the lock. Each subscriber is invoked separately: an exception from one is reported as
    /// a <c>cmdr error:</c> system line of the current run, and neither stops the others nor escapes to the thread that raised it
    /// (a timer or process-exit thread, where an escaped exception ends the host).
    /// </summary>
    private void RaiseChanged()
    {
        Action<ManagedProcess>? handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<ManagedProcess> handler in handlers.GetInvocationList().Cast<Action<ManagedProcess>>())
        {
            try
            {
                handler(this);
            }
            catch (Exception ex)
            {
                Output.WriteSystem($"cmdr error: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>The redirected pipes of one run and the tasks reading its output. Owned by the supervisor until both readers end.</summary>
    private sealed class RunPipes
    {
        private readonly CancellationTokenSource _abort = new();
        private int _pumpsRunning = 2;

        public RunPipes(long outputRun, StreamReader stdOut, StreamReader stdErr, StreamWriter? stdin)
        {
            OutputRun = outputRun;
            StdOut = stdOut;
            StdErr = stdErr;
            Stdin = stdin;
            StdinWriteLock = stdin is null ? null : new object();
        }

        /// <summary>The output run the chunks read from these pipes belong to.</summary>
        public long OutputRun { get; }

        public StreamReader StdOut { get; }

        public StreamReader StdErr { get; }

        /// <summary>The stdin writer, or null when stdin is not redirected.</summary>
        public StreamWriter? Stdin { get; }

        /// <summary>
        /// Serializes writes to <see cref="Stdin"/>: StreamWriter is not safe for concurrent writes. Separate from the supervisor's
        /// lock because a write can block on a full pipe and force kill must remain available while it does.
        /// </summary>
        public object? StdinWriteLock { get; }

        public Task? StdOutPump { get; set; }

        public Task? StdErrPump { get; set; }

        /// <summary>Cancelled when the supervisor is disposed.</summary>
        public CancellationToken Aborted => _abort.Token;

        /// <summary>Records that one reader ended. Returns true for the second.</summary>
        public bool PumpFinished() => Interlocked.Decrement(ref _pumpsRunning) == 0;

        /// <summary>
        /// Closes the stdin pipe. The underlying stream is closed directly, not the writer: a stop input write may be blocked on
        /// it, and disposing the writer would flush through the same blocked pipe.
        /// </summary>
        public void CloseStdin() => CloseQuietly(Stdin?.BaseStream);

        /// <summary>Cancels the readers and closes every pipe.</summary>
        public void Abort()
        {
            _abort.Cancel();
            CloseQuietly(StdOut.BaseStream);
            CloseQuietly(StdErr.BaseStream);
            CloseStdin();
        }

        private static void CloseQuietly(Stream? stream)
        {
            try
            {
                stream?.Dispose();
            }
            catch (IOException)
            {
                // The pipe is being abandoned.
            }
        }
    }
}
