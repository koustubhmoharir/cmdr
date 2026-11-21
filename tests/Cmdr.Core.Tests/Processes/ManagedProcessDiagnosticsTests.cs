using System.ComponentModel;
using Cmdr.Core.Processes;
using Cmdr.Core.Tests.Config;
using Shared.Processes;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;

namespace Cmdr.Core.Tests.Processes;

/// <summary>
/// Diagnostics of <see cref="ManagedProcess"/>: a stop failure of an earlier run must not appear in the next run's output, a
/// failure of the current run still must, and an exception from a <see cref="ManagedProcess.Changed"/> subscriber is contained.
/// Linux only (real processes).
/// </summary>
public class ManagedProcessDiagnosticsTests
{
    private const int BufferLines = 1000;
    private const string InterruptFailure = "injected interrupt failure";

    [Fact]
    public async Task InterruptFailureOfAnEarlierRunIsNotWrittenToTheNextRun()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var platform = new InterruptFailurePlatform();
        var p = new ManagedProcess(BufferLines, platform);
        try
        {
            p.Start(Sh("echo first; while :; do sleep 0.1; done", dir.Path));
            WaitForText(p, "first");

            // Run A's interrupt blocks until released, then fails. It is dispatched outside the lock, as in production.
            platform.GateInterrupts = true;
            Task stop = Task.Run(p.RequestStop);
            Assert.True(platform.InterruptEntered.Wait(TimeSpan.FromSeconds(10)), "the interrupt of run A was not dispatched");

            p.ForceKill();
            WaitForExited(p);
            WaitUntil(() => p.CanStart, "run A's leftovers to clear", p, TimeSpan.FromSeconds(5));

            p.Start(Sh("echo second", dir.Path));
            WaitForExited(p);
            WaitForText(p, "second");

            // Run A's interrupt now fails. Its failure must be dropped: run B is the current run.
            platform.ReleaseInterrupt.Set();
            await stop.WaitAsync(TimeSpan.FromSeconds(10));

            string text = Text(p);
            Assert.Contains("second", text, StringComparison.Ordinal);
            Assert.DoesNotContain("stop failed", text, StringComparison.Ordinal);
            Assert.DoesNotContain(InterruptFailure, text, StringComparison.Ordinal);
        }
        finally
        {
            platform.ReleaseInterrupt.Set();
            platform.GateInterrupts = false;
            Cleanup(p);
        }
    }

    [Fact]
    public void InterruptFailureOfTheCurrentRunIsStillReported()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var platform = new InterruptFailurePlatform { FailInterrupts = true };
        var p = new ManagedProcess(BufferLines, platform);
        try
        {
            p.Start(Sh("echo ready; while :; do sleep 0.1; done", dir.Path));
            WaitForText(p, "ready");

            p.RequestStop();

            WaitForText(p, $"stop failed: {InterruptFailure}");
            Assert.Single(Lines(p), l => l.PlainText == $"stop failed: {InterruptFailure}");
        }
        finally
        {
            platform.FailInterrupts = false;
            Cleanup(p);
        }
    }

    [Fact]
    public void ThrowingChangedSubscriberIsContainedAndTheProcessStillExits()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines);
        int secondCalls = 0;
        try
        {
            p.Changed += _ => throw new InvalidOperationException("subscriber boom");
            p.Changed += _ => Interlocked.Increment(ref secondCalls);

            p.Start(Sh("echo hi", dir.Path));
            WaitForExited(p);
            WaitForText(p, "cmdr error: InvalidOperationException: subscriber boom");
            WaitUntil(() => Volatile.Read(ref secondCalls) > 0, "the well-behaved subscriber to be called", p);
            Assert.Equal(ProcessState.Exited, p.Snapshot.State);
        }
        finally
        {
            Cleanup(p);
        }
    }

    /// <summary>
    /// Delegates to the Linux platform. While <see cref="GateInterrupts"/> is set, an interrupt announces itself on
    /// <see cref="InterruptEntered"/>, waits for <see cref="ReleaseInterrupt"/>, then fails. While <see cref="FailInterrupts"/> is set,
    /// it fails at once.
    /// </summary>
    private sealed class InterruptFailurePlatform : IProcessPlatform
    {
        private readonly IProcessPlatform _inner = new LinuxProcessPlatform();

        public volatile bool GateInterrupts;

        public volatile bool FailInterrupts;

        public readonly ManualResetEventSlim InterruptEntered = new();

        public readonly ManualResetEventSlim ReleaseInterrupt = new();

        public PreparedLaunch Prepare(LaunchSpec spec) => _inner.Prepare(spec);

        public LaunchedTree Launch(PreparedLaunch launch, Action<ILaunchedProcess> onExited)
        {
            LaunchedTree tree = _inner.Launch(launch, onExited);
            return tree with { Group = new Group(tree.Group, this) };
        }

        private sealed class Group(IProcessGroup inner, InterruptFailurePlatform owner) : IProcessGroup
        {
            public void Interrupt()
            {
                if (owner.GateInterrupts)
                {
                    owner.InterruptEntered.Set();
                    owner.ReleaseInterrupt.Wait(TimeSpan.FromSeconds(20));
                    throw new Win32Exception(5, InterruptFailure);
                }

                if (owner.FailInterrupts)
                {
                    throw new Win32Exception(5, InterruptFailure);
                }

                inner.Interrupt();
            }

            public void Kill() => inner.Kill();

            public int CountLiveProcesses() => inner.CountLiveProcesses();

            public void Detach() => inner.Detach();

            public void Dispose() => inner.Dispose();
        }
    }
}
