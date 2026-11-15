using System.Collections.Concurrent;
using Cmdr.Core.Config;
using Cmdr.Core.Output;
using Cmdr.Core.Processes;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;

namespace Cmdr.Core.Tests.Processes;

/// <summary>End-to-end tests of <see cref="ManagedProcess"/> against real processes (Linux only).</summary>
public class ManagedProcessTests
{
    private const int BufferLines = 1000;

    /// <summary>
    /// Starts a background sleep and prints "ready" once it ignores SIGINT, as non-interactive sh arranges for background jobs.
    /// The shell sets that in the forked child, which may run after the shell's next command, so the script waits for bit 2 of
    /// the child's SigIgn mask (a last hex digit of 2, 3, 6, 7, a, b, e or f).
    /// </summary>
    private const string BackgroundSleepIgnoringCtrlC =
        "sleep 30 & bg=$!; while ! grep -q '^SigIgn:.*[2367abef]$' /proc/$bg/status; do sleep 0.01; done; echo ready";

    [Fact]
    public void CapturesStdoutStderrAndExitCode()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            p.Start(Sh("echo hello; echo err 1>&2; exit 4", dir.Path));
            WaitForExited(p);

            ProcessSnapshot snap = p.Snapshot;
            Assert.Equal(ProcessState.Exited, snap.State);
            Assert.Equal(4, snap.ExitCode);
            Assert.NotNull(snap.StartedAt);
            Assert.NotNull(snap.ExitedAt);
            Assert.NotNull(snap.Pid);
            Assert.Equal(0, snap.LeftoverCount);

            IReadOnlyList<OutputLine> lines = Lines(p);
            Assert.Contains(lines, l => l.Source == OutputSource.StdOut && l.PlainText == "hello");
            Assert.Contains(lines, l => l.Source == OutputSource.StdErr && l.PlainText == "err");
            Assert.Contains(lines, l => l.Source == OutputSource.System && l.PlainText.StartsWith("=== started ", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Source == OutputSource.System && l.PlainText == "=== exited with code 4 ===");
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void AppliesWorkingDirectoryAndEnvironmentOverlay()
    {
        if (!OperatingSystem.IsLinux()) return;

        const string removedVar = "CMDR_TEST_REMOVED";
        string? previous = Environment.GetEnvironmentVariable(removedVar);
        Environment.SetEnvironmentVariable(removedVar, "present");
        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            var env = new Dictionary<string, string?> { ["FOO"] = "bar", [removedVar] = null };
            p.Start(Sh("pwd; echo \"$FOO\"; echo \"${CMDR_TEST_REMOVED-unset}\"", dir.Path, env));
            WaitForExited(p);
            WaitForText(p, "unset"); // last line printed

            string[] texts = Lines(p).Where(l => l.Source == OutputSource.StdOut).Select(l => l.PlainText).ToArray();
            Assert.Contains(texts, t => t == Path.GetFileName(dir.Path) || t.EndsWith(Path.GetFileName(dir.Path), StringComparison.Ordinal));
            Assert.Contains("bar", texts);
            Assert.Contains("unset", texts);
        }
        finally
        {
            Cleanup(p);
            Environment.SetEnvironmentVariable(removedVar, previous);
        }
    }

    [Fact]
    public void PartialOutputIsVisibleWhileRunning()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            p.Start(Sh("printf abc; sleep 5", dir.Path));
            WaitUntil(
                () => Lines(p).Any(l => l.Source == OutputSource.StdOut && l.PlainText == "abc"),
                "partial line 'abc' to appear while running",
                p);

            Assert.Equal(ProcessState.Running, p.Snapshot.State);

            p.ForceKill();
            WaitForExited(p);
            Assert.Equal(ProcessState.Exited, p.Snapshot.State);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void CtrlCStopInterruptsTheProcessTree()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            p.Start(Sh("trap \"echo got-int; exit 3\" INT; echo ready; while :; do sleep 0.1; done", dir.Path));
            WaitUntil(() => p.Snapshot.State == ProcessState.Running, "running", p);
            // Wait for the trap to be installed; an earlier Ctrl+C would kill the shell with the default action.
            WaitUntil(() => Text(p).Contains("ready", StringComparison.Ordinal), "shell ready", p);

