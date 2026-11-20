using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>
/// The environment script values that prepared actions carry to their launches: activations, sequences and branches, and restarts
/// (Linux only, real processes). Also which commands a restart starts (<see cref="CmdrSession.RestartStarts(IReadOnlyList{CommandEntry})"/>).
/// </summary>
public class SessionLaunchValuesTests
{
    /// <summary>A script that prints <paramref name="json"/> as its output.</summary>
    private static EnvScriptSpec Produces(string json) =>
        new() { Program = "sh", Args = new List<string> { "-c", "printf '%s' '" + json + "'" } };

    private static GroupSpec Par(string name, EnvScriptSpec? script, params ItemSpec[] items) =>
        new() { Name = name, Mode = GroupMode.Parallel, EnvScript = script, Items = items.ToList() };

    private static GroupSpec Seq(string name, EnvScriptSpec? script, params ItemSpec[] items) =>
        new() { Name = name, Mode = GroupMode.Sequential, EnvScript = script, Items = items.ToList() };

    /// <summary>A command that prints the given variables, each as <c>NAME=value</c>, from its environment.</summary>
    private static CommandSpec Prints(string name, params string[] variables) =>
        Sh(name, string.Join(" ; ", variables.Select(v => $"echo \"{v}=${v}\"")));

    private static CommandSpec WithEnv(CommandSpec spec, params (string Name, string? Value)[] env)
    {
        foreach ((string name, string? value) in env)
        {
            spec.Env[name] = value;
        }

        return spec;
    }

    /// <summary>A command with an input placeholder in its arguments, which exits at once.</summary>
    private static CommandSpec WithInput(string name, string input) =>
        new() { Name = name, Program = "sh", Args = new List<string> { "-c", "exit 0", "_", "${input:" + input + "}" } };

    private static CommandEntry Cmd(CmdrSession session, string name) => session.AllEntries.Single(entry => entry.Name == name);

