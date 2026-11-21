using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using Shared.Processes;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>Input placeholders in commands: which inputs an action needs, and how values reach the started commands (Linux only).</summary>
public class SessionInputTests
{
    private static GroupSpec Seq(string name, params ItemSpec[] items) => new() { Name = name, Mode = GroupMode.Sequential, Items = items.ToList() };

    private static GroupSpec Par(string name, params ItemSpec[] items) => new() { Name = name, Mode = GroupMode.Parallel, Items = items.ToList() };

    /// <summary>A command running <c>sh -c script</c> with extra arguments; the script sees them as <c>$1</c>, <c>$2</c>, ...</summary>
    private static CommandSpec Shell(string name, string script, params string[] args) =>
        new() { Name = name, Program = "sh", Args = new List<string> { "-c", script, "_" }.Concat(args).ToList() };

    /// <summary>Prints the value of the input, in brackets, so an empty value is visible.</summary>
    private static CommandSpec Echo(string name, string inputPlaceholder) =>
        Shell(name, "echo \"got:[$1]\"", inputPlaceholder);

    private static InputRequest Required(string name) => new(name, false);

    private static InputRequest Optional(string name) => new(name, true);

    [Fact]
    public void InputsForActivate_Commands_ListsInputsOfStartableTargetsOnly()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Par(
            "g",
            Sleeper("loop"),
            Shell("a", "exit 0", "${input:x}"),
            Shell("b", "exit 0", "${input:y?}", "${input:x}")));
        try
        {
            CommandEntry loop = session.Groups[0].Commands[0];
            CommandEntry a = session.Groups[0].Commands[1];
            CommandEntry b = session.Groups[0].Commands[2];

            Assert.Null(session.Activate(new[] { loop }));
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running, "loop running", loop.Process);

            Assert.Equal(new[] { Required("x"), Optional("y") }, session.InputsForActivate(new[] { loop, a, b }));
            Assert.Empty(session.InputsForActivate(new[] { loop }));
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void InputsForActivate_KillableTarget_IsEmpty()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Par(
            "g",
            Sh("stubborn", "trap '' INT; echo ready; while :; do sleep 0.1; done"),
            Shell("idle", "exit 0", "${input:x}")));
        try
        {
            CommandEntry stubborn = session.Groups[0].Commands[0];
            CommandEntry idle = session.Groups[0].Commands[1];

            Assert.Null(session.Activate(new[] { stubborn }));
            WaitUntil(() => Text(stubborn.Process).Contains("ready", StringComparison.Ordinal), "shell ready", stubborn.Process);
            Assert.Null(session.Stop(new[] { stubborn }));
            WaitUntil(() => stubborn.Process.Snapshot.State == ProcessState.Stopping, "stubborn stopping", stubborn.Process);

            Assert.Empty(session.InputsForActivate(new[] { stubborn, idle }));
            Assert.Empty(session.InputsForActivate(session.Groups[0]));
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void InputsForActivate_Group_ListsInputsOfEverythingItWouldStart()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Par(
            "root",
            Shell("p", "exit 0", "${input:p}"),
            Seq("seq", Shell("s1", "exit 0", "${input:s?}"), Shell("s2", "exit 0", "${input:p}"))));
        try
        {
            GroupEntry root = session.Groups[0];

            Assert.Equal(new[] { Required("p"), Optional("s") }, session.InputsForActivate(root));
            Assert.Equal(new[] { Required("p"), Optional("s") }, session.InputsForRestart(root));
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void InputsForActivate_Group_WithoutPlaceholders_IsEmpty()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Par("g", Sh("a", "exit 0")));
        try
        {
            Assert.Empty(session.InputsForActivate(session.Groups[0]));
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void ParallelGroup_StartsCommandsWithSubstitutedArguments_AndKeepsSpec()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Par("g", Echo("a", "${input:x}")));
        try
        {
            CommandEntry a = session.Groups[0].Commands[0];

            Assert.Null(session.Activate(new[] { a }, new Dictionary<string, string> { ["x"] = "hello" }));

            WaitForExited(a.Process);
            // Exit markers follow a bounded drain; redirected output can still arrive afterward.
            WaitForText(a.Process, "got:[hello]");
            Assert.Contains("got:[hello]", Text(a.Process), StringComparison.Ordinal);
            Assert.Equal("${input:x}", a.Spec.Args[^1]);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void SequentialGroup_LaterStepGetsTheSameValue()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq("seq", Sh("first", "exit 0"), Echo("second", "${input:x}")));
        try
        {
            GroupEntry seq = session.Groups[0];
            CommandEntry second = seq.Commands[1];

            Assert.Null(session.Activate(seq, new Dictionary<string, string> { ["x"] = "hello" }));
            WaitUntil(() => !seq.IsSequenceRunning, "sequence to finish", second.Process);

            WaitForExited(second.Process);
            WaitForText(second.Process, "got:[hello]");
            Assert.Contains("got:[hello]", Text(second.Process), StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void SubRun_GetsTheValuesOfTheEnclosingAction()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq(
            "outer",
            Seq("inner", Sh("first", "exit 0"), Echo("deep", "${input:x}"))));
        try
        {
            GroupEntry outer = session.Groups[0];
            CommandEntry deep = outer.Groups[0].Commands[1];

            Assert.Null(session.Activate(outer, new Dictionary<string, string> { ["x"] = "nested" }));
            WaitUntil(() => !outer.IsSequenceRunning && !outer.Groups[0].IsSequenceRunning, "sequences to finish", deep.Process);

            WaitForExited(deep.Process);
            WaitForText(deep.Process, "got:[nested]");
            Assert.Contains("got:[nested]", Text(deep.Process), StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void MissingValue_StepFailsToStart_AndSequenceDoesNotContinue()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq(
            "seq",
            Echo("needs", "${input:x}"),
            Sh("after", "echo ran > ran.txt")));
        try
        {
            GroupEntry seq = session.Groups[0];
            CommandEntry needs = seq.Commands[0];
            CommandEntry after = seq.Commands[1];

            Assert.Null(session.Activate(seq));
            WaitUntil(() => !seq.IsSequenceRunning, "sequence to fail", needs.Process);

            Assert.Equal(ProcessState.NotStarted, needs.Process.Snapshot.State);
            Assert.Contains("failed to start: no value for input 'x'", Text(needs.Process), StringComparison.Ordinal);
            Assert.Equal(ProcessState.NotStarted, after.Process.Snapshot.State);
            Assert.False(after.IsQueued);
            Assert.False(needs.IsQueued);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void EmptyValue_IsRejectedForRequiredPlaceholder_AndAcceptedForOptional()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Par("g", Echo("req", "${input:x}"), Echo("opt", "${input:x?}")));
        try
        {
            CommandEntry req = session.Groups[0].Commands[0];
            CommandEntry opt = session.Groups[0].Commands[1];
            var empty = new Dictionary<string, string> { ["x"] = "" };

            Assert.Null(session.Activate(new[] { req, opt }, empty));

            WaitForExited(opt.Process);
            WaitForText(opt.Process, "got:[]");
            Assert.Contains("got:[]", Text(opt.Process), StringComparison.Ordinal);
            Assert.Equal(ProcessState.NotStarted, req.Process.Snapshot.State);
            Assert.Contains("failed to start: input 'x' must not be empty", Text(req.Process), StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task RestartAsync_PassesValuesToTheNewRun()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Par("g", Echo("a", "${input:x}")));
        try
        {
            CommandEntry a = session.Groups[0].Commands[0];
            Assert.Null(session.Activate(new[] { a }, new Dictionary<string, string> { ["x"] = "first" }));
            WaitForExited(a.Process);

            string? message = await session.RestartAsync(new[] { a }, new Dictionary<string, string> { ["x"] = "second" });

            Assert.Null(message);
            WaitUntil(() => Text(a.Process).Contains("got:[second]", StringComparison.Ordinal), "restarted with second value", a.Process);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void PrepareActivate_StartRefusedByARunningSequence_IsRefused_AndAsksForNothing()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq(
            "build",
            Shell("loop", "sleep 600"),
            Echo("b", "${input:x}")));
        try
        {
            CommandEntry loop = session.Groups[0].Commands[0];
            CommandEntry b = session.Groups[0].Commands[1];
            Assert.False(session.PrepareActivate(new[] { b }).Refused);

            Assert.Null(session.Activate(new[] { loop }));
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running, "loop running", loop.Process);

            PreparedActivation prepared = session.PrepareActivate(new[] { b });
            Assert.True(prepared.Refused);
            Assert.Empty(prepared.Requests);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void InputsForRestart_CommandBesideActiveRun_AsksForNothing()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq(
            "build",
            Shell("loop", "sleep 600", "${input:y}"),
            Echo("b", "${input:x}")));
        try
        {
            CommandEntry loop = session.Groups[0].Commands[0];
            CommandEntry b = session.Groups[0].Commands[1];

            Assert.Null(session.Activate(new[] { loop }, new Dictionary<string, string> { ["y"] = "1" }));
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running, "loop running", loop.Process);

            // The sequence that runs loop refuses b's start, and nothing it runs is cancelled by a restart of b.
            Assert.Empty(session.InputsForRestart(new[] { b }));

            // Control: a restart of loop ends its own run, so its input is asked for; with b, both are asked for.
            Assert.Equal(new[] { Required("y") }, session.InputsForRestart(new[] { loop }));
            Assert.Equal(new[] { Required("y"), Required("x") }, session.InputsForRestart(new[] { loop, b }));
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void InputsForRestart_GroupBelowSequentialGroupWithRunningCommand_AsksForNothing()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq(
            "outer",
            Sleeper("loop"),
            Seq("inner", Echo("b", "${input:x}"))));
        try
        {
            GroupEntry outer = session.Groups[0];
            GroupEntry inner = outer.Groups[0];
            CommandEntry loop = outer.Commands[0];

            Assert.Null(session.Activate(new[] { loop }));
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running, "loop running", loop.Process);

            // Restarting inner would not cancel the run of outer, so loop (still running) refuses the start of b.
            Assert.Empty(session.InputsForRestart(inner));

            // A restart of outer stops loop and cancels its run, so b's start is not refused.
            Assert.Equal(new[] { Required("x") }, session.InputsForRestart(outer));
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void InputsForRestart_CommandBesideCommandLeftRunningByCancelledRun_AsksForNothingButGroupDoes()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Seq("build", Sleeper("a"), Echo("b", "${input:x}")));
        try
        {
            GroupEntry build = session.Groups[0];
            CommandEntry a = build.Commands[0];
            CommandEntry b = build.Commands[1];

            Assert.Null(session.Activate(build));
            WaitUntil(() => a.Process.Snapshot.State == ProcessState.Running, "first command running", a.Process);
            Assert.True(b.IsQueued);

            // Stopping the queued b cancels the run, and a keeps running after the run has ended.
            Assert.Null(session.Stop(new[] { b }));
            WaitUntil(() => !build.IsSequenceRunning, "run to end", a.Process);
            Assert.Equal(ProcessState.Running, a.Process.Snapshot.State);

            // A restart of b does not stop a, so a's running blocks b's start.
            Assert.Empty(session.InputsForRestart(new[] { b }));

            // A restart of the group stops a, so the start of b is not refused.
            Assert.Equal(new[] { Required("x") }, session.InputsForRestart(build));
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void InputsForActivate_ListsArgsThenAncestorEnvThenCommandEnv()
    {
        using var dir = new TempDir();
        var command = new CommandSpec
        {
            Name = "c",
            Program = "sh",
            Args = { "${input:arg}" },
            Env = new Dictionary<string, string?> { ["C"] = "${input:cmd?}" },
        };
        var inner = new GroupSpec
        {
            Name = "inner",
            Env = new Dictionary<string, string?> { ["I"] = "${input:inner}" },
            Items = new List<ItemSpec> { command },
        };
        var outer = new GroupSpec
        {
            Name = "outer",
            Mode = GroupMode.Parallel,
            Env = new Dictionary<string, string?> { ["O"] = "${input:outer}" },
            Items = new List<ItemSpec> { inner },
        };
        CmdrSession session = OpenWith(dir, outer);

        Assert.Equal(
            new[] { Required("arg"), Required("outer"), Required("inner"), Optional("cmd") },
            session.InputsForActivate(session.Groups[0]));
    }

    [Fact]
    public void InputInEnvValue_IsPassedToTheStartedCommand()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, new GroupSpec
        {
            Name = "g",
            Mode = GroupMode.Parallel,
            Env = new Dictionary<string, string?> { ["V"] = "${input:x}" },
            Items = new List<ItemSpec> { Sh("a", "echo \"v:[$V]\"") },
        });
        try
        {
            CommandEntry a = session.Groups[0].Commands[0];

            Assert.Null(session.Activate(new[] { a }, new Dictionary<string, string> { ["x"] = "hi" }));

            WaitForExited(a.Process);
            WaitForText(a.Process, "v:[hi]");
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void MissingValue_UsedOnlyInEnvValue_FailsToStart_AndLaunchesNothing()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, new GroupSpec
        {
            Name = "g",
            Mode = GroupMode.Parallel,
            Env = new Dictionary<string, string?> { ["V"] = "${input:x}" },
            Items = new List<ItemSpec> { Sh("a", "echo ran > ran.txt") },
        });
        try
        {
            CommandEntry a = session.Groups[0].Commands[0];

            Assert.Null(session.Activate(new[] { a }));

            WaitForText(a.Process, "failed to start: no value for input 'x'");
            Assert.Equal(ProcessState.NotStarted, a.Process.Snapshot.State);
            Assert.False(File.Exists(Path.Combine(dir.Path, "ran.txt")));
        }
        finally
        {
            Cleanup(session);
        }
    }
}