            p.RequestStop();
            ProcessSnapshot stopping = p.Snapshot;
            // The signal is delivered inside RequestStop, so the shell may already have exited and been recorded.
            Assert.True(stopping.State is ProcessState.Stopping or ProcessState.Exited, $"state after RequestStop: {stopping.State}");
            Assert.NotNull(stopping.StopRequestedAt);

            WaitForExited(p);
            Assert.Equal(3, p.Snapshot.ExitCode);
            Assert.Equal(0, p.Snapshot.LeftoverCount);
            // The exit marker can be written before the trap's output is pumped (the drain is bounded), so wait for it.
            WaitUntil(() => Text(p).Contains("got-int", StringComparison.Ordinal), "trap output 'got-int'", p);
            Assert.Contains("got-int", Text(p));
            Assert.Equal(stopping.StopRequestedAt, p.Snapshot.StopRequestedAt);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void GracefulStopIsRecordedInTheSnapshotAfterExit()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            p.Start(Sh("trap \"exit 3\" INT; echo ready; while :; do sleep 0.1; done", dir.Path));
            WaitUntil(() => p.Snapshot.State == ProcessState.Running, "running", p);
            WaitUntil(() => Text(p).Contains("ready", StringComparison.Ordinal), "shell ready", p);

            Assert.False(p.Snapshot.StopRequested);
            p.RequestStop();
            WaitForExited(p);

            Assert.Equal(3, p.Snapshot.ExitCode);
            Assert.True(p.Snapshot.StopRequested);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void ForceKillIsRecordedInTheSnapshotAfterExit()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            p.Start(Sh("echo ready; while :; do sleep 0.1; done", dir.Path));
            WaitUntil(() => Text(p).Contains("ready", StringComparison.Ordinal), "shell ready", p);

            p.ForceKill();
            WaitForExited(p);

            Assert.True(p.Snapshot.StopRequested);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void NonZeroExitWithoutStopIsNotStopRequested()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            p.Start(Sh("exit 3", dir.Path));
            WaitForExited(p);

            Assert.Equal(3, p.Snapshot.ExitCode);
            Assert.False(p.Snapshot.StopRequested);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void StopRequestedIsResetByANewRun()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            p.Start(Sh("echo ready; while :; do sleep 0.1; done", dir.Path));
            WaitUntil(() => Text(p).Contains("ready", StringComparison.Ordinal), "shell ready", p);
            p.ForceKill();
            WaitForExited(p);
            Assert.True(p.Snapshot.StopRequested);

            p.Start(Sh("exit 0", dir.Path));
            WaitForExited(p);

            Assert.Equal(0, p.Snapshot.ExitCode);
            Assert.False(p.Snapshot.StopRequested);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void InputStopWritesToStdin()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            p.Start(Sh("read x; echo \"got-$x\"", dir.Path, stopMethod: StopMethod.Input, stopInput: "q\n"));
            WaitUntil(() => p.Snapshot.State == ProcessState.Running, "running", p);

            p.RequestStop();
            WaitForExited(p);

            Assert.Equal(0, p.Snapshot.ExitCode);
            WaitForText(p, "got-q");
            Assert.Contains("got-q", Text(p));
            Assert.Contains("stop requested (input)", Text(p));
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void ConsoleStdinIsIgnoredOnLinuxSoInputStopStillWritesToThePipe()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            LaunchSpec spec = Sh("read x; echo \"got-$x\"", dir.Path, stopMethod: StopMethod.Input, stopInput: "q\n") with { Stdin = StdinMode.Console };
            p.Start(spec);
            WaitUntil(() => p.Snapshot.State == ProcessState.Running, "running", p);

            p.RequestStop();
            WaitForExited(p);

            Assert.Equal(0, p.Snapshot.ExitCode);
            WaitForText(p, "got-q");
            Assert.DoesNotContain("stop input failed", Text(p));
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void BackgroundChildLeftoverBlocksStartUntilForceKilled()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            LaunchSpec spec = Sh("sleep 30 & echo started", dir.Path);
            p.Start(spec);
            WaitUntil(() => p.Snapshot.State == ProcessState.Exited, "leader to exit", p);

