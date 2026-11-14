using System.ComponentModel;
using System.Diagnostics;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;

namespace Cmdr.Core.Tests.Processes;

/// <summary>
/// Review 02-01: a failing leftover count must not end the host. It is contained at the exit and timer callbacks and treated
/// as unknown (never as zero) until a later count succeeds. An escaped exception would abort the whole test run.
/// </summary>
public class ManagedProcessLeftoverCountFailureTests
{
    private const int BufferLines = 1000;
    private const string FailureLine = "leftover check failed: injected count failure, retrying";

    [Fact]
    public async Task FailedCountAtExitRecordsTheExitAndBlocksStartUntilACountSucceeds()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var platform = new FailingCountPlatform { Fail = true };
        var p = new ManagedProcess(BufferLines, platform);
        try
        {
            p.Start(Sh("sleep 30 & echo started", dir.Path));
            WaitForExited(p);
            await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));

            ProcessSnapshot snap = p.Snapshot;
            Assert.True(snap.LeftoverCountUnknown);
            Assert.True(snap.HasLeftovers);
            Assert.False(p.CanStart);
            Assert.Throws<InvalidOperationException>(() => p.Start(Sh("exit 0", dir.Path)));
            Assert.Equal("exit 0 · ? left", StatusFormatter.Format(snap, queued: false, DateTimeOffset.Now).Detail);

            // The poll keeps retrying, but the failure is reported once per streak.
            WaitUntil(() => platform.FailedCalls >= 3, "the poll to retry", p);
            Assert.Single(Lines(p), l => l.PlainText == FailureLine);

            platform.Fail = false;
            WaitUntil(() => !p.Snapshot.LeftoverCountUnknown, "a count to succeed", p);
            Assert.Equal(1, p.Snapshot.LeftoverCount);
            Assert.False(p.CanStart);

            p.ForceKill();
            WaitUntil(() => p.CanStart, "leftovers to clear after force kill", p, TimeSpan.FromSeconds(5));
        }
        finally
        {
            platform.Fail = false;
            Cleanup(p);
        }
    }

    [Fact]
    public void FailedCountAtExitWithNothingLeftClearsOnTheNextSuccessfulCount()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var platform = new FailingCountPlatform { Fail = true };
        var p = new ManagedProcess(BufferLines, platform);
        try
        {
            p.Start(Sh("exit 0", dir.Path));
            WaitForExited(p);
            Assert.True(p.Snapshot.LeftoverCountUnknown);
            Assert.False(p.CanStart);

            platform.Fail = false;
            WaitUntil(() => p.CanStart, "a successful count to clear the unknown state", p);
            Assert.Equal(0, p.Snapshot.LeftoverCount);
            Assert.False(p.Snapshot.LeftoverCountUnknown);

            p.Start(Sh("exit 0", dir.Path));
            WaitForExited(p);
        }
        finally
        {
            platform.Fail = false;
            Cleanup(p);
        }
    }

    [Fact]
    public void FailedCountOnATimerTickKeepsTheLastCountAndRecovers()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var platform = new FailingCountPlatform();
        var p = new ManagedProcess(BufferLines, platform);
        try
        {
            p.Start(Sh("sleep 30 & echo started", dir.Path));
            WaitForExited(p);
            WaitUntil(() => p.Snapshot.LeftoverCount == 1, "one leftover process", p);

            platform.Fail = true;
            WaitUntil(() => p.Snapshot.LeftoverCountUnknown, "the failed tick to mark the count unknown", p);
            Assert.Equal(1, p.Snapshot.LeftoverCount);
            Assert.False(p.CanStart);

            // Force kill stays available while the count is unknown, and the state stays unknown until a count succeeds.
            p.ForceKill();
            WaitUntil(() => platform.FailedCalls >= 3, "the poll to retry", p);
            Assert.True(p.Snapshot.LeftoverCountUnknown);
            Assert.Single(Lines(p), l => l.PlainText == FailureLine);

            platform.Fail = false;
            WaitUntil(() => p.CanStart, "a successful count after the kill", p, TimeSpan.FromSeconds(5));
            Assert.Equal(0, p.Snapshot.LeftoverCount);

            // A later streak reports again.
            p.Start(Sh("sleep 30 & echo started", dir.Path));
            WaitForExited(p);
            WaitUntil(() => p.Snapshot.LeftoverCount == 1, "one leftover process", p);
            platform.Fail = true;
            WaitUntil(() => p.Snapshot.LeftoverCountUnknown, "the second streak", p);
            Assert.Single(Lines(p), l => l.PlainText == FailureLine);
        }
        finally
        {
            platform.Fail = false;
            Cleanup(p);
        }
    }

    [Fact]
    public void DetachIsDecidedWhenTheFirstCountSucceeds()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var platform = new FailingCountPlatform { Fail = true };
        var p = new ManagedProcess(BufferLines, platform);
        int? leaderPid = null;
        try
        {
            p.Start(Sh("sleep 30 & echo started", dir.Path) with { Leftovers = LeftoverMode.Detach });
            WaitForExited(p);
            leaderPid = p.Snapshot.Pid;
            Assert.True(p.Snapshot.LeftoverCountUnknown);
            Assert.False(p.CanStart);

            platform.Fail = false;
            WaitUntil(() => p.CanStart, "the leftovers to be detached", p);
            WaitForText(p, "1 leftover process(es) detached");
            Assert.Equal(0, p.Snapshot.LeftoverCount);
        }
        finally
        {
            platform.Fail = false;
            if (leaderPid is int pgid)
            {
                var group = new LinuxProcessGroup(pgid);
                group.Kill();
                WaitUntil(() => group.CountLiveProcesses() == 0, "detached process to die", timeout: TimeSpan.FromSeconds(5));
            }

            Cleanup(p);
        }
    }

    /// <summary>Delegates to the Linux platform; its groups' counts throw while <see cref="Fail"/> is set.</summary>
    private sealed class FailingCountPlatform : IProcessPlatform
    {
        private readonly IProcessPlatform _inner = new LinuxProcessPlatform();
        private int _failedCalls;

        public volatile bool Fail;

        public int FailedCalls => Volatile.Read(ref _failedCalls);

        public PreparedLaunch Prepare(LaunchSpec spec) => _inner.Prepare(spec);

        public void Start(Process process, PreparedLaunch launch) => _inner.Start(process, launch);

        public IProcessGroup Attach(Process process) => new Group(_inner.Attach(process), this);

        private sealed class Group(IProcessGroup inner, FailingCountPlatform owner) : IProcessGroup
        {
            public void Interrupt() => inner.Interrupt();

            public void Kill() => inner.Kill();

            public int CountLiveProcesses()
            {
                if (owner.Fail)
                {
                    Interlocked.Increment(ref owner._failedCalls);
                    throw new Win32Exception(5, "injected count failure");
                }

                return inner.CountLiveProcesses();
            }

            public void Detach() => inner.Detach();

            public void Dispose() => inner.Dispose();
        }
    }
}
