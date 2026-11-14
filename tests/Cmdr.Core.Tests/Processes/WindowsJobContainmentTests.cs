using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;

namespace Cmdr.Core.Tests.Processes;

/// <summary>
/// Shares one non-parallel collection with <see cref="WindowsProcessPlatformTests"/>, so the cmd and PING processes of the
/// Windows process tests never overlap with another test class's launches.
/// </summary>
[CollectionDefinition("Windows processes", DisableParallelization = true)]
public sealed class WindowsProcessesCollection
{
}

/// <summary>
/// Windows only: a child is created inside its job (no start-then-assign window), the handle list keeps one launch's pipes out of
/// another child, Process.Start in Interrupt does not inherit a launch's pipe ends, and Kill or Dispose reach children the leader
/// spawned right away. These run on Windows and are reported back as text, so every failure carries a diagnostic dump
/// (see <see cref="Probe.Diagnose"/>).
/// </summary>
[SupportedOSPlatform("windows")]
[Collection("Windows processes")]
public class WindowsJobContainmentTests
{
    private const int EarlyChildRuns = 20;
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan KillTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan EofTimeout = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task EarlyChildIsInTheJobFromCreation()
    {
        if (!OperatingSystem.IsWindows()) return;

        for (int run = 1; run <= EarlyChildRuns; run++)
        {
            await EarlyChildRunAsync($"early child, run {run} of {EarlyChildRuns}, Group.Kill", disposeGroup: false);
        }
    }

    [Fact]
    public async Task DisposingTheGroupKillsTheEarlyChild()
    {
        if (!OperatingSystem.IsWindows()) return;

        await EarlyChildRunAsync("early child, Group.Dispose (kill-on-close)", disposeGroup: true);
    }

    [Fact]
    public async Task HandleListKeepsOneLaunchsPipesOutOfAnother()
    {
        if (!OperatingSystem.IsWindows()) return;

        var platform = new WindowsProcessPlatform();
        using var dirA = new TempDir();
        using var dirB = new TempDir();
        using var a = new Probe("launch A (short, its stdout is read to EOF)");
        using var b = new Probe("launch B (long, must not hold A's pipes)");

        LaunchedTree treeA = a.Start(platform, Cmd("ping -n 2 127.0.0.1 >nul & echo done", dirA.Path));
        Task stdoutA = a.DrainStdout(treeA.Process.StandardOutput);
        _ = a.DrainStderr(treeA.Process.StandardError);

        LaunchedTree treeB = b.Start(platform, Cmd("ping -n 30 127.0.0.1 >nul", dirB.Path));
        _ = b.DrainStdout(treeB.Process.StandardOutput);
        _ = b.DrainStderr(treeB.Process.StandardError);

        Require(await a.ExitedWithin(ExitTimeout), a, "launch A did not exit within 10 s");
        Require(!b.HasExited, b, "launch B exited before A's stdout was checked, so the check is inconclusive");

        bool aEof = await Within(stdoutA, EofTimeout);
        Require(
            aEof,
            a,
            $"launch A's stdout did not reach EOF within 3 s of A's exit (A exited at {a.ExitedAtMs} ms, EOF at {a.StdoutEofAtMs} ms, " +
            "B still running). If B had inherited A's pipe write end, EOF would wait for B's 30 s ping.");
        Require(!a.ReadFailed, a, "launch A's stdout read failed (see stdout/stderr read errors)");
        Require(a.StdoutText.Contains("done", StringComparison.Ordinal), a, "launch A's stdout does not contain \"done\"");
    }

