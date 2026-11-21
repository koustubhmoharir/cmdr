using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using Shared.Processes;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>
/// What Enter does is decided when it is pressed (<see cref="CmdrSession.PrepareActivate(GroupEntry)"/>), and the dialog for its
/// inputs does not change that decision: a command that becomes startable meanwhile is not started (Linux only).
/// </summary>
public class SessionActivationTests
{
    /// <summary>A command running <c>sh -c script</c> with extra arguments; the script sees them as <c>$1</c>, <c>$2</c>, ...</summary>
    private static CommandSpec Shell(string name, string script, params string[] args) =>
        new() { Name = name, Program = "sh", Args = new List<string> { "-c", script, "_" }.Concat(args).ToList() };

    private static Dictionary<string, string> Values(string name, string value) => new() { [name] = value };

    [Fact]
    public void GroupPrepared_CommandThatBecomesStartableLater_IsNotStartedAndItsInputIsNotAsked()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group(
            "g",
            GroupMode.Parallel,
            Shell("a", "echo \"a:[$1]\"; exec sleep 30", "${input:b}"),
            Shell("b", "echo \"b:[$1]\"; exec sleep 30", "${input:a}")));
        GroupEntry group = session.Groups[0];
        CommandEntry a = group.Commands[0];
        CommandEntry b = group.Commands[1];
        try
        {
            Assert.Null(session.Activate(new[] { a }, Values("b", "x")));
            WaitForText(a.Process, "a:[x]");

            PreparedActivation prepared = session.PrepareActivate(group);
            Assert.Equal(new[] { new InputRequest("a", false) }, prepared.Requests);

            // a is running, so it is not startable when Enter is pressed. It exits, and becomes startable, after the decision.
            a.Process.ForceKill();
            WaitUntil(() => a.Process.CanStart, "a to be startable again", a.Process);
            int aRunId = a.Process.RunId;

            Assert.Null(session.Activate(prepared, Values("a", "y")));

            WaitForText(b.Process, "b:[y]");
            Assert.Equal(ProcessState.Running, b.Process.Snapshot.State);
            // a was not startable when Enter was pressed, so it is not started, and the dialog did not ask for b.
            Assert.Equal(aRunId, a.Process.RunId);
            Assert.Equal(ProcessState.Exited, a.Process.Snapshot.State);
            Assert.DoesNotContain("no value for input", Text(b.Process));
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void GroupPreparedWhileAStopIsPending_ActivateKillsWhatWasKillableAndStartsNothing()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group(
            "g",
            GroupMode.Parallel,
            Sh("stubborn", "trap '' INT; echo ready; while :; do sleep 0.1; done"),
            Sleeper("idle")));
        GroupEntry group = session.Groups[0];
        CommandEntry stubborn = group.Commands[0];
        CommandEntry idle = group.Commands[1];
        try
        {
            Assert.Null(session.Activate(new[] { stubborn }));
            WaitForText(stubborn.Process, "ready");
            session.Stop(new[] { stubborn });
            WaitUntil(() => stubborn.Process.Snapshot.State == ProcessState.Stopping, "the stop to be requested", stubborn.Process);

            PreparedActivation prepared = session.PrepareActivate(group);
            Assert.Empty(prepared.Requests);

            Assert.Null(session.Activate(prepared));

            WaitUntil(() => stubborn.Process.Snapshot.State == ProcessState.Exited, "the stubborn command to be killed", stubborn.Process);
            Assert.Equal(ProcessState.NotStarted, idle.Process.Snapshot.State);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void PreparedActivation_OfAnotherSession_IsRefused()
    {
        using var first = new TempDir();
        using var second = new TempDir();
        CmdrSession one = OpenWith(first, Group("g", GroupMode.Parallel, Sleeper("a")));
        CmdrSession two = OpenWith(second, Group("g", GroupMode.Parallel, Sleeper("a")));
        try
        {
            PreparedActivation prepared = one.PrepareActivate(one.Groups[0]);

            Assert.Throws<ArgumentException>(() => two.Activate(prepared));
        }
        finally
        {
            Cleanup(one);
            Cleanup(two);
        }
    }
}
