using System.Diagnostics;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>Environment scripts in a session: owners and their order, values per owner, reuse, inputs, failures and cancellation.</summary>
public class SessionEnvScriptsTests
{
    private static readonly string BaseRel = Path.Combine("shared", "base.json");

    /// <summary>A script that runs <c>sh -c shell</c>.</summary>
    private static EnvScriptSpec Script(string shell, EnvScriptRerun rerun = EnvScriptRerun.Once) =>
        new() { Program = "sh", Args = new List<string> { "-c", shell }, Rerun = rerun };

    /// <summary>A script that appends its label to the log file and prints <paramref name="json"/>.</summary>
    private static EnvScriptSpec Logging(string log, string label, string json = "{}", EnvScriptRerun rerun = EnvScriptRerun.Once) =>
        Script("printf '%s\\n' '" + label + "' >> '" + log + "'; printf '%s' '" + json + "'", rerun);

    private static List<string> LogLines(string log) =>
        File.Exists(log) ? File.ReadAllLines(log).ToList() : new List<string>();

    private static GroupSpec G(string name, EnvScriptSpec? script = null, params ItemSpec[] items) =>
        new() { Name = name, EnvScript = script, Items = items.ToList() };

    private static CommandEntry Cmd(CmdrSession session, string name) => session.AllEntries.Single(entry => entry.Name == name);

