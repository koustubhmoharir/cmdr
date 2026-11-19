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
    [Fact]
    public async Task Activate_NewCommandInQueuedSequentialChild_IsRefusedWithoutDisruptingPlan()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string release = dir.File("release");
        CmdrSession session = OpenWith(dir,
            Seq("outer",
                Sh("gate", $"while [ ! -f '{release}' ]; do sleep 0.02; done; echo released"),
                Seq("inner", Sh("planned", "echo planned")),
                Sh("after", "echo after")),
            Group("independent", GroupMode.Parallel, Sh("sibling", "echo sibling"), Sh("movable", "echo moved")));
        try
        {
            GroupEntry outer = session.Groups[0];
            GroupEntry inner = outer.Groups[0];
            CommandEntry gate = outer.Commands[0];
            session.Activate(outer);
            WaitUntil(() => gate.Process.Snapshot.State == ProcessState.Running, "outer gate running", gate.Process);

            CommandEntry added = session.AddCommand(inner, Sh("added", "echo added"));
            string? refusal = session.Activate(new[] { added });
            Assert.Contains("running sequence", refusal, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(ProcessState.NotStarted, added.Process.Snapshot.State);
            Assert.False(added.IsQueued);

            CommandEntry moved = session.Groups[1].Commands[1];
            session.UpdateCommand(moved, Sh("moved", "echo moved"), inner);
            string? moveRefusal = session.Activate(new[] { moved });
            Assert.Contains("running sequence", moveRefusal, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(ProcessState.NotStarted, moved.Process.Snapshot.State);

            CommandEntry independent = session.Groups[1].Commands[0];
            Assert.Null(session.Activate(new[] { independent }));
            WaitForExited(independent.Process);
            Assert.Equal(0, independent.Process.Snapshot.ExitCode);

            File.WriteAllText(release, "go");
            WaitUntil(() => !outer.IsSequenceRunning, "outer plan to finish", gate.Process);
            Assert.Equal(ProcessState.Exited, inner.Commands[0].Process.Snapshot.State);
            Assert.Equal(ProcessState.NotStarted, added.Process.Snapshot.State);
            Assert.Equal(ProcessState.Exited, outer.Commands[1].Process.Snapshot.State);
        }
        finally
        {
            Cleanup(session);
        }
    }

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
            WaitForText(x2.Process, "sequence cancelled: exit code 3");
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
    public void ParallelGroupInsideSequence_FailedBranch_WritesExitCodeLine()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq(
            "seq",
            Par("par", Mark(dir, "bad", "bad", exitCode: "3"))));
        try
        {
            GroupEntry seq = session.Groups[0];
            CommandEntry bad = seq.Groups[0].Commands[0];

            Assert.Null(session.Activate(seq));
            WaitUntil(() => !seq.IsSequenceRunning, "sequence to end", bad.Process);

            WaitForText(bad.Process, "sequence cancelled: exit code 3");
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void ParallelGroupInsideSequence_StoppedBranch_WritesStoppedAndCancelsSequence()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq(
            "seq",
            Par("par", Sleeper("p")),
            Mark(dir, "after", "after")));
        try
        {
            GroupEntry seq = session.Groups[0];
            CommandEntry p = seq.Groups[0].Commands[0];
            CommandEntry after = seq.Commands[0];

            Assert.Null(session.Activate(seq));
            WaitUntil(() => p.Process.Snapshot.State == ProcessState.Running, "p running", p.Process);

            Assert.Null(session.Stop(new[] { p }));

            WaitUntil(() => !seq.IsSequenceRunning, "sequence to end", p.Process);
            WaitForExited(p.Process);
            WaitForText(p.Process, "sequence cancelled: stopped");
            Assert.DoesNotContain("sequence cancelled: exit code", Text(p.Process), StringComparison.Ordinal);
            Assert.Equal(ProcessState.NotStarted, after.Process.Snapshot.State);
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

    /// <summary>
    /// Starts the sequential group, then stops its queued second command. The run ends, but the first command, which was
    /// running, keeps running: the group has no active run while one of its commands is still running.
    /// </summary>
    private static void EndRunWhileFirstCommandKeepsRunning(CmdrSession session, GroupEntry group, CommandEntry running, CommandEntry queued)
    {
        Assert.Null(session.Activate(group));
        WaitUntil(() => running.Process.Snapshot.State == ProcessState.Running, "first command running", running.Process);
        Assert.True(queued.IsQueued);

        Assert.Null(session.Stop(new[] { queued }));
        WaitUntil(() => !group.IsSequenceRunning, "run to end", running.Process);
        Assert.Equal(ProcessState.Running, running.Process.Snapshot.State);
    }

    [Fact]
    public void Activate_GroupAfterStopOfQueuedCommand_IsRefusedWhileTheRunningCommandStillRuns()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string release = dir.File("release");
        CmdrSession session = OpenWith(
            dir,
            Seq("build", Sh("a", $"while [ ! -f '{release}' ]; do sleep 0.02; done"), Mark(dir, "b", "b"), Mark(dir, "c", "c")));
        try
        {
            GroupEntry build = session.Groups[0];
            CommandEntry a = build.Commands[0];
            CommandEntry b = build.Commands[1];
            CommandEntry c = build.Commands[2];
            EndRunWhileFirstCommandKeepsRunning(session, build, a, b);

            string? refusal = session.Activate(build);

            Assert.Equal("Cannot start: 'a' in sequential group 'build' is still running.", refusal);
            Assert.Equal(ProcessState.NotStarted, b.Process.Snapshot.State);
            Assert.Equal(ProcessState.NotStarted, c.Process.Snapshot.State);
            Assert.False(b.IsQueued);
            Assert.False(c.IsQueued);
            Assert.Equal(ProcessState.Running, a.Process.Snapshot.State);
            Assert.Empty(OrderLines(dir));

            // a exits on its own; once it has, the group is startable again and runs in order (a exits at once now).
            File.WriteAllText(release, "go");
            WaitForExited(a.Process);
            Assert.Equal(0, a.Process.Snapshot.ExitCode);

            Assert.Null(session.Activate(build));
            WaitUntil(() => !build.IsSequenceRunning, "build to finish", a.Process);
            Assert.Equal(new[] { "b", "c" }, OrderLines(dir));
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void Activate_CommandRowAfterStopOfQueuedCommand_IsRefusedWhileTheRunningCommandStillRuns()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq("build", Sleeper("a"), Mark(dir, "b", "b"), Mark(dir, "c", "c")));
        try
        {
            GroupEntry build = session.Groups[0];
            CommandEntry a = build.Commands[0];
            CommandEntry b = build.Commands[1];
            CommandEntry c = build.Commands[2];
            EndRunWhileFirstCommandKeepsRunning(session, build, a, b);

            string? refusal = session.Activate(new[] { b });

            Assert.Equal("Cannot start 'b': 'a' in sequential group 'build' is still running.", refusal);
            Assert.Equal(ProcessState.NotStarted, b.Process.Snapshot.State);
            Assert.False(b.IsQueued);
            Assert.Empty(OrderLines(dir));

            Assert.Null(session.Stop(new[] { a }));
            WaitForExited(a.Process);

            Assert.Null(session.Activate(new[] { b }));
            WaitUntil(() => !build.IsSequenceRunning, "b to finish", b.Process);
            Assert.Equal(new[] { "b" }, OrderLines(dir));
            Assert.Equal(ProcessState.NotStarted, c.Process.Snapshot.State);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void Activate_ParallelParentOfBusySequence_IsRefusedAndStartsNothing()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Par(
            "root",
            Seq("build", Sleeper("a"), Mark(dir, "b", "b")),
            Mark(dir, "sibling", "sibling")));
        try
        {
            GroupEntry root = session.Groups[0];
            GroupEntry build = root.Groups[0];
            CommandEntry a = build.Commands[0];
            CommandEntry b = build.Commands[1];
            CommandEntry sibling = root.Commands[0];
            EndRunWhileFirstCommandKeepsRunning(session, build, a, b);

            string? refusal = session.Activate(root);

            Assert.Equal("Cannot start: 'a' in sequential group 'build' is still running.", refusal);
            Assert.Equal(ProcessState.NotStarted, sibling.Process.Snapshot.State);
            Assert.Equal(ProcessState.NotStarted, b.Process.Snapshot.State);
            Assert.Empty(OrderLines(dir));

            Assert.Null(session.Stop(new[] { a }));
            WaitForExited(a.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void SubRun_WhoseGroupHasARunningCommand_IsRefusedAndFailsTheEnclosingSequence()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string release = dir.File("release");
        CmdrSession session = OpenWith(
            dir,
            Seq(
                "outer",
                Sh("gate", $"while [ ! -f '{release}' ]; do sleep 0.02; done"),
                Seq("inner", Mark(dir, "b", "b")),
                Mark(dir, "after", "after")),
            Group("free", GroupMode.Parallel, Sleeper("x")));
        try
        {
            GroupEntry outer = session.Groups[0];
            GroupEntry inner = outer.Groups[0];
            CommandEntry gate = outer.Commands[0];
            CommandEntry b = inner.Commands[0];
            CommandEntry after = outer.Commands[1];
            CommandEntry x = session.Groups[1].Commands[0];

            Assert.Null(session.Activate(outer));
            WaitUntil(() => gate.Process.Snapshot.State == ProcessState.Running, "gate running", gate.Process);
            Assert.Null(session.Activate(new[] { x }));
            WaitUntil(() => x.Process.Snapshot.State == ProcessState.Running, "x running", x.Process);

            // Moving a running command is allowed; it lands in a group the active run has planned but not started.
            session.UpdateCommand(x, Sleeper("x"), inner);
            File.WriteAllText(release, "go");

            WaitUntil(() => !outer.IsSequenceRunning, "outer sequence to fail", gate.Process);
            WaitForText(b.Process, "Cannot start: 'x' in sequential group 'inner' is still running.");
            Assert.Equal(ProcessState.NotStarted, b.Process.Snapshot.State);
            Assert.Equal(ProcessState.NotStarted, after.Process.Snapshot.State);
            Assert.False(b.IsQueued);
            Assert.False(after.IsQueued);
            Assert.Equal(ProcessState.Running, x.Process.Snapshot.State);
            Assert.Empty(OrderLines(dir));

            Assert.Null(session.Stop(new[] { x }));
            WaitForExited(x.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task RestartAsync_RunningCommandOfBusySequence_RestartsIt()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq("build", Sleeper("a"), Mark(dir, "b", "b"), Mark(dir, "c", "c")));
        try
        {
            GroupEntry build = session.Groups[0];
            CommandEntry a = build.Commands[0];
            CommandEntry b = build.Commands[1];
            EndRunWhileFirstCommandKeepsRunning(session, build, a, b);
            int? firstPid = a.Process.Snapshot.Pid;

            string? message = await session.RestartAsync(new[] { a });

            Assert.Null(message);
            WaitUntil(
                () => a.Process.Snapshot.State == ProcessState.Running && a.Process.Snapshot.Pid != firstPid,
                "a restarted",
                a.Process);
            Assert.True(build.IsSequenceRunning);
            Assert.Equal(ProcessState.NotStarted, b.Process.Snapshot.State);
            Assert.Empty(OrderLines(dir));

            Assert.Null(session.Stop(new[] { a }));
            WaitUntil(() => !build.IsSequenceRunning, "restarted run to end", a.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task RestartAsync_OtherCommandOfBusySequence_IsRefused()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq("build", Sleeper("a"), Mark(dir, "b", "b")));
        try
        {
            GroupEntry build = session.Groups[0];
            CommandEntry a = build.Commands[0];
            CommandEntry b = build.Commands[1];
            EndRunWhileFirstCommandKeepsRunning(session, build, a, b);

            string? message = await session.RestartAsync(new[] { b });

            Assert.Equal("Cannot start 'b': 'a' in sequential group 'build' is still running.", message);
            Assert.Equal(ProcessState.Running, a.Process.Snapshot.State);
            Assert.Equal(ProcessState.NotStarted, b.Process.Snapshot.State);
            Assert.False(b.IsQueued);
            Assert.False(build.IsSequenceRunning);
            Assert.Empty(OrderLines(dir));

            Assert.Null(session.Stop(new[] { a }));
            WaitForExited(a.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void Activate_CommandsOfParallelGroupBesideBusySequence_StillStart()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Par(
            "root",
            Seq("build", Sleeper("a"), Mark(dir, "b", "b")),
            Sleeper("x"),
            Sleeper("y")));
        try
        {
            GroupEntry root = session.Groups[0];
            GroupEntry build = root.Groups[0];
            CommandEntry a = build.Commands[0];
            CommandEntry b = build.Commands[1];
            CommandEntry x = root.Commands[0];
            CommandEntry y = root.Commands[1];
            EndRunWhileFirstCommandKeepsRunning(session, build, a, b);

            Assert.Null(session.Activate(new[] { x, y }));

            WaitUntil(
                () => x.Process.Snapshot.State == ProcessState.Running && y.Process.Snapshot.State == ProcessState.Running,
                "x and y running",
                x.Process);
            Assert.Equal(ProcessState.NotStarted, b.Process.Snapshot.State);

            Assert.Null(session.Stop(new[] { x, y }));
            Assert.Null(session.Stop(new[] { a }));
            WaitUntil(() => !build.IsSequenceRunning, "build to end", a.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void InputsForActivate_BusySequence_AsksForNoInputsForRefusedStarts()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var needsInput = new CommandSpec
        {
            Name = "b",
            Program = "sh",
            Args = new List<string> { "-c", "echo b", "_", "${input:v}" },
        };
        CmdrSession session = OpenWith(dir, Seq("build", Sleeper("a"), needsInput));
        try
        {
            GroupEntry build = session.Groups[0];
            CommandEntry a = build.Commands[0];
            CommandEntry b = build.Commands[1];
            Assert.Single(session.InputsForActivate(build));

            EndRunWhileFirstCommandKeepsRunning(session, build, a, b);

            Assert.Empty(session.InputsForActivate(build));
            Assert.Empty(session.InputsForActivate(new[] { b }));

            Assert.Null(session.Stop(new[] { a }));
            WaitForExited(a.Process);
            Assert.Single(session.InputsForActivate(build));
        }
        finally
        {
            Cleanup(session);
        }
    }
}
