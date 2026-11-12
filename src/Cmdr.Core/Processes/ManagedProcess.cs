using System.ComponentModel;
using System.Diagnostics;
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
    private Process? _process;
    private IProcessGroup? _group;
    private StreamWriter? _stdin;
    private Task? _stdOutPump;
    private Task? _stdErrPump;
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

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!CanStartLocked)
            {
                throw new InvalidOperationException(
                    $"Cannot start: state is {_snapshot.State}, {_snapshot.LeftoverCount} leftover process(es) running.");
            }

            StartLocked(spec);
        }

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
        string? input;
        string? inputAfterInterrupt;
        StopMethod method;
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
            stdin = _stdin;
            input = _stopInput;
            inputAfterInterrupt = _inputAfterInterrupt;
            method = _stopMethod;

            Output.WriteSystem(method == StopMethod.CtrlC ? "stop requested (Ctrl+C)" : "stop requested (input)");
        }

        // Signals and pipe writes happen outside the lock: a write can block if the child is not reading stdin.
        if (method == StopMethod.CtrlC)
        {
            bool interrupted = SignalGroup(group, g => g.Interrupt(), "stop failed");
            if (interrupted && inputAfterInterrupt is not null)
            {
                WriteStopInput(stdin, inputAfterInterrupt);
            }
        }
        else
        {
            WriteStopInput(stdin, input);
        }

        if (stateChanged)
        {
            RaiseChanged();
        }
    }

    /// <summary>
    /// Force-kills the whole process tree. Valid while running, stopping, or exited with leftover processes;
    /// otherwise does nothing. Never called automatically.
    /// </summary>
    public void ForceKill()
    {
        IProcessGroup? group;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            bool valid = IsActiveLocked
                || (_snapshot.State == ProcessState.Exited && _snapshot.LeftoverCount > 0);
            if (!valid)
            {
                return;
            }

            if (IsActiveLocked)
            {
                _stopOrKillRequested = true;
                _forceKillRequested = true;
            }

            group = _group;
            Output.WriteSystem("force kill");

            // Re-check leftovers soon so the state reflects the kill without waiting for the next poll.
            _leftoverTimer?.Change(LeftoverRecheckAfterKill, LeftoverPollInterval);
        }

        SignalGroup(group, g => g.Kill(), "force kill failed");
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
    /// Stops the current run if active, waits for its leader to exit, then starts <paramref name="spec"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Leftover processes from the earlier run are still alive.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled while waiting.</exception>
    public async Task RestartAsync(LaunchSpec spec, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        bool active;
        lock (_sync)
        {
            active = IsActiveLocked;
        }

        if (active)
        {
            RequestStop();
            await WaitForExitAsync(ct).ConfigureAwait(false);
        }

        lock (_sync)
        {
            if (!CanStartLocked)
            {
                throw new InvalidOperationException(
                    $"Cannot restart: {_snapshot.LeftoverCount} process(es) from the previous run are still running.");
            }
        }

        Start(spec);
    }

    /// <summary>
    /// Stops the leader/leftover tracking and closes the process handle and output log.
    /// </summary>
    /// <remarks>
    /// Does not kill anything on Linux. On Windows, disposing the group closes its job object, which kills any processes
    /// still in it (KILL_ON_JOB_CLOSE); this is intentional.
    /// </remarks>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopLeftoverTimerLocked();
            _runExited?.TrySetResult();
            _process?.Dispose();
            _process = null;
            _group?.Dispose();
            _group = null;
            _stdin = null;
            Output.Dispose();
        }
    }

    private bool CanStartLocked =>
        _snapshot.State is ProcessState.NotStarted or ProcessState.Exited or ProcessState.FailedToStart
        && _snapshot.LeftoverCount == 0;

    private bool IsActiveLocked => _snapshot.State is ProcessState.Running or ProcessState.Stopping;

    /// <summary>Launches the process. Caller holds the lock and has verified <see cref="CanStartLocked"/>.</summary>
    private void StartLocked(LaunchSpec spec)
    {
        int runId = ++_runId;
        ReleaseRunResourcesLocked();
        StopLeftoverTimerLocked();
        _readiness = null;

        _stopMethod = spec.StopMethod;
        _stopInput = spec.StopInput;
        _leftoverMode = spec.Leftovers;
        _stopOrKillRequested = false;
        _forceKillRequested = false;
        _runExited = null;

        DateTimeOffset now = DateTimeOffset.Now;
        Output.BeginRun(now);

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

        Process? process = null;
        string? inputAfterInterrupt = null;
        string? notice = null;
        try
        {
            PreparedLaunch launch = _platform.Prepare(spec);
            inputAfterInterrupt = launch.InputAfterInterrupt;
            notice = launch.Notice;
            process = new Process
            {
                StartInfo = launch.StartInfo,
                EnableRaisingEvents = true,
            };

            // Subscribed before Start so an immediate exit is not missed. The handler waits for the lock,
            // which Start holds until the run state below is in place.
            process.Exited += (_, _) => OnExited(runId, process);
            _platform.Start(process, launch);
        }
        catch (Exception ex)
        {
            process?.Dispose();
            _snapshot = new ProcessSnapshot(ProcessState.FailedToStart, null, null, null, null, null, ex.Message, 0);
            Output.EndRun($"failed to start: {ex.Message}");
            return;
        }

        IProcessGroup group;
        try
        {
            // The platform kills the leader itself if attaching fails, so no untracked process is left running.
            // _process is not yet set, so the Exited handler ignores this run.
            group = _platform.Attach(process);
        }
        catch (Exception ex)
        {
            process.Dispose();
            _snapshot = new ProcessSnapshot(ProcessState.FailedToStart, null, null, null, null, null, ex.Message, 0);
            Output.EndRun($"failed to start: {ex.Message}");
            return;
        }

        _process = process;
        _group = group;
        _inputAfterInterrupt = inputAfterInterrupt;
        // Process.StandardInput throws when stdin is not redirected (the Console stdin mode): then there is no writer.
        _stdin = process.StartInfo.RedirectStandardInput ? process.StandardInput : null;
        if (_stdin is not null)
        {
            _stdin.AutoFlush = true;
        }

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
        _stdOutPump = Task.Run(() => PumpAsync(process.StandardOutput, isStdErr: false));
        _stdErrPump = Task.Run(() => PumpAsync(process.StandardError, isStdErr: true));
        if (notice is not null)
        {
            Output.WriteSystem(notice);
        }
    }

    /// <summary>
    /// Applies the readiness patterns to the lines a run just completed. Runs on the pump thread, outside every lock of this
    /// object. Matching happens before taking the lock, so a slow pattern does not hold it.
    /// </summary>
    private void OnProcessLinesCompleted(IReadOnlyList<OutputLine> lines)
    {
        ReadinessMatcher? matcher;
        lock (_sync)
        {
            matcher = _readiness;
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
            // The matcher check drops lines that belong to an earlier run, which can still be pumped after a new run began.
            if (_disposed || !ReferenceEquals(matcher, _readiness) || !IsActiveLocked || _snapshot.Readiness == phase)
            {
                return;
            }

            _snapshot = _snapshot with { Readiness = phase, ReadinessSince = DateTimeOffset.Now };
        }

        RaiseChanged();
    }

    /// <summary>Reads one output stream until EOF, forwarding raw chunks (no line splitting) to <see cref="Output"/>.</summary>
    private async Task PumpAsync(StreamReader reader, bool isStdErr)
    {
        char[] buffer = new char[ReadBufferChars];
        try
        {
            while (true)
            {
                int read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (isStdErr)
                {
                    Output.WriteStdErr(buffer.AsSpan(0, read));
                }
                else
                {
                    Output.WriteStdOut(buffer.AsSpan(0, read));
                }
            }
        }
        catch (IOException)
        {
            // The pipe was closed (process disposed or killed). Nothing more to read.
        }
        catch (ObjectDisposedException)
        {
            // The process handle was disposed while reading.
        }
    }

    /// <summary>Handles the leader's exit: waits briefly for output to drain, then records the exit and checks for leftovers.</summary>
    private void OnExited(int runId, Process process)
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

            stdOut = _stdOutPump;
            stdErr = _stdErrPump;
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

            int leftovers = _group?.CountLiveProcesses() ?? 0;
            bool detached = leftovers > 0 && TryDetachLeftoversLocked(leftovers);

            Output.EndRun($"exited with code {exitCode}");
            _runExited?.TrySetResult();

            SetLeftoverCountLocked(detached ? 0 : leftovers);
        }

        RaiseChanged();
    }

    /// <summary>
    /// Stops supervising the leftover processes of a run whose leader exited, when the command asks for it
    /// (<see cref="LeftoverMode.Detach"/>). A graceful stop (Ctrl+C or input) still detaches them, so a command such as
    /// <c>dotnet watch</c> does not leave its build servers tracked after the user stops it. Only a force kill during the
    /// run prevents detaching, because it means "kill everything". Writes the outcome to the output before the end marker.
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

    private bool IsCurrentRunLocked(int runId, Process process) =>
        !_disposed && runId == _runId && ReferenceEquals(process, _process) && IsActiveLocked;

    /// <summary>Stores the leftover count, starting or stopping the poll timer as needed. Returns true if the count changed.</summary>
    private bool SetLeftoverCountLocked(int count)
    {
        bool changed = false;
        if (_snapshot.LeftoverCount != count)
        {
            _snapshot = _snapshot with { LeftoverCount = count };
            changed = true;
        }

        if (count > 0)
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

            changed = SetLeftoverCountLocked(_group.CountLiveProcesses());
        }

        if (changed)
        {
            RaiseChanged();
        }
    }

    private void StopLeftoverTimerLocked()
    {
        _leftoverTimer?.Dispose();
        _leftoverTimer = null;
    }

    /// <summary>Releases the previous run's process handle, group and pipes. Caller holds the lock.</summary>
    private void ReleaseRunResourcesLocked()
    {
        _process?.Dispose();
        _process = null;
        _group?.Dispose();
        _group = null;
        _stdin = null;
        _stdOutPump = null;
        _stdErrPump = null;
    }

    /// <summary>Delivers a signal to the group. Returns true if it was delivered without error.</summary>
    private bool SignalGroup(IProcessGroup? group, Action<IProcessGroup> signal, string failurePrefix)
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
            Output.WriteSystem($"{failurePrefix}: {ex.Message}");
            return false;
        }
    }

    private void WriteStopInput(StreamWriter? stdin, string? input)
    {
        try
        {
            if (stdin is null)
            {
                throw new IOException("stdin is not available");
            }

            stdin.Write(input ?? string.Empty);
        }
        catch (IOException ex)
        {
            Output.WriteSystem($"stop input failed: {ex.Message}");
        }
        catch (ObjectDisposedException ex)
        {
            Output.WriteSystem($"stop input failed: {ex.Message}");
        }
    }

    private void RaiseChanged()
    {
        Changed?.Invoke(this);
    }
}