    [Fact]
    public async Task ChildDoesNotInheritAnInheritableHandleOutsideItsList()
    {
        if (!OperatingSystem.IsWindows()) return;

        // The sequential test above cannot see a leak: A's client copies are closed before B starts. Here a pipe whose write end
        // is inheritable is still open in cmdr when the child is created, as it is for a concurrent launch in flight. Only the
        // child could keep it open once cmdr closes its own copy, so EOF must arrive long before the child's 30 s ping ends.
        var platform = new WindowsProcessPlatform();
        using var dir = new TempDir();
        using var held = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var probe = new Probe("child must not inherit a handle that is not in its list");

        LaunchedTree tree = probe.Start(platform, Cmd("ping -n 30 127.0.0.1 >nul", dir.Path));
        _ = probe.DrainStdout(tree.Process.StandardOutput);
        _ = probe.DrainStderr(tree.Process.StandardError);

        held.DisposeLocalCopyOfClientHandle();
        Task<int> read = Task.Run(() => held.ReadByte()); // -1 at EOF
        bool done = await Within(read, EofTimeout);
        Require(
            done,
            probe,
            "the held pipe did not reach EOF within 3 s after cmdr closed its copy: the child inherited a handle outside its handle list " +
            "(its own 30 s ping is still running)");
        int value = await read;
        Require(value == -1, probe, $"the held pipe's read returned {value}, expected -1 (EOF)");
    }