    private static CmdrSession Open(TempDir dir, CmdrConfig config)
    {
        ConfigStore.Save(config, dir.File("cmdr.json"));
        return CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);
    }

    [Fact]
    public async Task Activate_CommandSeesGroupScriptAndFileScriptValues()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = Open(dir, new CmdrConfig
        {
            EnvScript = Produces("{\"F\":\"from-file\"}"),
            Groups = new List<GroupSpec> { Par("g", Produces("{\"S\":\"from-group\"}"), Prints("cmd", "S", "F")) },
        });
        try
        {
            CommandEntry cmd = Cmd(session, "cmd");
            EnvPreparation prep = await session.PrepareEnvAsync(new[] { cmd }, null, CancellationToken.None);
            Assert.True(prep.Succeeded, prep.Error);

            Assert.Null(session.Activate(new[] { cmd }, null, prep.Values));

            WaitForExited(cmd.Process);
            WaitForText(cmd.Process, "S=from-group");
            WaitForText(cmd.Process, "F=from-file");
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task Activate_GroupEnvReadingScriptVariable_SeesTheScriptValue()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var group = Par("g", Produces("{\"X\":\"script-x\"}"), Prints("cmd", "G"));
        group.Env = new Dictionary<string, string?> { ["G"] = "${env:X}-suffix" };
        CmdrSession session = Open(dir, new CmdrConfig { Groups = new List<GroupSpec> { group } });
        try
        {
            CommandEntry cmd = Cmd(session, "cmd");
            EnvPreparation prep = await session.PrepareEnvAsync(new[] { cmd }, null, CancellationToken.None);
            Assert.True(prep.Succeeded, prep.Error);

            Assert.Null(session.Activate(new[] { cmd }, null, prep.Values));

            WaitForExited(cmd.Process);
            WaitForText(cmd.Process, "G=script-x-suffix");
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task Activate_CommandEnvOverridesScriptVariable()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = Open(dir, new CmdrConfig
        {
            Groups = new List<GroupSpec>
            {
                Par("g", Produces("{\"X\":\"script\"}"), WithEnv(Prints("cmd", "X"), ("X", "command"))),
            },
        });
        try
        {
            CommandEntry cmd = Cmd(session, "cmd");
            EnvPreparation prep = await session.PrepareEnvAsync(new[] { cmd }, null, CancellationToken.None);
            Assert.True(prep.Succeeded, prep.Error);

            Assert.Null(session.Activate(new[] { cmd }, null, prep.Values));

            WaitForExited(cmd.Process);
            WaitForText(cmd.Process, "X=command");
            Assert.DoesNotContain("X=script", Text(cmd.Process), StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task Activate_SequentialGroup_EveryStepSeesTheValues_IncludingSubGroupsAndBranches()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = Open(dir, new CmdrConfig
        {
            Groups = new List<GroupSpec>
            {
                Seq(
                    "seq",
                    Produces("{\"S\":\"seq-value\"}"),
                    Prints("first", "S"),
                    Seq("sub", null, Prints("second", "S")),
                    Par("branches", null, Prints("third", "S"))),
            },
        });
        try
        {
            GroupEntry group = session.Groups[0];
            EnvPreparation prep = await session.PrepareEnvAsync(group.AllCommands, null, CancellationToken.None);
            Assert.True(prep.Succeeded, prep.Error);

            Assert.Null(session.Activate(group, null, prep.Values));

            foreach (string name in new[] { "first", "second", "third" })
            {
                CommandEntry cmd = Cmd(session, name);
                WaitForExited(cmd.Process);
                WaitForText(cmd.Process, "S=seq-value");
            }
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task RestartWithScriptValues_RestartedCommandSeesTheValues()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = Open(dir, new CmdrConfig
        {
            Groups = new List<GroupSpec> { Par("g", Produces("{\"S\":\"restarted\"}"), Prints("cmd", "S")) },
        });
        try
        {
            CommandEntry cmd = Cmd(session, "cmd");
            EnvPreparation prep = await session.PrepareEnvAsync(new[] { cmd }, null, CancellationToken.None);
            Assert.True(prep.Succeeded, prep.Error);

            PreparedRestart prepared = session.PrepareRestart(new[] { cmd }, null, prep.Values);
            Assert.Null(await session.RunRestartAsync(prepared));

            WaitForExited(cmd.Process);
            WaitForText(cmd.Process, "S=restarted");
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void Activate_CommandWhoseScriptHasNoValues_FailsToStart_OthersStart()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = Open(dir, new CmdrConfig
        {
            Groups = new List<GroupSpec>
            {
                Par(
                    "P",
                    null,
                    Par("A", Produces("{\"S\":\"a\"}"), Prints("needs", "S")),
                    Par("B", null, Prints("free", "S"))),
            },
        });
        try
        {
            CommandEntry needs = Cmd(session, "needs");
            CommandEntry free = Cmd(session, "free");

            Assert.Null(session.Activate(session.Groups[0]));

            WaitForExited(free.Process);
            WaitForText(needs.Process, "failed to start: env script of group 'P › A' has not run");
            Assert.Equal(ProcessState.NotStarted, needs.Process.Snapshot.State);
            Assert.Equal(ProcessState.Exited, free.Process.Snapshot.State);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void Activate_CommandTargetWithoutScriptValues_WritesTheFailureAndLaunchesNothing()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = Open(dir, new CmdrConfig
        {
            EnvScript = Produces("{\"F\":\"file\"}"),
            Groups = new List<GroupSpec> { Par("g", null, Prints("cmd", "F")) },
        });
        try
        {
            CommandEntry cmd = Cmd(session, "cmd");

            Assert.Null(session.Activate(new[] { cmd }));

            WaitForText(cmd.Process, "failed to start: env script of file 'cmdr.json' has not run");
            Assert.Equal(ProcessState.NotStarted, cmd.Process.Snapshot.State);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void RestartStarts_Commands_AreTheOnesInputsForRestartConsiders()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = Open(dir, new CmdrConfig
        {
            Groups = new List<GroupSpec> { Par("g", null, WithInput("a", "x"), Prints("c")) },
        });
        try
        {
            CommandEntry a = Cmd(session, "a");
            CommandEntry c = Cmd(session, "c");

            Assert.Equal(new[] { a, c }, session.RestartStarts(new[] { a, c }));
            Assert.Equal(new[] { new InputRequest("x", false) }, session.InputsForRestart(new[] { a, c }));
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void RestartStarts_RefusedCommands_IsEmpty()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = Open(dir, new CmdrConfig
        {
            Groups = new List<GroupSpec> { Seq("seq", null, Sleeper("loop"), WithInput("b", "y")) },
        });
        try
        {
            CommandEntry loop = Cmd(session, "loop");
            CommandEntry b = Cmd(session, "b");

            Assert.Null(session.Activate(new[] { loop }));
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running, "loop running", loop.Process);

            // b cannot start while the sequential group's loop runs: the restart would be refused.
            Assert.Empty(session.RestartStarts(new[] { b }));
            Assert.Empty(session.InputsForRestart(new[] { b }));
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void RestartStarts_Group_IncludesRunningAndStartableCommands()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = Open(dir, new CmdrConfig
        {
            Groups = new List<GroupSpec> { Par("g", null, Sleeper("loop"), Prints("a")) },
        });
        try
        {
            GroupEntry group = session.Groups[0];
            CommandEntry loop = Cmd(session, "loop");

            Assert.Null(session.Activate(new[] { loop }));
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running, "loop running", loop.Process);

            Assert.Equal(new[] { "loop", "a" }, session.RestartStarts(group).Select(entry => entry.Name));
            Assert.Empty(session.InputsForRestart(group));
        }
        finally
        {
            Cleanup(session);
        }
    }
}