            WaitUntil(() => p.Snapshot.LeftoverCount == 1, "one leftover process", p);
            Assert.False(p.CanStart);
            Assert.Throws<InvalidOperationException>(() => p.Start(spec));

            p.ForceKill();
            WaitUntil(() => p.Snapshot.LeftoverCount == 0, "leftovers to clear after force kill", p, TimeSpan.FromSeconds(5));
            Assert.True(p.CanStart);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void CtrlCReachesChildrenAndBackgroundChildIgnoresIt()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            p.Start(Sh(BackgroundSleepIgnoringCtrlC + "; sleep 30", dir.Path));
            WaitUntil(() => p.Snapshot.State == ProcessState.Running, "running", p);
            // Wait until the background sleep ignores Ctrl+C; an earlier Ctrl+C would kill the shell itself.
            WaitUntil(() => Text(p).Contains("ready", StringComparison.Ordinal), "shell ready", p);

            p.RequestStop();
            WaitUntil(() => p.Snapshot.State == ProcessState.Exited, "leader to exit after Ctrl+C", p);

            // Non-interactive sh gives background jobs SIG_IGN for SIGINT, so the background sleep survives.
            WaitUntil(() => p.Snapshot.LeftoverCount == 1, "background sleep to remain", p);

            p.ForceKill();
            WaitUntil(() => p.Snapshot.LeftoverCount == 0, "leftovers to clear after force kill", p, TimeSpan.FromSeconds(5));
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void MissingWorkingDirectoryFailsToStartWithoutThrowing()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            string missing = Path.Combine(dir.Path, "does-not-exist");
            p.Start(Sh("echo never", missing));

            ProcessSnapshot snap = p.Snapshot;
            Assert.Equal(ProcessState.FailedToStart, snap.State);
            Assert.False(string.IsNullOrEmpty(snap.Error));
            Assert.Null(snap.Pid);
            Assert.True(p.CanStart);
            Assert.Contains(Lines(p), l => l.Source == OutputSource.System && l.PlainText == $"=== failed to start: {p.Snapshot.Error} ===");
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void NonexistentProgramExitsNonZeroWithStderr()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            p.Start(Exec("cmdr-no-such-program-xyz", Array.Empty<string>(), dir.Path));
            WaitForExited(p);

            Assert.NotEqual(0, p.Snapshot.ExitCode);
            WaitUntil(() => Lines(p).Any(l => l.Source == OutputSource.StdErr && l.PlainText.Length > 0), "stderr output", p);
            Assert.Contains(Lines(p), l => l.Source == OutputSource.StdErr && l.PlainText.Length > 0);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void StartWhileRunningThrows()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            LaunchSpec spec = Sh("sleep 5", dir.Path);
            p.Start(spec);
            WaitUntil(() => p.Snapshot.State == ProcessState.Running, "running", p);

            Assert.Throws<InvalidOperationException>(() => p.Start(spec));
            Assert.Equal(ProcessState.Running, p.Snapshot.State);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void ChangedFiresOnStartAndExit()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        var seen = new ConcurrentQueue<ProcessState>();
        p.Changed += q => seen.Enqueue(q.Snapshot.State);
        try
        {
            // Changed carries no state; handlers read the current snapshot. The process must outlive Start's own
            // Changed notification for "Running" to be observable.
            p.Start(Sh("sleep 0.3", dir.Path));
            WaitForExited(p);
            // The exit marker is written before Changed is raised for Exited, so wait for that notification too.
            WaitUntil(() => seen.Contains(ProcessState.Exited), "Changed for exit", p);

            ProcessState[] states = seen.ToArray();
            Assert.Contains(ProcessState.Running, states);
            Assert.Contains(ProcessState.Exited, states);
            Assert.True(Array.IndexOf(states, ProcessState.Running) < Array.LastIndexOf(states, ProcessState.Exited));
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public async Task RapidShortRunsAlwaysEndExitedWithCodeZero()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            LaunchSpec spec = Sh("exit 0", dir.Path);
            for (int i = 0; i < 20; i++)
            {
                p.Start(spec);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await p.WaitForExitAsync(cts.Token);

                ProcessSnapshot snap = p.Snapshot;
                Assert.True(snap.State == ProcessState.Exited, $"run {i}: state was {snap.State}");
                Assert.Equal(0, snap.ExitCode);
            }
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void StopImmediatelyAfterStartIsNotLost()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // Regression: Process.Start returns before setsid has created the group; a group signal sent in that
        // window used to fail with ESRCH and be dropped silently.
        using var dir = new TempDir();
        for (int i = 0; i < 20; i++)
        {
            var p = new ManagedProcess(100);
            try
            {
                p.Start(Exec("sleep", new[] { "30" }, dir.Path));
                p.RequestStop();
                WaitForExited(p, TimeSpan.FromSeconds(5));
            }
            finally
            {
                Cleanup(p);
            }
        }
    }

