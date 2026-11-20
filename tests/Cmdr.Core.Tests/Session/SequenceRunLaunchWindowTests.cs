using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>
/// A cancellation of a run while its current command is being started, before the run thread checks for the cancellation. A Stop
/// aimed at a queued command leaves the command that is starting running; a Stop aimed at that command, or at its group, stops it.
/// </summary>
public class SequenceRunLaunchWindowTests
{
    [Fact]
    public void StopOfQueuedCommand_WhileCurrentCommandIsStarting_LeavesItRunning()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq("build", Sleeper("a"), Sh("b", "echo b")));
        using var gate = new LaunchGate();
        try
        {
            GroupEntry build = session.Groups[0];
            CommandEntry a = build.Commands[0];
            CommandEntry b = build.Commands[1];
            gate.Hold(a);

            Assert.Null(session.Activate(build));
            gate.WaitUntilEntered();

            Assert.Null(session.Stop(new[] { b }));
            gate.Release();

            WaitUntil(() => !build.IsSequenceRunning, "run to end", a.Process);
            Assert.Equal(ProcessState.Running, a.Process.Snapshot.State);
            Assert.Equal(ProcessState.NotStarted, b.Process.Snapshot.State);
            Assert.False(b.IsQueued);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void StopOfCurrentCommand_WhileItIsStarting_StopsIt()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq("build", Sleeper("a"), Sh("b", "echo b")));
        using var gate = new LaunchGate();
        try
        {
            GroupEntry build = session.Groups[0];
            CommandEntry a = build.Commands[0];
            CommandEntry b = build.Commands[1];
            gate.Hold(a);

            Assert.Null(session.Activate(build));
            gate.WaitUntilEntered();

            Assert.Null(session.Stop(new[] { a }));
            gate.Release();

            WaitForExited(a.Process);
            Assert.True(a.Process.Snapshot.StopRequested);
            WaitUntil(() => !build.IsSequenceRunning, "run to end", a.Process);
            Assert.Equal(ProcessState.NotStarted, b.Process.Snapshot.State);
            Assert.False(b.IsQueued);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void StopOfGroup_WhileCurrentCommandIsStarting_StopsIt()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq("build", Sleeper("a"), Sh("b", "echo b")));
        using var gate = new LaunchGate();
        try
        {
            GroupEntry build = session.Groups[0];
            CommandEntry a = build.Commands[0];
            CommandEntry b = build.Commands[1];
            gate.Hold(a);

            Assert.Null(session.Activate(build));
            gate.WaitUntilEntered();

            Assert.Null(session.Stop(build));
            gate.Release();

            WaitForExited(a.Process);
            Assert.True(a.Process.Snapshot.StopRequested);
            WaitUntil(() => !build.IsSequenceRunning, "run to end", a.Process);
            Assert.Equal(ProcessState.NotStarted, b.Process.Snapshot.State);
            Assert.False(b.IsQueued);
        }
        finally
        {
            Cleanup(session);
        }
    }

    private static GroupSpec Seq(string name, params ItemSpec[] items) => new() { Name = name, Mode = GroupMode.Sequential, Items = items.ToList() };

    /// <summary>
    /// Holds the run thread right after the held command starts, until <see cref="Release"/>. Only the held command is affected,
    /// so other runs in the same process are not. The hook is cleared on dispose.
    /// </summary>
    private sealed class LaunchGate : IDisposable
    {
        private readonly ManualResetEventSlim _entered = new();
        private readonly ManualResetEventSlim _release = new();
        private CommandEntry? _held;

        internal LaunchGate()
        {
            SequenceRun.AfterLaunchForTesting = OnStarted;
        }

        /// <summary>Makes <paramref name="entry"/> the command the run thread is held after.</summary>
        internal void Hold(CommandEntry entry) => _held = entry;

        internal void WaitUntilEntered() =>
            Assert.True(_entered.Wait(TimeSpan.FromSeconds(10)), "the run thread to start the held command");

        internal void Release() => _release.Set();

        public void Dispose()
        {
            _release.Set();
            SequenceRun.AfterLaunchForTesting = null;
        }

        private void OnStarted(CommandEntry started)
        {
            if (started != _held)
            {
                return;
            }

            _entered.Set();
            _release.Wait(TimeSpan.FromSeconds(10));
        }
    }
}
