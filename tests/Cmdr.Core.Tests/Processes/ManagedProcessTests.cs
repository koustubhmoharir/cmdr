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
            p.Start(Sh("trap \"echo got-int; exit 3\" INT; while :; do sleep 0.1; done", dir.Path));
            WaitUntil(() => p.Snapshot.State == ProcessState.Running, "running", p);
            Thread.Sleep(200); // Let the trap be installed.

            p.RequestStop();
            ProcessSnapshot stopping = p.Snapshot;
            Assert.Equal(ProcessState.Stopping, stopping.State);
            Assert.NotNull(stopping.StopRequestedAt);

            WaitForExited(p);
            Assert.Equal(3, p.Snapshot.ExitCode);
            Assert.Equal(0, p.Snapshot.LeftoverCount);
            Assert.Contains("got-int", Text(p));
            Assert.Equal(stopping.StopRequestedAt, p.Snapshot.StopRequestedAt);
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
            Assert.Contains("got-q", Text(p));
            Assert.Contains("stop requested (input)", Text(p));
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
            p.Start(Sh("sleep 30 & sleep 30", dir.Path));
            WaitUntil(() => p.Snapshot.State == ProcessState.Running, "running", p);
            Thread.Sleep(200); // Let both sleeps start.

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
            p.Start(new LaunchSpec("cmdr-no-such-program-xyz", Array.Empty<string>(), dir.Path, new Dictionary<string, string?>(), StopMethod.CtrlC, null));
            WaitForExited(p);

            Assert.NotEqual(0, p.Snapshot.ExitCode);
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
    public async Task RestartStopsAndStartsANewRun()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            LaunchSpec spec = Sh("while :; do sleep 0.1; done", dir.Path);
            p.Start(spec);
            WaitUntil(() => p.Snapshot.State == ProcessState.Running, "running", p);
            int? firstPid = p.Snapshot.Pid;
            Thread.Sleep(200); // Let the trap-less loop settle into sleep.

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await p.RestartAsync(spec, cts.Token);

            Assert.Equal(ProcessState.Running, p.Snapshot.State);
            Assert.NotEqual(firstPid, p.Snapshot.Pid);
            Assert.Equal(2, Lines(p).Count(l => l.PlainText.StartsWith("=== started ", StringComparison.Ordinal)));
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
            p.Start(Sh("exit 0", dir.Path));
            WaitForExited(p);

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
}