    [Fact]
    public void DetachedLeftoversAreNotTrackedAndDoNotBlockStart()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        int? leaderPid = null;
        try
        {
            LaunchSpec spec = Sh("sleep 30 & echo started", dir.Path) with { Leftovers = LeftoverMode.Detach };
            p.Start(spec);
            WaitForExited(p);
            leaderPid = p.Snapshot.Pid;

            Assert.Equal(ProcessState.Exited, p.Snapshot.State);
            Assert.Equal(0, p.Snapshot.ExitCode);
            Assert.Equal(0, p.Snapshot.LeftoverCount);
            Assert.True(p.CanStart);

            // The message must come before the end marker, which is written last.
            IReadOnlyList<OutputLine> lines = Lines(p);
            int detached = lines.ToList().FindIndex(l => l.PlainText == "1 leftover process(es) detached");
            int exited = lines.ToList().FindIndex(l => l.PlainText == "=== exited with code 0 ===");
            Assert.True(detached >= 0, "detach message is missing");
            Assert.True(detached < exited, "detach message must precede the end marker");

            // Nothing polls the detached group, so the count stays at zero.
            Thread.Sleep(1200);
            Assert.Equal(0, p.Snapshot.LeftoverCount);

            p.Start(Sh("exit 0", dir.Path));
            WaitForExited(p);
            Assert.Equal(0, p.Snapshot.ExitCode);
        }
        finally
        {
            if (leaderPid is int pgid)
            {
                // The detached sleep is in the group whose id is the leader's pid (setsid). Kill it so it does not leak.
                var group = new LinuxProcessGroup(pgid);
                group.Kill();
                WaitUntil(() => group.CountLiveProcesses() == 0, "detached process to die", timeout: TimeSpan.FromSeconds(5));
            }

            Cleanup(p);
        }
    }

    [Fact]
    public void GracefulStopStillDetachesLeftovers()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        int? leaderPid = null;
        try
        {
            LaunchSpec spec = Sh(BackgroundSleepIgnoringCtrlC + "; sleep 30", dir.Path) with { Leftovers = LeftoverMode.Detach };
            p.Start(spec);
            WaitUntil(() => p.Snapshot.State == ProcessState.Running, "running", p);
            // Wait until the background sleep ignores Ctrl+C; an earlier Ctrl+C would kill the shell itself.
            WaitUntil(() => Text(p).Contains("ready", StringComparison.Ordinal), "shell ready", p);
            leaderPid = p.Snapshot.Pid;

            p.RequestStop();
            WaitUntil(() => p.Snapshot.State == ProcessState.Exited, "leader to exit after Ctrl+C", p);

            // The background sleep ignores Ctrl+C. A graceful stop still detaches it, so it is no longer tracked.
            WaitUntil(() => Text(p).Contains("1 leftover process(es) detached", StringComparison.Ordinal), "detach message", p);
            Assert.Equal(0, p.Snapshot.LeftoverCount);
            Assert.True(p.Snapshot.StopRequested);
            Assert.True(p.CanStart);

            IReadOnlyList<OutputLine> lines = Lines(p);
            int detached = lines.ToList().FindIndex(l => l.PlainText == "1 leftover process(es) detached");
            int exited = lines.ToList().FindIndex(l => l.PlainText.StartsWith("=== exited", StringComparison.Ordinal));
            Assert.True(detached >= 0 && detached < exited, "detach message must precede the end marker");
        }
        finally
        {
            if (leaderPid is int pgid)
            {
                // The detached sleep is in the group whose id is the leader's pid (setsid). Kill it so it does not leak.
                var group = new LinuxProcessGroup(pgid);
                group.Kill();
                WaitUntil(() => group.CountLiveProcesses() == 0, "detached process to die", timeout: TimeSpan.FromSeconds(5));
            }

            Cleanup(p);
        }
    }

    [Fact]
    public void ForceKillPreventsDetachingLeftovers()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            LaunchSpec spec = Sh("sleep 30 & echo ready; sleep 30", dir.Path) with { Leftovers = LeftoverMode.Detach };
            p.Start(spec);
            WaitUntil(() => p.Snapshot.State == ProcessState.Running, "running", p);
            WaitUntil(() => Text(p).Contains("ready", StringComparison.Ordinal), "shell ready", p);

            // A force kill during the run means "kill everything". On Linux it signals the whole process group, so the
            // background sleep is killed along with the leader and there is nothing left to detach. The assertion that
            // matters is that no detach happened: the group is gone, so the leftover count cannot be used to observe it.
            p.ForceKill();
            WaitUntil(() => p.Snapshot.State == ProcessState.Exited, "leader to exit after force kill", p, TimeSpan.FromSeconds(5));
            WaitUntil(() => p.Snapshot.LeftoverCount == 0, "leftovers to clear after force kill", p, TimeSpan.FromSeconds(5));

            Assert.DoesNotContain("detached", Text(p));
            Assert.True(p.Snapshot.StopRequested);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void ForceKillForAStaleRunIdDoesNothingToANewerRun_AndForCurrentRunIdKillsIt()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            LaunchSpec spec = Sh("echo ready; while :; do sleep 0.1; done", dir.Path);
            p.Start(spec);
            WaitUntil(() => Text(p).Contains("ready", StringComparison.Ordinal), "first run ready", p);
            int staleRunId = p.RunId;

            p.ForceKill();
            WaitForExited(p);

            p.Start(spec);
            WaitUntil(() => p.Snapshot.State == ProcessState.Running && Text(p).Contains("ready", StringComparison.Ordinal), "second run ready", p);
            int currentRunId = p.RunId;
            Assert.NotEqual(staleRunId, currentRunId);

            // The stale run ended: killing "that run" must not touch the newer one.
            p.ForceKill(staleRunId);
            Thread.Sleep(500);
            Assert.Equal(ProcessState.Running, p.Snapshot.State);
            Assert.False(p.Snapshot.StopRequested);
            Assert.Equal(currentRunId, p.RunId);

            p.ForceKill(currentRunId);
            WaitForExited(p);
            Assert.True(p.Snapshot.StopRequested);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void LaunchWithoutItsOwnGroupIsAFailedStartAndItsLateExitIsIgnored()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        int leaderPid = 0;
        var platform = new LinuxProcessPlatform(TimeSpan.FromMilliseconds(200), pid =>
        {
            leaderPid = pid;
            return 0;
        });
        var p = new ManagedProcess(BufferLines, platform);
        try
        {
            p.Start(Exec("/bin/sleep", new[] { "30" }, dir.Path));
            Assert.Equal(ProcessState.FailedToStart, p.Snapshot.State);
            Assert.Contains("own process group", Text(p), StringComparison.Ordinal);
            Assert.True(p.CanStart);

            // The killed leader's exit callback may run; it must not record an exit for the failed run.
            WaitUntil(() => LinuxProcessGroupTests.IsProcessGone(leaderPid), "failed leader to be killed", timeout: TimeSpan.FromSeconds(5));
            Thread.Sleep(200);
            Assert.Equal(ProcessState.FailedToStart, p.Snapshot.State);
            Assert.DoesNotContain("=== exited", Text(p), StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(p);
        }
    }
}