    private static CmdrSession Open(TempDir dir, CmdrConfig config)
    {
        ConfigStore.Save(config, dir.File("cmdr.json"));
        return CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);
    }

    /// <summary>
    /// A session whose file imports <c>shared/base.json</c>. The base file has <paramref name="baseScript"/> and one group with a command;
    /// the main file has <paramref name="baseEnv"/> (null for none), <paramref name="mainScript"/> and <paramref name="groups"/>.
    /// </summary>
    private static CmdrSession OpenImporting(
        TempDir dir,
        EnvScriptSpec baseScript,
        List<string>? baseEnv,
        EnvScriptSpec? mainScript,
        params GroupSpec[] groups)
    {
        ConfigStore.Save(new CmdrConfig
        {
            EnvScript = baseScript,
            Groups = new List<GroupSpec> { Group("base-group", GroupMode.Parallel, Sh("base-cmd", "true")) },
        }, dir.File(BaseRel));
        return Open(dir, new CmdrConfig
        {
            Base = BaseRel,
            BaseEnv = baseEnv,
            EnvScript = mainScript,
            Groups = groups.ToList(),
        });
    }

    [Fact]
    public async Task Owners_RunInDependencyOrder_AndTheBaseFileIsOwnerBecauseItsLevelsReachTheCommand()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string log = dir.File("log.txt");
        using CmdrSession session = OpenImporting(
            dir,
            Logging(log, "base-file"),
            baseEnv: new List<string> { "X" },
            mainScript: Logging(log, "main-file"),
            G("g", Logging(log, "group-g"), G("h", Logging(log, "group-h"), Sh("cmd", "true"))));

        IReadOnlyList<CommandEntry> commands = new[] { Cmd(session, "cmd") };
        Assert.True(session.NeedsEnvScripts(commands));
        EnvPreparation prep = await session.PrepareEnvAsync(commands, null, CancellationToken.None);

        Assert.True(prep.Succeeded, prep.Error);
        Assert.Equal(new[] { "base-file", "main-file", "group-g", "group-h" }, LogLines(log));
    }

    [Fact]
    public async Task BaseFileWithoutBaseEnv_IsNotAnOwner()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string log = dir.File("log.txt");
        using CmdrSession session = OpenImporting(
            dir,
            Logging(log, "base-file"),
            baseEnv: null,
            mainScript: Logging(log, "main-file"),
            G("g", null, Sh("cmd", "true")));

        EnvPreparation prep = await session.PrepareEnvAsync(new[] { Cmd(session, "cmd") }, null, CancellationToken.None);

        Assert.True(prep.Succeeded, prep.Error);
        Assert.Equal(new[] { "main-file" }, LogLines(log));
    }

    [Fact]
    public async Task SetOfCommands_RunsEachOwnerOnce_InDependencyOrder()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string log = dir.File("log.txt");
        using CmdrSession session = OpenImporting(
            dir,
            Logging(log, "base-file"),
            baseEnv: new List<string> { "X" },
            mainScript: Logging(log, "main-file"),
            G("outer", Logging(log, "outer"), G("inner", Logging(log, "inner"), Sh("a", "true"))),
            G("second", Logging(log, "second"), Sh("b", "true")),
            G("plain", null, Sh("c", "true")));

        IReadOnlyList<CommandEntry> commands = new[] { Cmd(session, "a"), Cmd(session, "b"), Cmd(session, "c"), Cmd(session, "a") };
        EnvPreparation prep = await session.PrepareEnvAsync(commands, null, CancellationToken.None);

        Assert.True(prep.Succeeded, prep.Error);
        Assert.Equal(new[] { "base-file", "main-file", "outer", "second", "inner" }, LogLines(log));
    }

    [Fact]
    public async Task NoScripts_NeedNothing_AndPrepareCompletesWithNoValues()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        using CmdrSession session = Open(dir, new CmdrConfig { Groups = new List<GroupSpec> { G("g", null, Sh("cmd", "true")) } });
        IReadOnlyList<CommandEntry> commands = new[] { Cmd(session, "cmd") };

        Assert.False(session.NeedsEnvScripts(commands));
        EnvPreparation prep = await session.PrepareEnvAsync(commands, null, CancellationToken.None);

        Assert.True(prep.Succeeded);
        Assert.Empty(prep.Affected);
        Assert.False(prep.Values.TryGet(session.MainSource, out _));
    }

    [Fact]
    public async Task GroupScript_SeesTheValuesOfTheFileScript_AndValuesArePerOwner()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        using CmdrSession session = Open(dir, new CmdrConfig
        {
            EnvScript = Script("printf '%s' '{\"FILE_VAR\":\"from-file\"}'"),
            Groups = new List<GroupSpec>
            {
                G("g", Script("printf '{\"GROUP_VAR\":\"%s\"}' \"$FILE_VAR\""), Sh("cmd", "true")),
            },
        });
        CommandEntry cmd = Cmd(session, "cmd");

        EnvPreparation prep = await session.PrepareEnvAsync(new[] { cmd }, null, CancellationToken.None);

        Assert.True(prep.Succeeded, prep.Error);
        Assert.True(prep.Values.TryGet(session.MainSource, out IReadOnlyDictionary<string, string?>? fileValues));
        Assert.Equal("from-file", fileValues!["FILE_VAR"]);
        Assert.True(prep.Values.TryGet(cmd.Group, out IReadOnlyDictionary<string, string?>? groupValues));
        Assert.Equal("from-file", groupValues!["GROUP_VAR"]);
    }

    [Fact]
    public async Task OnceScript_RunsOnceAcrossTwoPreparations()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string log = dir.File("log.txt");
        using CmdrSession session = Open(dir, new CmdrConfig
        {
            Groups = new List<GroupSpec> { G("g", Logging(log, "once"), Sh("cmd", "true")) },
        });
        IReadOnlyList<CommandEntry> commands = new[] { Cmd(session, "cmd") };

        Assert.True((await session.PrepareEnvAsync(commands, null, CancellationToken.None)).Succeeded);
        Assert.True((await session.PrepareEnvAsync(commands, null, CancellationToken.None)).Succeeded);

        Assert.Equal(new[] { "once" }, LogLines(log));
    }

    [Fact]
    public async Task EachStartScript_RunsEachTime()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string log = dir.File("log.txt");
        using CmdrSession session = Open(dir, new CmdrConfig
        {
            Groups = new List<GroupSpec> { G("g", Logging(log, "each", rerun: EnvScriptRerun.EachStart), Sh("cmd", "true")) },
        });
        IReadOnlyList<CommandEntry> commands = new[] { Cmd(session, "cmd") };

        Assert.True((await session.PrepareEnvAsync(commands, null, CancellationToken.None)).Succeeded);
        Assert.True((await session.PrepareEnvAsync(commands, null, CancellationToken.None)).Succeeded);

        Assert.Equal(new[] { "each", "each" }, LogLines(log));
    }

    [Fact]
    public async Task ChangedInput_RerunsTheDownstreamOnceScript()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string log = dir.File("log.txt");
        using CmdrSession session = Open(dir, new CmdrConfig
        {
            Env = new Dictionary<string, string?> { ["V"] = "${input:v}" },
            Groups = new List<GroupSpec> { G("g", Logging(log, "group"), Sh("cmd", "true")) },
        });
        IReadOnlyList<CommandEntry> commands = new[] { Cmd(session, "cmd") };

        IReadOnlyList<InputRequest> requests = session.InputsForActivate(commands);
        Assert.Contains(requests, request => request.Name == "v");

        Assert.True((await session.PrepareEnvAsync(commands, new Dictionary<string, string> { ["v"] = "1" }, CancellationToken.None)).Succeeded);
        Assert.Single(LogLines(log));

        Assert.True((await session.PrepareEnvAsync(commands, new Dictionary<string, string> { ["v"] = "2" }, CancellationToken.None)).Succeeded);
        Assert.Equal(2, LogLines(log).Count);

        Assert.True((await session.PrepareEnvAsync(commands, new Dictionary<string, string> { ["v"] = "2" }, CancellationToken.None)).Succeeded);
        Assert.Equal(2, LogLines(log).Count);
    }

    [Fact]
    public async Task ScriptArguments_AskForTheirInputs_AndTheValuesAreSubstituted()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var script = new EnvScriptSpec
        {
            Program = "sh",
            Args = new List<string> { "-c", "printf '{\"A\":\"%s\"}' \"$1\"", "x", "${input:name}" },
        };
        using CmdrSession session = Open(dir, new CmdrConfig { Groups = new List<GroupSpec> { G("g", script, Sh("cmd", "true")) } });
        IReadOnlyList<CommandEntry> commands = new[] { Cmd(session, "cmd") };

        IReadOnlyList<InputRequest> requests = session.InputsForActivate(commands);
        Assert.Contains(requests, request => request.Name == "name" && !request.AllowEmpty);

        EnvPreparation prep = await session.PrepareEnvAsync(commands, new Dictionary<string, string> { ["name"] = "hi" }, CancellationToken.None);

        Assert.True(prep.Succeeded, prep.Error);
        Assert.True(prep.Values.TryGet(commands[0].Group, out IReadOnlyDictionary<string, string?>? values));
        Assert.Equal("hi", values!["A"]);
    }

    [Fact]
    public async Task FailingFileScript_ReturnsTheFailure_WritesItToEveryAffectedCommand_AndStops()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string log = dir.File("log.txt");
        using CmdrSession session = Open(dir, new CmdrConfig
        {
            EnvScript = Script("echo 'boom detail' >&2; exit 3"),
            Groups = new List<GroupSpec>
            {
                G("g", Logging(log, "group-g"), Sh("a", "true")),
                G("h", null, Sh("b", "true")),
            },
        });
        CommandEntry a = Cmd(session, "a");
        CommandEntry b = Cmd(session, "b");

        EnvPreparation prep = await session.PrepareEnvAsync(new[] { a, b }, null, CancellationToken.None);

        Assert.False(prep.Succeeded);
        Assert.Equal("file 'cmdr.json'", prep.FailedOwnerLabel);
        Assert.NotNull(prep.Error);
        Assert.Contains("boom detail", prep.StdErr);
        Assert.Equal(2, prep.Affected.Count);
        Assert.Same(a, prep.Affected[0]);
        Assert.Same(b, prep.Affected[1]);
        Assert.Empty(LogLines(log));
        Assert.Contains($"env script of file 'cmdr.json' failed: {prep.Error}", Text(a.Process));
        Assert.Contains("boom detail", Text(a.Process));
        Assert.Contains($"env script of file 'cmdr.json' failed: {prep.Error}", Text(b.Process));
    }

    [Fact]
    public async Task FailingGroupScript_LabelNamesTheGroupPath()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        using CmdrSession session = Open(dir, new CmdrConfig
        {
            Groups = new List<GroupSpec> { G("g", null, G("h", Script("exit 1"), Sh("cmd", "true"))) },
        });
        CommandEntry cmd = Cmd(session, "cmd");

        EnvPreparation prep = await session.PrepareEnvAsync(new[] { cmd }, null, CancellationToken.None);

        Assert.False(prep.Succeeded);
        Assert.Equal("group 'g › h'", prep.FailedOwnerLabel);
        Assert.Contains($"env script of group 'g › h' failed: {prep.Error}", Text(cmd.Process));
    }

    [Fact]
    public async Task Cancellation_CancelsTheTask_KillsTheScript_AndWritesNothing()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string log = dir.File("log.txt");
        using CmdrSession session = Open(dir, new CmdrConfig
        {
            Groups = new List<GroupSpec> { G("g", Script("printf 'started\\n' >> '" + log + "'; sleep 30"), Sh("cmd", "true")) },
        });
        CommandEntry cmd = Cmd(session, "cmd");
        using var cancel = new CancellationTokenSource();

        Task<EnvPreparation> task = session.PrepareEnvAsync(new[] { cmd }, null, cancel.Token);
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (LogLines(log).Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        var clock = Stopwatch.StartNew();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), $"the script was not stopped promptly: {clock.Elapsed}");
        Assert.DoesNotContain("env script", Text(cmd.Process));
    }

    [Fact]
    public async Task RefreshEnvScripts_ForcesARerunOfAOnceScript()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string log = dir.File("log.txt");
        using CmdrSession session = Open(dir, new CmdrConfig
        {
            Groups = new List<GroupSpec> { G("g", Logging(log, "once"), Sh("cmd", "true")) },
        });
        CommandEntry cmd = Cmd(session, "cmd");
        IReadOnlyList<CommandEntry> commands = new[] { cmd };

        Assert.True((await session.PrepareEnvAsync(commands, null, CancellationToken.None)).Succeeded);
        Assert.True((await session.PrepareEnvAsync(commands, null, CancellationToken.None)).Succeeded);
        Assert.Single(LogLines(log));

        session.RefreshEnvScripts(cmd);
        Assert.True((await session.PrepareEnvAsync(commands, null, CancellationToken.None)).Succeeded);

        Assert.Equal(2, LogLines(log).Count);
    }

    [Fact]
    public async Task LastEnvScriptRun_RecordsAFailure()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        using CmdrSession session = Open(dir, new CmdrConfig
        {
            EnvScript = Script("exit 2"),
            Groups = new List<GroupSpec> { G("g", null, Sh("cmd", "true")) },
        });

        Assert.Null(session.LastEnvScriptRun(session.MainSource));
        EnvPreparation prep = await session.PrepareEnvAsync(new[] { Cmd(session, "cmd") }, null, CancellationToken.None);

        Assert.False(prep.Succeeded);
        (DateTimeOffset finishedAt, EnvScriptResult result)? last = session.LastEnvScriptRun(session.MainSource);
        Assert.NotNull(last);
        Assert.False(last.Value.result.Succeeded);
        Assert.True(last.Value.finishedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void EnvScriptOwnersOf_ListsTheOwnersOfACommandInRunOrderWithTheirLabels()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenImporting(
            dir,
            Script("true"),
            baseEnv: new List<string> { "X" },
            mainScript: Script("true"),
            G("outer", Script("true"), G("inner", Script("true"), Sh("cmd", "true"))));

        IReadOnlyList<(object Owner, string Label)> owners = session.EnvScriptOwnersOf(Cmd(session, "cmd"));

        Assert.Equal(4, owners.Count);
        Assert.Same(session.MainSource.BaseSource, owners[0].Owner);
        Assert.Equal($"file '{session.MainSource.BaseSource!.DisplayPath}'", owners[0].Label);
        Assert.Same(session.MainSource, owners[1].Owner);
        Assert.Equal($"file '{session.MainSource.DisplayPath}'", owners[1].Label);
        Assert.Same(session.AllGroups.Single(g => g.Name == "outer"), owners[2].Owner);
        Assert.Equal("group 'outer'", owners[2].Label);
        Assert.Same(session.AllGroups.Single(g => g.Name == "inner"), owners[3].Owner);
        Assert.Equal("group 'outer › inner'", owners[3].Label);
    }

    [Fact]
    public void EnvScriptOwnersOf_IsEmptyWhenNoScriptApplies()
    {
        using var dir = new TempDir();
        using CmdrSession session = Open(dir, new CmdrConfig
        {
            Groups = new List<GroupSpec> { G("g", null, Sh("cmd", "true")) },
        });

        Assert.Empty(session.EnvScriptOwnersOf(Cmd(session, "cmd")));
        Assert.Empty(session.EnvScriptOwnersOf(session.AllGroups.Single()));
    }

    [Fact]
    public async Task KnownEnvScriptValues_AreEmptyBeforeARun_AndHoldTheValuesOfASuccessfulRun()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        using CmdrSession session = Open(dir, new CmdrConfig
        {
            Groups = new List<GroupSpec> { G("g", Script("printf '%s' '{\"GREETING\":\"hello\"}'"), Sh("cmd", "true")) },
        });
        CommandEntry cmd = Cmd(session, "cmd");
        GroupEntry group = session.AllGroups.Single();

        Assert.False(session.KnownEnvScriptValues(cmd).TryGet(group, out _));
        Assert.Null(session.KnownLaunchEnvironment(cmd).GroupValues[0]);

        EnvPreparation prep = await session.PrepareEnvAsync(new[] { cmd }, null, CancellationToken.None);

        Assert.True(prep.Succeeded, prep.Error);
        Assert.True(session.KnownEnvScriptValues(cmd).TryGet(group, out IReadOnlyDictionary<string, string?>? values));
        Assert.Equal("hello", values!["GREETING"]);
        Assert.Equal("hello", session.KnownLaunchEnvironment(cmd).GroupValues[0]!["GREETING"]);
    }

    [Fact]
    public async Task KnownEnvScriptValues_AreAbsentAfterAFailedRerun()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string gate = dir.File("gate.txt");
        EnvScriptSpec script = Script(
            "if [ -f '" + gate + "' ]; then exit 3; fi; printf '%s' '{\"A\":\"1\"}'",
            EnvScriptRerun.EachStart);
        using CmdrSession session = Open(dir, new CmdrConfig
        {
            Groups = new List<GroupSpec> { G("g", script, Sh("cmd", "true")) },
        });
        CommandEntry cmd = Cmd(session, "cmd");
        GroupEntry group = session.AllGroups.Single();
        IReadOnlyList<CommandEntry> commands = new[] { cmd };

        Assert.True((await session.PrepareEnvAsync(commands, null, CancellationToken.None)).Succeeded);
        Assert.True(session.KnownEnvScriptValues(cmd).TryGet(group, out _));

        File.WriteAllText(gate, string.Empty);
        Assert.False((await session.PrepareEnvAsync(commands, null, CancellationToken.None)).Succeeded);

        Assert.False(session.KnownEnvScriptValues(cmd).TryGet(group, out _));
        Assert.False(session.LastEnvScriptRun(group)!.Value.Result.Succeeded);
    }
}
