using System.Globalization;
using System.Reflection;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

/// <summary>
/// Environment scripts in the UI: Ctrl+R runs the scripts of the focused row or shown command again at the next start (with its message,
/// and in edit mode the run-mode message), its hint shows only when a script applies, the info pane shows each script's command and last
/// run with its variables, the command's environment includes the values known so far, and the copy carries them and says when a script
/// has not run. The scripts are real <c>sh</c> scripts, so these tests run on Linux only.
/// </summary>
[Collection("Terminal GUI")]
public sealed class EnvScriptInfoTests : IDisposable
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string RunOnlyMessage = "Press Ctrl+E for run mode to start or stop commands.";
    private const string WillRunAgain = "Environment scripts will run again at the next start";
    private const string NoneApply = "No environment scripts apply here";
    private const string CopiedPartially = "Copied; variables of env scripts without a successful run are not included";

    private readonly string _dir = Directory.CreateTempSubdirectory("cmdr-env-info-").FullName;
    private CmdrSession? _session;

    public void Dispose()
    {
        _session?.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: the temporary folder is left behind when it cannot be removed.
        }
    }

    /// <summary>A script that runs <c>sh -c shell</c>.</summary>
    private static EnvScriptSpec Shell(string shell, EnvScriptRerun rerun = EnvScriptRerun.Once) =>
        new() { Program = "sh", Args = new List<string> { "-c", shell }, Rerun = rerun };

    /// <summary>A script that prints <paramref name="json"/> as its output.</summary>
    private static EnvScriptSpec Prints(string json) => Shell("printf '%s' '" + json + "'");

    /// <summary>A script that appends a line to <paramref name="log"/> and prints <paramref name="json"/>.</summary>
    private static EnvScriptSpec Logging(string log, string json) =>
        Shell("printf 'ran\\n' >> '" + log + "'; printf '%s' '" + json + "'");

    private static GroupSpec Group(string name, EnvScriptSpec? script, params ItemSpec[] items) =>
        new() { Name = name, EnvScript = script, Items = items.ToList() };

    private static CommandSpec Command(string name) => new() { Name = name, Program = "tool" };

    /// <summary>Writes the configuration to a file in the sandbox and loads it in <paramref name="mode"/>.</summary>
    private CmdrSession Open(CmdrConfig config, SessionMode mode = SessionMode.Run)
    {
        string path = Path.Combine(_dir, "cmdr.json");
        ConfigStore.Save(config, path);
        _session = CmdrSession.Load(path, mode);
        return _session;
    }

    /// <summary>Runs an action with a controller on a session, with the terminal driver of the tests.</summary>
    private static void WithController(CmdrSession session, Action<MainController> body)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, session);
        controller.Build();
        body(controller);
    }

    private static CommandEntry Cmd(CmdrSession session, string name) => session.AllEntries.Single(entry => entry.Name == name);

    private static GroupEntry Grp(CmdrSession session, string name) => session.AllGroups.Single(group => group.Name == name);

    /// <summary>Runs the preparation of the commands to completion, as the start does.</summary>
    private static EnvPreparation Prepare(CmdrSession session, params CommandEntry[] commands) =>
        session.PrepareEnvAsync(commands, null, CancellationToken.None).GetAwaiter().GetResult();

    private static string Time(DateTimeOffset at) => at.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static void FocusKey(MainController controller, object key) =>
        typeof(MainController).GetMethod("FocusKey", Members)!.Invoke(controller, new[] { key });

    private static string? Message(MainController controller) =>
        (string?)typeof(MainController).GetField("_message", Members)!.GetValue(controller);

    private static void ClearMessage(MainController controller) =>
        typeof(MainController).GetField("_message", Members)!.SetValue(controller, null);

    private static string HintLeft(MainController controller) =>
        (string)typeof(MainController).GetMethod("HintLeft", Members)!.Invoke(controller, null)!;

    private static T Field<T>(MainController controller, string name) =>
        (T)typeof(MainController).GetField(name, Members)!.GetValue(controller)!;

    /// <summary>The plain text of the info pane's lines, as the pane builds them for the focused row.</summary>
    private static IReadOnlyList<string> PaneLines(MainController controller)
    {
        var details = Field<DetailsView>(controller, "_details");
        var lines = (IReadOnlyList<DetailsLine>)typeof(DetailsView).GetMethod("Lines", Members)!.Invoke(details, null)!;
        return DetailsText.Plain(lines);
    }

    [Fact]
    public void CtrlR_OnAGroupWithAScript_SaysItWillRunAgainAtTheNextStart()
    {
        if (!OperatingSystem.IsLinux()) return;

        CmdrSession session = Open(new CmdrConfig { Groups = new List<GroupSpec> { Group("g", Prints("{}"), Command("c")) } });
        WithController(session, controller =>
        {
            FocusKey(controller, Grp(session, "g"));
            Field<CommandListView>(controller, "_list").SetFocus();
            ClearMessage(controller);

            Assert.True(controller.OnListKey(Key.R.WithCtrl));

            Assert.Equal(WillRunAgain, Message(controller));
        });
    }

    [Fact]
    public void CtrlR_OnACommandWithoutScripts_SaysNoScriptApplies()
    {
        CmdrSession session = Open(new CmdrConfig { Groups = new List<GroupSpec> { Group("g", null, Command("c")) } });
        WithController(session, controller =>
        {
            FocusKey(controller, Cmd(session, "c"));
            Field<CommandListView>(controller, "_list").SetFocus();
            ClearMessage(controller);

            Assert.True(controller.OnListKey(Key.R.WithCtrl));

            Assert.Equal(NoneApply, Message(controller));
        });
    }

    [Fact]
    public void CtrlR_InEditMode_SaysHowToReachRunMode_AndRunsNothing()
    {
        CmdrSession session = Open(new CmdrConfig { Groups = new List<GroupSpec> { Group("g", Prints("{}"), Command("c")) } }, SessionMode.Edit);
        WithController(session, controller =>
        {
            FocusKey(controller, Cmd(session, "c"));
            Field<CommandListView>(controller, "_list").SetFocus();
            ClearMessage(controller);

            Assert.True(controller.OnListKey(Key.R.WithCtrl));

            Assert.Equal(RunOnlyMessage, Message(controller));
        });
    }

    [Fact]
    public void CtrlR_OnAFileRow_IsSilent()
    {
        if (!OperatingSystem.IsLinux()) return;

        CmdrSession session = Open(new CmdrConfig
        {
            EnvScript = Prints("{}"),
            Groups = new List<GroupSpec> { Group("g", null, Command("c")) },
        });
        WithController(session, controller =>
        {
            FocusKey(controller, session.MainSource);
            Field<CommandListView>(controller, "_list").SetFocus();
            ClearMessage(controller);

            Assert.True(controller.OnListKey(Key.R.WithCtrl));

            Assert.Null(Message(controller));
        });
    }

    [Fact]
    public void CtrlR_ForcesARerunOfAOnceScript_AtTheNextStart()
    {
        if (!OperatingSystem.IsLinux()) return;

        string log = Path.Combine(_dir, "log.txt");
        CmdrSession session = Open(new CmdrConfig
        {
            Groups = new List<GroupSpec> { Group("g", Logging(log, "{}"), Command("c")) },
        });
        CommandEntry cmd = Cmd(session, "c");
        Assert.True(Prepare(session, cmd).Succeeded);
        Assert.True(Prepare(session, cmd).Succeeded);
        Assert.Single(File.ReadAllLines(log));

        WithController(session, controller =>
        {
            FocusKey(controller, cmd);
            Field<CommandListView>(controller, "_list").SetFocus();
            Assert.True(controller.OnListKey(Key.R.WithCtrl));
        });

        Assert.True(Prepare(session, cmd).Succeeded);
        Assert.Equal(2, File.ReadAllLines(log).Length);
    }

    [Fact]
    public void CtrlR_InTheInfoPane_ActsOnTheShownCommand()
    {
        CmdrSession session = Open(new CmdrConfig { Groups = new List<GroupSpec> { Group("g", Prints("{}"), Command("c")) } });
        WithController(session, controller =>
        {
            FocusKey(controller, Cmd(session, "c"));
            Field<DetailsView>(controller, "_details").SetFocus();
            ClearMessage(controller);

            Assert.True(controller.OnShownCommandKey(Key.R.WithCtrl));

            Assert.Equal(WillRunAgain, Message(controller));
        });
    }

    [Fact]
    public void Hint_ShowsCtrlREnv_OnlyWhenAScriptApplies_InTheListAndTheInfoPane()
    {
        CmdrSession session = Open(new CmdrConfig
        {
            Groups = new List<GroupSpec>
            {
                Group("scripted", Prints("{}"), Command("with")),
                Group("plain", null, Command("without")),
            },
        });
        WithController(session, controller =>
        {
            Field<CommandListView>(controller, "_list").SetFocus();
            FocusKey(controller, Cmd(session, "with"));
            Assert.Contains("Del Clear  ^R Env", HintLeft(controller), StringComparison.Ordinal);

            FocusKey(controller, Grp(session, "scripted"));
            Assert.Contains("Del Clear  ^R Env", HintLeft(controller), StringComparison.Ordinal);

            FocusKey(controller, Cmd(session, "without"));
            Assert.DoesNotContain("^R Env", HintLeft(controller), StringComparison.Ordinal);

            FocusKey(controller, Cmd(session, "with"));
            Field<DetailsView>(controller, "_details").SetFocus();
            Assert.Contains("Del Clear  ^R Env", HintLeft(controller), StringComparison.Ordinal);

            FocusKey(controller, Cmd(session, "without"));
            Assert.DoesNotContain("^R Env", HintLeft(controller), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void GroupInfo_ShowsTheScriptAndItsStatus_NotRunYet_ThenTheLastRun_ThenItsFailure()
    {
        if (!OperatingSystem.IsLinux()) return;

        CmdrSession session = Open(new CmdrConfig
        {
            Groups = new List<GroupSpec> { Group("g", Shell("printf '%s' '{\"A\":\"1\",\"B\":null}'"), Command("c")) },
        });
        GroupEntry group = Grp(session, "g");

        IReadOnlyList<string> before = DetailsText.Plain(DetailsText.GroupLines(group, session.ConfigDir, session.LastEnvScriptRun(group)));
        Assert.Equal("envScript: sh -c printf '%s' '{\"A\":\"1\",\"B\":null}'", before[^2]);
        Assert.Equal("not run yet", before[^1]);

        Assert.True(Prepare(session, Cmd(session, "c")).Succeeded);
        (DateTimeOffset finishedAt, EnvScriptResult result)? run = session.LastEnvScriptRun(group);
        IReadOnlyList<string> after = DetailsText.Plain(DetailsText.GroupLines(group, session.ConfigDir, run));
        int status = after.ToList().FindIndex(line => line.StartsWith("last run ", StringComparison.Ordinal));
        Assert.Equal(
            $"last run {Time(run!.Value.finishedAt)} ({run.Value.result.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s): 2 variables",
            after[status]);
        Assert.Equal(new[] { "A=1", "B (unset)" }, after.Skip(status + 1).Take(2));
    }

    [Fact]
    public void GroupInfo_ShowsAFailedRun_WithItsError()
    {
        if (!OperatingSystem.IsLinux()) return;

        CmdrSession session = Open(new CmdrConfig
        {
            Groups = new List<GroupSpec> { Group("g", Shell("exit 2"), Command("c")) },
        });
        GroupEntry group = Grp(session, "g");
        Assert.False(Prepare(session, Cmd(session, "c")).Succeeded);

        (DateTimeOffset finishedAt, EnvScriptResult result)? run = session.LastEnvScriptRun(group);
        IReadOnlyList<string> lines = DetailsText.Plain(DetailsText.GroupLines(group, session.ConfigDir, run));

        Assert.Equal($"last run {Time(run!.Value.finishedAt)} failed: {run.Value.result.Error}", lines[^1]);
    }

    [Fact]
    public void FileInfo_ShowsItsScriptsStatusAndVariables_AfterTheScriptLine()
    {
        if (!OperatingSystem.IsLinux()) return;

        CmdrSession session = Open(new CmdrConfig
        {
            EnvScript = Prints("{\"FILE_VAR\":\"v\"}"),
            Groups = new List<GroupSpec> { Group("g", null, Command("c")) },
        });
        ConfigSource file = session.MainSource;
        var environment = new Dictionary<string, string?>();

        IReadOnlyList<string> before = DetailsText.Plain(DetailsText.FileLines(file, environment, false, ':', session.LastEnvScriptRun(file)));
        int index = before.ToList().FindIndex(line => line.StartsWith("envScript: ", StringComparison.Ordinal));
        Assert.Equal("not run yet", before[index + 1]);

        Assert.True(Prepare(session, Cmd(session, "c")).Succeeded);
        (DateTimeOffset finishedAt, EnvScriptResult result)? run = session.LastEnvScriptRun(file);
        IReadOnlyList<string> after = DetailsText.Plain(DetailsText.FileLines(file, environment, false, ':', run));

        Assert.StartsWith($"last run {Time(run!.Value.finishedAt)} (", after[index + 1], StringComparison.Ordinal);
        Assert.Equal("FILE_VAR=v", after[index + 2]);
    }

    [Fact]
    public void CommandInfo_IncludesTheKnownScriptValues_AndNamesEachScriptThatHasNotRun()
    {
        if (!OperatingSystem.IsLinux()) return;

        CmdrSession session = Open(new CmdrConfig
        {
            Groups = new List<GroupSpec> { Group("g", Prints("{\"GREETING\":\"hello\"}"), Command("c")) },
        });
        CommandEntry cmd = Cmd(session, "c");
        WithController(session, controller =>
        {
            FocusKey(controller, cmd);

            IReadOnlyList<string> before = PaneLines(controller);
            Assert.Contains("env script of group 'g' has no successful run yet; its variables are not shown", before);
            Assert.DoesNotContain("GREETING=hello", before);

            Assert.True(Prepare(session, cmd).Succeeded);
            FocusKey(controller, cmd);

            IReadOnlyList<string> after = PaneLines(controller);
            Assert.Contains("GREETING=hello", after);
            Assert.DoesNotContain(after, line => line.Contains("has no successful run yet", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Copy_CarriesTheKnownScriptValues_AndTheMessageSaysWhenAScriptHasNotRun()
    {
        if (!OperatingSystem.IsLinux()) return;

        CmdrSession session = Open(new CmdrConfig
        {
            Groups = new List<GroupSpec> { Group("g", Prints("{\"GREETING\":\"hello\"}"), Command("c")) },
        });
        CommandEntry cmd = Cmd(session, "c");
        var environment = new Dictionary<string, string?>();

        WithController(session, controller =>
        {
            FocusKey(controller, cmd);
            ClearMessage(controller);

            controller.CopyCommandLine(FocusedRow(controller));

            Assert.Equal(CopiedPartially, Message(controller));
        });
        Assert.DoesNotContain("GREETING=hello", CommandLineFormatter.ForCopy(cmd, false, environment, ':', null));

        Assert.True(Prepare(session, cmd).Succeeded);
        Assert.Contains(
            "GREETING=hello",
            CommandLineFormatter.ForCopy(cmd, false, environment, ':', session.KnownLaunchEnvironment(cmd)));
        WithController(session, controller =>
        {
            FocusKey(controller, cmd);
            ClearMessage(controller);

            controller.CopyCommandLine(FocusedRow(controller));

            Assert.Equal("Copied command line", Message(controller));
        });
    }

    /// <summary>The row the list focuses, which the copy reads.</summary>
    private static ListRow? FocusedRow(MainController controller) => (ListRow?)typeof(MainController).GetProperty("FocusedRow", Members)!.GetValue(controller);
}
