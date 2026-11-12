using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>Runs of nested groups: sub-runs, parallel steps, failure and cancellation, Stop, Activate and Restart on groups.</summary>
public class SessionTreeRunTests
{
    private const string Order = "order.txt";

    /// <summary>A command that appends its marker to the shared order file.</summary>
    private static CommandSpec Mark(TempDir dir, string name, string marker, string exitCode = "0") =>
        Sh(name, $"echo {marker} >> '{dir.File(Order)}'; exit {exitCode}");

    private static string[] OrderLines(TempDir dir) =>
        File.Exists(dir.File(Order)) ? File.ReadAllLines(dir.File(Order)) : Array.Empty<string>();

    private static GroupSpec Seq(string name, params ItemSpec[] items) => new() { Name = name, Mode = GroupMode.Sequential, Items = items.ToList() };

    private static GroupSpec Par(string name, params ItemSpec[] items) => new() { Name = name, Mode = GroupMode.Parallel, Items = items.ToList() };

    [Fact]
    public void Sequence_WithSequentialAndParallelChildGroups_RunsStepsInOrder()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq(
            "seq",
            Seq("inner", Mark(dir, "a1", "a1"), Mark(dir, "a2", "a2")),
            Par("par", Mark(dir, "p1", "p1")),
            Mark(dir, "after", "after")));
        try
        {
            GroupEntry seq = session.Groups[0];

            Assert.Null(session.Activate(seq));
            WaitUntil(() => !seq.IsSequenceRunning, "outer sequence to finish");

            Assert.Equal(new[] { "a1", "a2", "p1", "after" }, OrderLines(dir));
            Assert.All(session.AllEntries, e => Assert.Equal(ProcessState.Exited, e.Process.Snapshot.State));
            Assert.All(session.AllEntries, e => Assert.False(e.IsQueued));
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void SubRunFailure_CancelsRemainderOfOuterSequence()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq(
            "seq",
            Seq("inner", Mark(dir, "x1", "x1"), Mark(dir, "x2", "x2", exitCode: "3"), Mark(dir, "x3", "x3")),
            Mark(dir, "after", "after")));
        try
        {
            GroupEntry seq = session.Groups[0];
            GroupEntry inner = seq.Groups[0];
            CommandEntry x2 = inner.Commands[1];
            CommandEntry x3 = inner.Commands[2];
            CommandEntry after = seq.Commands[0];

            Assert.Null(session.Activate(seq));
            WaitUntil(() => !seq.IsSequenceRunning && !inner.IsSequenceRunning, "sequences to end", x2.Process);

            Assert.Equal(new[] { "x1", "x2" }, OrderLines(dir));
            WaitForExited(x2.Process);
            Assert.Equal(3, x2.Process.Snapshot.ExitCode);
            Assert.Contains("sequence cancelled: exit code 3", Text(x2.Process), StringComparison.Ordinal);
            Assert.Equal(ProcessState.NotStarted, x3.Process.Snapshot.State);
            Assert.Equal(ProcessState.NotStarted, after.Process.Snapshot.State);
            Assert.False(x3.IsQueued);
            Assert.False(after.IsQueued);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void ParallelSiblingSequences_FailureInOneDoesNotCancelTheOther()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Par(
            "root",
            Seq("seqA", Sh("a1", "exit 1"), Mark(dir, "a2", "a2")),
            Seq("seqB", Mark(dir, "b1", "b1"), Mark(dir, "b2", "b2"))));
        try
        {
            GroupEntry root = session.Groups[0];
            GroupEntry seqA = root.Groups[0];
            GroupEntry seqB = root.Groups[1];

            Assert.Null(session.Activate(root));
            WaitUntil(() => !seqA.IsSequenceRunning && !seqB.IsSequenceRunning, "both sequences to end", seqB.Commands[1].Process);

            Assert.Equal(new[] { "b1", "b2" }, OrderLines(dir));
            Assert.Equal(ProcessState.NotStarted, seqA.Commands[1].Process.Snapshot.State);
            Assert.Equal(ProcessState.Exited, seqB.Commands[1].Process.Snapshot.State);
            Assert.Equal(0, seqB.Commands[1].Process.Snapshot.ExitCode);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void ParallelChildFailure_FailsSequenceFast_AndSiblingKeepsRunning()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq(
            "outer",
            Par("par", Sh("fail", "exit 3"), Sleeper("long")),
            Mark(dir, "after", "after")));
        try
        {
            GroupEntry outer = session.Groups[0];
            CommandEntry long_ = outer.Groups[0].Commands[1];
            CommandEntry after = outer.Commands[0];

            Assert.Null(session.Activate(outer));
            WaitUntil(() => long_.Process.Snapshot.State == ProcessState.Running, "long running", long_.Process);
            WaitUntil(() => !outer.IsSequenceRunning, "outer sequence to fail", long_.Process);

            Assert.Equal(ProcessState.NotStarted, after.Process.Snapshot.State);
            Assert.False(after.IsQueued);
            Assert.Empty(OrderLines(dir));
            Assert.Equal(ProcessState.Running, long_.Process.Snapshot.State);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void Stop_CommandInsideNestedSubRun_CancelsSubRunAndOuterSequence()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq(
            "outer",
            Seq("inner", Sleeper("loop"), Mark(dir, "innerAfter", "innerAfter")),
            Mark(dir, "outerAfter", "outerAfter")));
        try
        {
            GroupEntry outer = session.Groups[0];
            GroupEntry inner = outer.Groups[0];
            CommandEntry loop = inner.Commands[0];
            CommandEntry innerAfter = inner.Commands[1];
            CommandEntry outerAfter = outer.Commands[0];

            Assert.Null(session.Activate(outer));
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running, "loop running", loop.Process);

            Assert.Null(session.Stop(new[] { loop }));

            WaitUntil(() => !outer.IsSequenceRunning && !inner.IsSequenceRunning, "sequences cancelled", loop.Process);
            WaitForExited(loop.Process);
            Assert.Equal(ProcessState.NotStarted, innerAfter.Process.Snapshot.State);
            Assert.Equal(ProcessState.NotStarted, outerAfter.Process.Snapshot.State);
            Assert.Empty(OrderLines(dir));
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void Stop_ParallelGroupInsideSequence_CancelsEnclosingSequence()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq(
            "outer",
            Par("par", Sleeper("loop")),
            Mark(dir, "after", "after")));
        try
        {
            GroupEntry outer = session.Groups[0];
            GroupEntry par = outer.Groups[0];
            CommandEntry loop = par.Commands[0];

            Assert.Null(session.Activate(outer));
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running, "loop running", loop.Process);

            Assert.Null(session.Stop(par));

            WaitUntil(() => !outer.IsSequenceRunning, "outer sequence cancelled", loop.Process);
            WaitForExited(loop.Process);
            Assert.Equal(ProcessState.NotStarted, outer.Commands[0].Process.Snapshot.State);
            Assert.Empty(OrderLines(dir));
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void Stop_Group_CancelsRunsBelowAndStopsRunningProcesses()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq(
            "outer",
            Seq("inner", Sleeper("loop"), Mark(dir, "innerAfter", "innerAfter")),
            Mark(dir, "outerAfter", "outerAfter")));
        try
        {
            GroupEntry outer = session.Groups[0];
            GroupEntry inner = outer.Groups[0];
            CommandEntry loop = inner.Commands[0];

            Assert.Null(session.Activate(outer));
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running, "loop running", loop.Process);

            Assert.Null(session.Stop(outer));

            WaitUntil(() => !outer.IsSequenceRunning && !inner.IsSequenceRunning, "sequences cancelled", loop.Process);
            WaitForExited(loop.Process);
            Assert.Equal(ProcessState.NotStarted, inner.Commands[1].Process.Snapshot.State);
            Assert.Equal(ProcessState.NotStarted, outer.Commands[0].Process.Snapshot.State);
            Assert.Empty(OrderLines(dir));
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void Activate_Group_KillsStoppingCommandBelow_AndStartsNothing()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        // Ignores Ctrl+C, so a graceful stop leaves it stopping until it is force-killed.
        CmdrSession session = OpenWith(dir, Par(
            "root",
            Par("nested", Sh("stubborn", "trap '' INT; echo ready; while :; do sleep 0.1; done")),
            Sh("idle", "exit 0")));
        try
        {
            GroupEntry root = session.Groups[0];
            CommandEntry stubborn = root.Groups[0].Commands[0];
            CommandEntry idle = root.Commands[0];

            Assert.Null(session.Activate(root));
            WaitForExited(idle.Process);
            int? idlePid = idle.Process.Snapshot.Pid;
            WaitUntil(() => Text(stubborn.Process).Contains("ready", StringComparison.Ordinal), "shell ready", stubborn.Process);

            Assert.Null(session.Stop(new[] { stubborn }));
            WaitUntil(() => stubborn.Process.Snapshot.State == ProcessState.Stopping, "stubborn stopping", stubborn.Process);

            Assert.Null(session.Activate(root));

            WaitUntil(() => stubborn.Process.Snapshot.State == ProcessState.Exited, "force killed", stubborn.Process);
            Assert.Equal(idlePid, idle.Process.Snapshot.Pid);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void Activate_Group_SkipsRunningCommands_AndStartsTheRest()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Par("root", Sleeper("loop"), Par("empty"), Sh("idle", "exit 0")));
        try
        {
            GroupEntry root = session.Groups[0];
            CommandEntry loop = root.Commands[0];
            CommandEntry idle = root.Commands[1];

            Assert.Null(session.Activate(new[] { loop }));
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running, "loop running", loop.Process);
            int? loopPid = loop.Process.Snapshot.Pid;

            Assert.Null(session.Activate(root));

            WaitForExited(idle.Process);
            Assert.Equal(0, idle.Process.Snapshot.ExitCode);
            Assert.Equal(loopPid, loop.Process.Snapshot.Pid);
            Assert.Equal(ProcessState.Running, loop.Process.Snapshot.State);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void Activate_Group_RefusedWhenItOverlapsAnActiveRun()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Par("root", Seq("build", Sleeper("loop"), Sh("other", "exit 0"))));
        try
        {
            GroupEntry root = session.Groups[0];
            GroupEntry build = root.Groups[0];
            CommandEntry loop = build.Commands[0];
            CommandEntry other = build.Commands[1];

            Assert.Null(session.Activate(build));
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running, "loop running", loop.Process);

            string? message = session.Activate(root);

            Assert.Equal("A sequence is already running in group 'build'.", message);
            Assert.Equal(ProcessState.NotStarted, other.Process.Snapshot.State);
            Assert.True(other.IsQueued);

            Assert.Null(session.Stop(new[] { loop }));
            WaitUntil(() => !build.IsSequenceRunning, "build cancelled", loop.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task RestartAsync_Group_RestartsRunningCommandsBelow()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Par("root", Sleeper("loop"), Par("nested", Sleeper("deep"))));
        try
        {
            GroupEntry root = session.Groups[0];
            CommandEntry loop = root.Commands[0];
            CommandEntry deep = root.Groups[0].Commands[0];
            Assert.Null(session.Activate(root));
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running && deep.Process.Snapshot.State == ProcessState.Running, "both running", loop.Process);
            int? loopPid = loop.Process.Snapshot.Pid;
            int? deepPid = deep.Process.Snapshot.Pid;

            string? message = await session.RestartAsync(root);

            Assert.Null(message);
            WaitUntil(
                () => loop.Process.Snapshot.State == ProcessState.Running && loop.Process.Snapshot.Pid != loopPid
                    && deep.Process.Snapshot.State == ProcessState.Running && deep.Process.Snapshot.Pid != deepPid,
                "both restarted",
                loop.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task RestartAsync_Group_CancelsRunningSequenceAndStartsItAgain()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq("seq", Sleeper("loop"), Mark(dir, "after", "after")));
        try
        {
            GroupEntry seq = session.Groups[0];
            CommandEntry loop = seq.Commands[0];
            Assert.Null(session.Activate(seq));
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running, "loop running", loop.Process);
            int? firstPid = loop.Process.Snapshot.Pid;

            string? message = await session.RestartAsync(seq);

            Assert.Null(message);
            WaitUntil(
                () => loop.Process.Snapshot.State == ProcessState.Running && loop.Process.Snapshot.Pid != firstPid,
                "loop restarted",
                loop.Process);
            Assert.True(seq.IsSequenceRunning);
            Assert.Empty(OrderLines(dir));

            session.Stop(seq);
            WaitUntil(() => !seq.IsSequenceRunning, "sequence stopped", loop.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }
}