    [Fact]
    public async Task InterruptRunningAlongsideLaunchesDoesNotDelayEof()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Interrupt starts a helper process with Process.Start, which inherits every inheritable handle. The stand-in helper runs
        // for about 4 s, so a leaked pipe end would hold a launch's EOF past the 3 s limit. The helper's arguments are commented
        // out by the trailing '#', so the --send-ctrl-c arguments that Interrupt appends are ignored by PowerShell.
        var helper = new CtrlCHelperCommand(
            "powershell",
            new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 4; exit 0 #" });
        var platform = new WindowsProcessPlatform(helper);
        using var dirA = new TempDir();
        using var dirB = new TempDir();
        using var groupOwner = new Probe("group B: interrupted repeatedly while launches run");

        LaunchedTree treeB = groupOwner.Start(platform, Cmd("ping -n 60 127.0.0.1 >nul", dirB.Path));
        _ = groupOwner.DrainStdout(treeB.Process.StandardOutput);
        _ = groupOwner.DrainStderr(treeB.Process.StandardError);

        int interrupts = 0;
        string? interruptError = null;
        using var stop = new CancellationTokenSource();
        Task interrupting = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    treeB.Group.Interrupt();
                    Interlocked.Increment(ref interrupts);
                }
                catch (Exception ex)
                {
                    Volatile.Write(ref interruptError, ex.ToString());
                    return;
                }
            }
        });

        try
        {
            for (int run = 1; run <= 3; run++)
            {
                using var probe = new Probe($"launch A #{run} (EOF must come within 3 s of exit while Interrupt runs)");
                LaunchedTree treeA = probe.Start(platform, Cmd("ping -n 2 127.0.0.1 >nul & echo done", dirA.Path));
                Task stdout = probe.DrainStdout(treeA.Process.StandardOutput);
                _ = probe.DrainStderr(treeA.Process.StandardError);

                Require(await probe.ExitedWithin(ExitTimeout), probe, "launch A did not exit within 10 s");
                bool eof = await Within(stdout, EofTimeout);
                probe.AddNote($"Interrupt calls completed so far: {Volatile.Read(ref interrupts)}; " +
                              $"last Interrupt error: {Volatile.Read(ref interruptError) ?? "none"}");
                Require(
                    eof,
                    probe,
                    $"launch A's stdout did not reach EOF within 3 s of its exit (exit at {probe.ExitedAtMs} ms, EOF at " +
                    $"{probe.StdoutEofAtMs} ms) while Interrupt ran on another group");
                Require(!probe.ReadFailed, probe, "launch A's stdout read failed while Interrupt ran");
                Require(
                    Volatile.Read(ref interruptError) is null,
                    probe,
                    $"Interrupt failed while launches ran: {Volatile.Read(ref interruptError)}");
            }
        }
        finally
        {
            stop.Cancel();
        }

        bool stopped = await Within(interrupting, TimeSpan.FromSeconds(15));
        Require(
            stopped,
            groupOwner,
            "the Interrupt loop did not stop within 15 s after the launches finished");
        Require(
            Volatile.Read(ref interruptError) is null,
            groupOwner,
            $"Interrupt failed: {Volatile.Read(ref interruptError)}");
        groupOwner.AddNote($"Interrupt calls completed in total: {Volatile.Read(ref interrupts)}");
        Require(Volatile.Read(ref interrupts) >= 1, groupOwner, "Interrupt never completed, so the test exercised nothing");
    }

    [Fact]
    public async Task ExitCodeAndOutputReachTheCaller()
    {
        if (!OperatingSystem.IsWindows()) return;

        var platform = new WindowsProcessPlatform();
        using var dir = new TempDir();
        using var probe = new Probe("exit code and output");

        LaunchedTree tree = probe.Start(platform, Cmd("echo out & echo err 1>&2 & exit /b 7", dir.Path));
        Task stdout = probe.DrainStdout(tree.Process.StandardOutput);
        Task stderr = probe.DrainStderr(tree.Process.StandardError);

        Require(await probe.ExitedWithin(ExitTimeout), probe, "leader did not exit within 10 s");
        Require(await Within(Task.WhenAll(stdout, stderr), ExitTimeout), probe, "stdout or stderr did not reach EOF within 10 s");
        Require(!probe.ReadFailed, probe, "a read failed (see stdout/stderr read errors)");

        // A second exit call would be a bug; give a late one time to show itself.
        await Task.Delay(300);
        Require(probe.ExitCalls == 1, probe, $"onExited was called {probe.ExitCalls} times, expected exactly once");
        Require(ReferenceEquals(probe.ExitInstance, tree.Process), probe, "onExited received an instance other than LaunchedTree.Process");
        Require(tree.Process.ExitCode == 7, probe, $"ExitCode is {tree.Process.ExitCode}, expected 7");
        Require(probe.StdoutLines.Contains("out"), probe, "stdout does not contain the line \"out\"");
        Require(probe.StderrLines.Contains("err"), probe, "stderr does not contain the line \"err\"");
    }

    [Fact]
    public async Task FailedStartThrowsWithTheDirectoryAndCallsNoExit()
    {
        if (!OperatingSystem.IsWindows()) return;

        var platform = new WindowsProcessPlatform();
        using var dir = new TempDir();
        using var probe = new Probe("start failure (missing working directory)");
        string missing = Path.Combine(dir.Path, "no-such-directory");
        HashSet<int> cmdBefore = ProcessIds("cmd");

        int exitCalls = 0;
        LaunchedTree? unexpected = null;
        Win32Exception? error = null;
        Exception? other = null;
        try
        {
            unexpected = platform.Launch(platform.Prepare(Cmd("echo hi", missing)), _ => Interlocked.Increment(ref exitCalls));
        }
        catch (Win32Exception ex)
        {
            error = ex;
        }
        catch (Exception ex)
        {
            other = ex;
        }

        try
        {
            // Diagnostics only: other test classes may run other processes, so this list is never asserted on.
            List<int> newCmd = ProcessIds("cmd").Where(id => !cmdBefore.Contains(id)).ToList();
            probe.AddNote($"cmd processes that appeared during the failed launch (machine-wide, not asserted): {string.Join(", ", newCmd)}");

            Require(
                error is not null,
                probe,
                $"Launch did not throw Win32Exception; it threw {other?.GetType().Name ?? (unexpected is null ? "nothing" : "no exception, a tree was returned")}: {other?.Message}");
            Require(
                error!.Message.Contains(missing, StringComparison.OrdinalIgnoreCase),
                probe,
                $"the Win32Exception message does not name the working directory: \"{error.Message}\"");

            await Task.Delay(300);
            Require(exitCalls == 0, probe, $"onExited was called {exitCalls} time(s) for a launch that failed");
        }
        finally
        {
            if (unexpected is not null)
            {
                unexpected.Group.Dispose();
                unexpected.Process.Dispose();
            }
        }
    }

    [Fact]
    public async Task ConsoleStdinGivesNullStdinAndRunsToCompletion()
    {
        if (!OperatingSystem.IsWindows()) return;

        var platform = new WindowsProcessPlatform();
        using var dir = new TempDir();
        using var probe = new Probe("console stdin");

        LaunchedTree tree = probe.Start(platform, Cmd("echo ok", dir.Path) with { Stdin = StdinMode.Console });
        Require(tree.Process.StandardInput is null, probe, "StandardInput is not null for StdinMode.Console");
        Task stdout = probe.DrainStdout(tree.Process.StandardOutput);
        Task stderr = probe.DrainStderr(tree.Process.StandardError);

        Require(await probe.ExitedWithin(ExitTimeout), probe, "leader did not exit within 10 s");
        Require(await Within(Task.WhenAll(stdout, stderr), ExitTimeout), probe, "output did not reach EOF within 10 s");
        Require(!probe.ReadFailed, probe, "a read failed (see stdout/stderr read errors)");
        Require(tree.Process.ExitCode == 0, probe, $"ExitCode is {tree.Process.ExitCode}, expected 0");
        Require(probe.StdoutLines.Contains("ok"), probe, "stdout does not contain the line \"ok\"");
    }

    /// <summary>
    /// Starts <c>start /b ping</c> from a leader that exits at once, then kills or disposes the group. Every PING the job holds
    /// must exit within 5 s of that. Every PING started after the probe began is tracked for cleanup, whether or not it is in the job.
    /// </summary>
    private static async Task EarlyChildRunAsync(string label, bool disposeGroup)
    {
        var platform = new WindowsProcessPlatform();
        using var dir = new TempDir();
        using var probe = new Probe(label);

        LaunchedTree tree = probe.Start(platform, Cmd("start /b ping -n 20 127.0.0.1 >nul & exit /b 0", dir.Path));
        _ = probe.DrainStdout(tree.Process.StandardOutput);
        _ = probe.DrainStderr(tree.Process.StandardError);

        Require(await probe.ExitedWithin(ExitTimeout), probe, "leader did not exit within 10 s");
        Require(tree.Process.ExitCode == 0, probe, $"leader exit code is {tree.Process.ExitCode}, expected 0");

        List<Process> pings = probe.TrackPingsInJob();
        Require(pings.Count >= 1, probe, "no PING started after the launch is in the job (is the child created in the job?)");
        Require((probe.LiveCount() ?? 0) >= 1, probe, "the job reports no live process while the PING runs");

        string verb = disposeGroup ? "Group.Dispose" : "Group.Kill";
        if (disposeGroup)
        {
            probe.Act(verb, () => tree.Group.Dispose());
        }
        else
        {
            probe.Act(verb, () => tree.Group.Kill());
        }

        foreach (Process ping in pings)
        {
            Require(
                ping.WaitForExit((int)KillTimeout.TotalMilliseconds),
                probe,
                $"PING pid {ping.Id} (in the job) still running 5 s after {verb}");
        }

        if (!disposeGroup)
        {
            Require(
                await PollAsync(() => probe.LiveCount() == 0, KillTimeout),
                probe,
                "the job live count did not reach 0 within 5 s after Group.Kill");
        }
    }

    private static LaunchSpec Cmd(string command, string workingDirectory) =>
        Exec("cmd", new[] { "/c", command }, workingDirectory);

    /// <summary>Fails the test with the probe's diagnostic dump when <paramref name="condition"/> is false.</summary>
    private static void Require(bool condition, Probe probe, string step)
    {
        if (!condition)
        {
            Assert.Fail(probe.Diagnose(step));
        }
    }

    /// <summary>
    /// True when <paramref name="task"/> completes within <paramref name="timeout"/>. A faulted task is not a success: once it has
    /// completed, it is awaited, so its exception surfaces.
    /// </summary>
    private static async Task<bool> Within(Task task, TimeSpan timeout)
    {
        Task winner = await Task.WhenAny(task, Task.Delay(timeout));
        if (winner != task)
        {
            return false;
        }

        await task;
        return true;
    }

    private static async Task<bool> PollAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(50);
        }

        return true;
    }

    private static HashSet<int> ProcessIds(string name)
    {
        var ids = new HashSet<int>();
        foreach (Process p in Process.GetProcessesByName(name))
        {
            using (p)
            {
                ids.Add(p.Id);
            }
        }

        return ids;
    }

    /// <summary>
    /// One launch under test: its exit, its output, the PING processes it tracks, and the diagnostic dump. Disposing it kills what it
    /// still tracks by the process handles it opened, independently of the job.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private sealed class Probe : IDisposable
    {
        private static readonly string[] DiagnosedNames = { "ping", "cmd", "conhost" };

        private readonly string _name;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly DateTime _since = DateTime.Now.AddSeconds(-2); // Process.StartTime is local time
        private readonly object _sync = new();
        private readonly StringBuilder _stdout = new();
        private readonly StringBuilder _stderr = new();
        private readonly List<string> _readErrors = new();
        private readonly List<string> _notes = new();
        private readonly List<PingRecord> _pings = new();
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ILaunchedProcess? _leader;
        private WindowsProcessGroup? _group;
        private ILaunchedProcess? _exitInstance;
        private int _exitCalls;
        private long _exitedAtMs = -1;
        private long _stdoutEofAtMs = -1;

        internal Probe(string name)
        {
            _name = name;
        }

        /// <summary>A PING process started after the probe began, with its handle kept open. <c>InJob</c> is "true", "false" or the error.</summary>
        private sealed record PingRecord(Process Process, int Pid, string Start, string InJob);

        internal bool HasExited => _exited.Task.IsCompleted;

        internal int ExitCalls => Volatile.Read(ref _exitCalls);

        internal ILaunchedProcess? ExitInstance => _exitInstance;

        /// <summary>Milliseconds since the probe was created when onExited ran, or -1.</summary>
        internal long ExitedAtMs => Volatile.Read(ref _exitedAtMs);

        /// <summary>Milliseconds since the probe was created when stdout reached a successful EOF, or -1.</summary>
        internal long StdoutEofAtMs => Volatile.Read(ref _stdoutEofAtMs);

        /// <summary>True when a stdout or stderr read failed (an I/O error, not EOF). Tests that need output or EOF must check it.</summary>
        internal bool ReadFailed
        {
            get
            {
                lock (_sync)
                {
                    return _readErrors.Count > 0;
                }
            }
        }

        internal string StdoutText
        {
            get
            {
                lock (_sync)
                {
                    return _stdout.ToString();
                }
            }
        }

        internal string StderrText
        {
            get
            {
                lock (_sync)
                {
                    return _stderr.ToString();
                }
            }
        }

        internal IReadOnlyList<string> StdoutLines => Lines(StdoutText);

        internal IReadOnlyList<string> StderrLines => Lines(StderrText);

        /// <summary>Starts the launch and records its leader and group for the diagnostics.</summary>
        internal LaunchedTree Start(WindowsProcessPlatform platform, LaunchSpec spec)
        {
            LaunchedTree tree = platform.Launch(platform.Prepare(spec), OnExited);
            _leader = tree.Process;
            _group = (WindowsProcessGroup)tree.Group;
            return tree;
        }

        internal void AddNote(string note)
        {
            lock (_sync)
            {
                _notes.Add(note);
            }
        }

        internal Task<bool> ExitedWithin(TimeSpan timeout) => Within(_exited.Task, timeout);

        internal Task DrainStdout(StreamReader reader) => Drain(reader, isStdErr: false);

        internal Task DrainStderr(StreamReader reader) => Drain(reader, isStdErr: true);

        /// <summary>The job's live process count, or null when it cannot be read (the reason is in the diagnostic).</summary>
        internal int? LiveCount()
        {
            WindowsProcessGroup? group = _group;
            if (group is null)
            {
                return null;
            }

            try
            {
                return group.CountLiveProcesses();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Tracks every PING process started since this probe began (by StartTime), keeping its handle open so it can be watched and
        /// killed later, and returns those that are in the job. The ones outside the job stay tracked and are listed in the diagnostic.
        /// </summary>
        internal List<Process> TrackPingsInJob()
        {
            WindowsProcessGroup group = _group ?? throw new InvalidOperationException("The launch has not started.");
            var inJob = new List<Process>();
            foreach (Process p in Process.GetProcesses())
            {
                bool tracked = false;
                try
                {
                    if (string.Equals(p.ProcessName, "PING", StringComparison.OrdinalIgnoreCase) && p.StartTime >= _since)
                    {
                        tracked = true;
                        _ = p.Handle; // opened now, so the exit can still be observed after the process is killed
                        string start = p.StartTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                        string membership;
                        try
                        {
                            membership = IsInJob(p, group) ? "true" : "false";
                        }
                        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                        {
                            membership = $"error({ex.Message})";
                        }

                        lock (_sync)
                        {
                            _pings.Add(new PingRecord(p, p.Id, start, membership));
                        }

                        if (membership == "true")
                        {
                            inJob.Add(p);
                        }
                    }
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // Gone or not readable before it could be tracked.
                }

                if (!tracked)
                {
                    p.Dispose();
                }
            }

            return inJob;
        }

        /// <summary>Runs <paramref name="action"/>; a throw fails the test with the diagnostic dump.</summary>
        internal void Act(string step, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Assert.Fail(Diagnose($"{step} threw {ex.GetType().Name}: {ex.Message}"));
            }
        }

        /// <summary>Multi-line dump: the step, timings, the leader, the job, the PINGs tracked, the processes involved, and the captured output.</summary>
        internal string Diagnose(string step)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{_name}] FAILED AT: {step}");
            sb.AppendLine($"  elapsed since probe began: {_clock.ElapsedMilliseconds} ms");

            ILaunchedProcess? leader = _leader;
            if (leader is null)
            {
                sb.AppendLine("  leader: not started");
            }
            else
            {
                string exitCode = HasExited ? Describe(() => leader.ExitCode) : "n/a";
                sb.AppendLine(
                    $"  leader: pid={leader.Id} exited={HasExited} exitCode={exitCode} onExitedCalls={ExitCalls} " +
                    $"onExitedAt={ExitedAtMs} ms stdoutEofAt={(StdoutEofAtMs < 0 ? "not yet" : StdoutEofAtMs + " ms")}");
            }

            WindowsProcessGroup? group = _group;
            if (group is null)
            {
                sb.AppendLine("  job: none");
            }
            else
            {
                string live = group.Job.IsClosed ? "0 (job closed)" : Describe(() => group.CountLiveProcesses());
                sb.AppendLine($"  job: CountLiveProcesses={live} jobClosed={group.Job.IsClosed}");
            }

            List<PingRecord> pings;
            List<string> readErrors;
            List<string> notes;
            lock (_sync)
            {
                pings = _pings.ToList();
                readErrors = _readErrors.ToList();
                notes = _notes.ToList();
            }

            sb.AppendLine($"  PINGs tracked since probe began ({pings.Count}; inJob=false ones are outside the job):");
            foreach (PingRecord record in pings)
            {
                sb.AppendLine(
                    $"    pid={record.Pid} start={record.Start} inJob={record.InJob} exited={Describe(() => record.Process.HasExited)}");
            }

            sb.AppendLine("  processes named ping, cmd or conhost started since the probe began:");
            int listed = 0;
            foreach (Process p in Process.GetProcesses())
            {
                using (p)
                {
                    string name = Describe(() => p.ProcessName);
                    if (!DiagnosedNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    DateTime? start = null;
                    try
                    {
                        start = p.StartTime;
                    }
                    catch (Exception)
                    {
                        // Not readable (gone, or no access): listed below without a start time.
                    }

                    if (start is not null && start < _since)
                    {
                        continue;
                    }

                    listed++;
                    string startText = start is null ? "unknown" : start.Value.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                    sb.AppendLine(
                        $"    pid={p.Id} name={name} start={startText} exited={Describe(() => p.HasExited)} " +
                        $"inJob={JobMembership(p, group)}");
                }
            }

            if (listed == 0)
            {
                sb.AppendLine("    (none)");
            }

            foreach (string error in readErrors)
            {
                sb.AppendLine($"  read error: {error}");
            }

            foreach (string note in notes)
            {
                sb.AppendLine($"  note: {note}");
            }

            sb.AppendLine($"  stdout: {Quote(StdoutText)}");
            sb.AppendLine($"  stderr: {Quote(StderrText)}");
            return sb.ToString();
        }

        /// <summary>Kills what the probe tracks: the job first, then each PING it tracked that is still alive, then the leader's handle.</summary>
        public void Dispose()
        {
            try
            {
                _group?.Dispose();
            }
            catch (Exception)
            {
                // Best effort: the processes are killed below by handle.
            }

            List<PingRecord> pings;
            lock (_sync)
            {
                pings = _pings.ToList();
                _pings.Clear();
            }

            foreach (PingRecord record in pings)
            {
                Process p = record.Process;
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill();
                    }
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // Already gone.
                }

                p.Dispose();
            }

            _leader?.Dispose();
        }

        private void OnExited(ILaunchedProcess process)
        {
            Interlocked.Increment(ref _exitCalls);
            _exitInstance = process;
            Volatile.Write(ref _exitedAtMs, _clock.ElapsedMilliseconds);
            _exited.TrySetResult();
        }

        /// <summary>
        /// Reads until EOF on a background thread. A successful EOF sets the EOF time (stdout only); an I/O error is recorded
        /// separately and is not EOF.
        /// </summary>
        private Task Drain(StreamReader reader, bool isStdErr) => Task.Run(() =>
        {
            var buffer = new char[1024];
            try
            {
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    Append(isStdErr, buffer.AsSpan(0, read).ToString());
                }

                if (!isStdErr)
                {
                    Volatile.Write(ref _stdoutEofAtMs, _clock.ElapsedMilliseconds);
                }
            }
            catch (IOException ex)
            {
                lock (_sync)
                {
                    _readErrors.Add($"{(isStdErr ? "stderr" : "stdout")}: {ex.Message}");
                }

                Append(isStdErr, $"[read failed: {ex.Message}]");
            }
            finally
            {
                reader.Dispose();
            }
        });

        private void Append(bool isStdErr, string text)
        {
            lock (_sync)
            {
                StringBuilder target = isStdErr ? _stderr : _stdout;
                target.Append(text);
            }
        }

        private static bool IsInJob(Process p, WindowsProcessGroup group)
        {
            int ok = WindowsNative.IsProcessInJob(p.Handle, group.Job.DangerousGetHandle(), out int result);
            if (ok == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "IsProcessInJob failed");
            }

            return result != 0;
        }

        private static string JobMembership(Process p, WindowsProcessGroup? group)
        {
            if (group is null)
            {
                return "no job";
            }

            if (group.Job.IsClosed)
            {
                return "job closed";
            }

            return Describe(() => IsInJob(p, group));
        }

        private static string Describe<T>(Func<T> read)
        {
            try
            {
                return Convert.ToString(read(), CultureInfo.InvariantCulture) ?? "null";
            }
            catch (Exception ex)
            {
                return $"error({ex.GetType().Name}: {ex.Message})";
            }
        }

        private static IReadOnlyList<string> Lines(string text) =>
            text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();

        private static string Quote(string text)
        {
            const int Limit = 1500;
            string shown = text.Length <= Limit ? text : "..." + text[^Limit..];
            return "\"" + shown.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + "\"";
        }
    }
}
