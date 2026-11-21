using Cmdr.Core.Output;
using Cmdr.Core.Processes;
using Cmdr.Core.Tests.Config;
using Shared.Processes;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;

namespace Cmdr.Core.Tests.Processes;

/// <summary>
/// Review 02-02 and 02-03: each run owns its pipes. Output of an earlier run never reaches a later one, and disposal closes
/// every pipe and stops output (Linux only).
/// </summary>
public class ManagedProcessRunPipesTests
{
    private const int BufferLines = 1000;

    /// <summary>Shell snippet that waits until <paramref name="file"/> exists.</summary>
    private static string WaitFor(string file) => $"while [ ! -e {file} ]; do sleep 0.05; done";

    [Fact]
    public void LeftoverOfAnEarlierRunCannotWriteIntoOrReadyTheNextRun()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        int? oldLeader = null;
        try
        {
            // The background child holds the run's stdout and stderr and writes a ready match once the next run is going.
            string old = $"({WaitFor("go")}; printf 'OLD_READY\\n'; printf 'OLD_READY\\n' >&2; printf 'partial'; sleep 30) & echo old-leader";
            p.Start(Sh(old, dir.Path) with { Leftovers = LeftoverMode.Detach });
            WaitForExited(p);
            oldLeader = p.Snapshot.Pid;
            Assert.True(p.CanStart);

            p.Start(Sh("echo NEW_BOOT; sleep 30", dir.Path) with { Ready = new ReadySpec { Pattern = "^OLD_READY$" } });
            WaitForText(p, "NEW_BOOT");
            File.WriteAllText(Path.Combine(dir.Path, "go"), string.Empty);

            WaitForText(p, CommandOutput.StaleOutputNotice);
            Thread.Sleep(300);
            Assert.DoesNotContain("OLD_READY", Text(p), StringComparison.Ordinal);
            Assert.DoesNotContain("partial", Text(p), StringComparison.Ordinal);
            Assert.Single(Lines(p), l => l.PlainText == CommandOutput.StaleOutputNotice);
            Assert.Equal(ProcessReadiness.Starting, p.Snapshot.Readiness);
        }
        finally
        {
            KillGroup(oldLeader);
            Cleanup(p);
        }
    }

    [Fact]
    public void StartingTheNextRunClosesTheEarlierRunsStdin()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        int? oldLeader = null;
        try
        {
            // The leftover reads stdin until EOF, which only closing the supervisor's end of the pipe can produce. sh gives a
            // background job /dev/null as stdin, so the pipe is passed on as fd 3.
            p.Start(Sh("exec 3<&0; (cat <&3 > /dev/null; touch stdin-closed) & echo started", dir.Path) with { Leftovers = LeftoverMode.Detach });
            WaitForExited(p);
            oldLeader = p.Snapshot.Pid;
            Thread.Sleep(200);
            Assert.False(File.Exists(Path.Combine(dir.Path, "stdin-closed")));

            p.Start(Sh("exit 0", dir.Path));
            WaitUntil(() => File.Exists(Path.Combine(dir.Path, "stdin-closed")), "the leftover to see stdin EOF", p);
            WaitForExited(p);

            // Once the leftover has exited too, every pipe has reached EOF and its reader is released.
            WaitUntil(() => p.LivePipeCount == 0, "all readers to end", p);
        }
        finally
        {
            KillGroup(oldLeader);
            Cleanup(p);
        }
    }

    [Fact]
    public void ReadersOfFinishedRunsAreReleased()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        try
        {
            for (int i = 0; i < 5; i++)
            {
                p.Start(Sh("echo hi", dir.Path));
                WaitForExited(p);
            }

            WaitUntil(() => p.LivePipeCount == 0, "every run's readers to end", p);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public void DisposeStopsOutputAndEndsTheReadersWithoutKillingTheChild()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        int? leader = null;
        try
        {
            p.Start(Sh($"echo BEFORE; {WaitFor("go")}; echo AFTER >&2; sleep 30", dir.Path));
            WaitForText(p, "BEFORE");
            leader = p.Snapshot.Pid;

            // Read after Dispose: one chunk changes the buffer twice (its completed lines, then its partial line), so a
            // version read as soon as BEFORE shows could miss the second change. Dispose takes the output lock that every
            // chunk holds, so no change of a chunk read before it can follow.
            p.Dispose();
            long versionBefore = p.Output.Buffer.Version;
            WaitUntil(() => p.LivePipeCount == 0, "the readers to end after disposal");

            // Linux disposal does not kill: the child is still alive and runs on, but its output goes nowhere.
            var group = new LinuxProcessGroup(leader!.Value);
            Assert.True(group.CountLiveProcesses() > 0);
            File.WriteAllText(Path.Combine(dir.Path, "go"), string.Empty);
            Thread.Sleep(300);
            Assert.DoesNotContain("AFTER", Text(p), StringComparison.Ordinal);
            Assert.Equal(versionBefore, p.Output.Buffer.Version);
        }
        finally
        {
            KillGroup(leader);
            p.Dispose();
        }
    }

    [Fact]
    public void DisposeIsIdempotentAfterAFailedStart()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        p.Start(Sh("exit 0", Path.Combine(dir.Path, "missing")));
        Assert.Equal(ProcessState.FailedToStart, p.Snapshot.State);
        Assert.Equal(0, p.LivePipeCount);
        p.Dispose();
        p.Dispose();
    }

    private static void KillGroup(int? pgid)
    {
        if (pgid is not int id)
        {
            return;
        }

        var group = new LinuxProcessGroup(id);
        group.Kill();
        WaitUntil(() => group.CountLiveProcesses() == 0, "the process group to die", timeout: TimeSpan.FromSeconds(5));
    }
}
