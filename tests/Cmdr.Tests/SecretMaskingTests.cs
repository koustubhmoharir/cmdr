using System.Reflection;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Terminal.Gui.App;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

/// <summary>
/// Secret variables in the UI: the info pane masks the value of every secret env line (of a command, a group, a file and the variables
/// of an env script run), the copy leaves secret values out and says so, and a name that only shares a prefix with a secret is not masked.
/// The env script run uses a real <c>sh</c> script, so that test runs on Linux only.
/// </summary>
[Collection("Terminal GUI")]
public sealed class SecretMaskingTests : IDisposable
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Mask = "••••";
    private const string SecretsLeftOut = "Copied; secret values are left out";
    private const string SecretsAndScripts = "Copied; secret values and variables of env scripts without a successful run are left out";
    private const string ScriptsNotIncluded = "Copied; variables of env scripts without a successful run are not included";

    private static readonly IReadOnlyDictionary<string, string?> ProcessEnvironment = new Dictionary<string, string?>
    {
        ["PROCESS_VAR"] = "process",
    };

    private readonly string _dir = Directory.CreateTempSubdirectory("cmdr-secret-mask-").FullName;
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

    /// <summary>
    /// The file has the secret FILE_SECRET and a plain FILE_PLAIN. Its group <c>g</c> has the secret GROUP_SECRET and TOKEN, a plain TOKEN_ID
    /// (same prefix as TOKEN, not secret), and a command <c>run</c> with the secret CMD_SECRET and a removal of GONE_SECRET (secret), whose
    /// arguments are <c>${env:TOKEN}</c>, <c>${env:TOKEN_ID}</c> and <c>plain</c>.
    /// </summary>
    private static CmdrConfig SecretConfig() => new()
    {
        Secret = new() { "FILE_SECRET" },
        Env = new() { ["FILE_SECRET"] = "file-s3cret", ["FILE_PLAIN"] = "file-visible" },
        Groups =
        {
            new GroupSpec
            {
                Name = "g",
                Secret = new() { "GROUP_SECRET", "TOKEN" },
                Env = new() { ["GROUP_SECRET"] = "group-s3cret", ["TOKEN"] = "token-s3cret", ["TOKEN_ID"] = "public-id" },
                Items =
                {
                    new CommandSpec
                    {
                        Name = "run",
                        Program = "tool",
                        Args = new() { "${env:TOKEN}", "${env:TOKEN_ID}", "plain" },
                        Secret = new() { "CMD_SECRET", "GONE_SECRET" },
                        Env = new() { ["CMD_SECRET"] = "cmd-s3cret", ["CMD_PLAIN"] = "cmd-plain", ["GONE_SECRET"] = null },
                    },
                },
            },
        },
    };

    /// <summary>Writes the configuration to a file in the sandbox and loads it in run mode.</summary>
    private CmdrSession Open(CmdrConfig config)
    {
        string path = Path.Combine(_dir, "cmdr.json");
        ConfigStore.Save(config, path);
        _session = CmdrSession.Load(path, SessionMode.Run);
        return _session;
    }

    private static CommandEntry Cmd(CmdrSession session, string name) => session.AllEntries.Single(entry => entry.Name == name);

    private static GroupEntry Grp(CmdrSession session, string name) => session.AllGroups.Single(group => group.Name == name);

    private static string Dir(CmdrSession session) => Path.GetFullPath(session.ConfigDir);

    /// <summary>The command's info pane lines, as the info pane builds them, with its secret names.</summary>
    private static IReadOnlyList<DetailsLine> CommandLinesOf(CmdrSession session, CommandEntry entry) =>
        DetailsText.CommandLines(
            entry,
            Dir(session),
            windows: false,
            ProcessEnvironment,
            ':',
            known: null,
            notRunLabels: null,
            secret: session.SecretNamesFor(entry));

    /// <summary>Runs an action with a controller on a session, with the terminal driver of the tests.</summary>
    private static void WithController(CmdrSession session, Action<MainController> body)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, session);
        controller.Build();
        body(controller);
    }

    private static void FocusKey(MainController controller, object key) =>
        typeof(MainController).GetMethod("FocusKey", Members)!.Invoke(controller, new[] { key });

    private static string? Message(MainController controller) =>
        (string?)typeof(MainController).GetField("_message", Members)!.GetValue(controller);

    private static void ClearMessage(MainController controller) =>
        typeof(MainController).GetField("_message", Members)!.SetValue(controller, null);

    private static ListRow? FocusedRow(MainController controller) =>
        (ListRow?)typeof(MainController).GetProperty("FocusedRow", Members)!.GetValue(controller);

    /// <summary>The message after copying the command from the list, as a user does with <c>c</c>.</summary>
    private static string? CopyMessage(CmdrSession session, CommandEntry entry)
    {
        string? message = null;
        WithController(session, controller =>
        {
            FocusKey(controller, entry);
            ClearMessage(controller);
            controller.CopyCommandLine(FocusedRow(controller));
            message = Message(controller);
        });
        return message;
    }

    [Fact]
    public void A_command_masks_the_values_of_its_secret_env_lines_and_keeps_the_others()
    {
        CmdrSession session = Open(SecretConfig());
        IReadOnlyList<string> lines = DetailsText.Plain(CommandLinesOf(session, Cmd(session, "run")));

        Assert.Contains($"TOKEN={Mask}", lines);
        Assert.Contains($"GROUP_SECRET={Mask}", lines);
        Assert.Contains($"FILE_SECRET={Mask}", lines);
        Assert.Contains($"CMD_SECRET={Mask}", lines);
        Assert.Contains("GONE_SECRET (unset)", lines);
        Assert.Contains("FILE_PLAIN=file-visible", lines);
        Assert.Contains("TOKEN_ID=public-id", lines);
        Assert.Contains("CMD_PLAIN=cmd-plain", lines);
        Assert.DoesNotContain(lines, line => line.Contains("s3cret", StringComparison.Ordinal));
    }

    [Fact]
    public void The_selection_copy_copies_the_masked_text_the_pane_shows()
    {
        CmdrSession session = Open(SecretConfig());
        IReadOnlyList<DetailsLine> lines = CommandLinesOf(session, Cmd(session, "run"));

        IReadOnlyList<string> texts = DetailsText.LogicalTexts(lines);

        Assert.Contains($"TOKEN={Mask}", texts);
        Assert.DoesNotContain(texts, text => text.Contains("token-s3cret", StringComparison.Ordinal));
    }

    [Fact]
    public void A_group_masks_the_values_of_its_secret_env_lines()
    {
        CmdrSession session = Open(SecretConfig());
        GroupEntry group = Grp(session, "g");

        IReadOnlyList<string> lines = DetailsText.Plain(DetailsText.GroupLines(group, Dir(session), secret: session.SecretNamesFor(group)));

        Assert.Contains($"GROUP_SECRET={Mask}", lines);
        Assert.Contains($"TOKEN={Mask}", lines);
        Assert.Contains("TOKEN_ID=public-id", lines);
        Assert.DoesNotContain(lines, line => line.Contains("s3cret", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_masks_the_values_of_its_secret_env_lines_and_still_lists_the_names()
    {
        CmdrSession session = Open(SecretConfig());
        ConfigSource file = session.MainSource;

        IReadOnlyList<string> lines = DetailsText.Plain(DetailsText.FileLines(
            file,
            ProcessEnvironment,
            windows: false,
            pathSeparator: ':',
            secret: session.SecretNamesFor(file)));

        Assert.Contains($"FILE_SECRET={Mask}", lines);
        Assert.Contains("FILE_PLAIN=file-visible", lines);
        Assert.Contains("secret: FILE_SECRET", lines);
        Assert.DoesNotContain(lines, line => line.Contains("file-s3cret", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_secret_set_the_values_are_shown_as_before()
    {
        CmdrSession session = Open(SecretConfig());

        IReadOnlyList<string> lines = DetailsText.Plain(DetailsText.CommandLines(
            Cmd(session, "run"),
            Dir(session),
            windows: false,
            ProcessEnvironment,
            ':'));

        Assert.Contains("TOKEN=token-s3cret", lines);
    }

    [Fact]
    public void The_variables_of_an_env_script_run_are_masked_for_a_group_and_a_file_by_their_own_sets()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // The script is an sh script.
        }

        CmdrSession session = Open(new CmdrConfig
        {
            Secret = new() { "FILE_SCRIPT_SECRET" },
            EnvScript = Shell("printf '{\"FILE_SCRIPT_SECRET\":\"%s\",\"FILE_SCRIPT_PLAIN\":\"file-ok\"}' \"$(echo file-ran)-s3cret\""),
            Groups =
            {
                new GroupSpec
                {
                    Name = "g",
                    Secret = new() { "GROUP_SCRIPT_SECRET" },
                    EnvScript = Shell("printf '{\"GROUP_SCRIPT_SECRET\":\"%s\",\"GROUP_SCRIPT_PLAIN\":\"group-ok\"}' \"$(echo group-ran)-s3cret\""),
                    Items = { new CommandSpec { Name = "run", Program = "tool" } },
                },
            },
        });
        Assert.True(Prepare(session, Cmd(session, "run")).Succeeded);
        GroupEntry group = Grp(session, "g");
        ConfigSource file = session.MainSource;

        IReadOnlyList<string> groupLines = DetailsText.Plain(DetailsText.GroupLines(
            group,
            Dir(session),
            session.LastEnvScriptRun(group),
            session.SecretNamesFor(group)));
        IReadOnlyList<string> fileLines = DetailsText.Plain(DetailsText.FileLines(
            file,
            ProcessEnvironment,
            windows: false,
            pathSeparator: ':',
            session.LastEnvScriptRun(file),
            session.SecretNamesFor(file)));

        Assert.Contains($"GROUP_SCRIPT_SECRET={Mask}", groupLines);
        Assert.Contains("GROUP_SCRIPT_PLAIN=group-ok", groupLines);
        Assert.DoesNotContain(groupLines, line => line.Contains("group-ran-s3cret", StringComparison.Ordinal));
        Assert.Contains($"FILE_SCRIPT_SECRET={Mask}", fileLines);
        Assert.Contains("FILE_SCRIPT_PLAIN=file-ok", fileLines);
        Assert.DoesNotContain(fileLines, line => line.Contains("file-ran-s3cret", StringComparison.Ordinal));
    }

    [Fact]
    public void The_copy_leaves_out_secret_assignments_and_keeps_a_secret_removal()
    {
        CmdrSession session = Open(SecretConfig());
        CommandEntry run = Cmd(session, "run");

        string line = CommandLineFormatter.ForCopy(run, false, ProcessEnvironment, ':', null, session.SecretNamesFor(run));

        Assert.DoesNotContain("s3cret", line, StringComparison.Ordinal);
        Assert.DoesNotContain("TOKEN=", line, StringComparison.Ordinal);
        Assert.DoesNotContain("CMD_SECRET=", line, StringComparison.Ordinal);
        Assert.Contains("-u GONE_SECRET", line, StringComparison.Ordinal);
        Assert.Contains("TOKEN_ID=public-id", line, StringComparison.Ordinal);
        Assert.Contains("FILE_PLAIN=file-visible", line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_copy_keeps_an_argument_with_a_secret_reference_as_written_and_expands_the_others()
    {
        CmdrSession session = Open(SecretConfig());
        CommandEntry run = Cmd(session, "run");
        string dir = CommandLineFormatter.QuotePosix(Dir(session));

        string line = CommandLineFormatter.ForCopy(run, false, ProcessEnvironment, ':', null, session.SecretNamesFor(run));

        Assert.Equal(
            $"(cd {dir} && env -u GONE_SECRET FILE_PLAIN=file-visible TOKEN_ID=public-id CMD_PLAIN=cmd-plain tool '${{env:TOKEN}}' public-id plain)",
            line);
    }

    [Fact]
    public void The_copy_without_a_secret_set_is_as_before()
    {
        CmdrSession session = Open(SecretConfig());
        CommandEntry run = Cmd(session, "run");
        string dir = CommandLineFormatter.QuotePosix(Dir(session));

        string line = CommandLineFormatter.ForCopy(run, false, ProcessEnvironment, ':');

        string assignments = "FILE_SECRET=file-s3cret FILE_PLAIN=file-visible GROUP_SECRET=group-s3cret TOKEN=token-s3cret "
            + "TOKEN_ID=public-id CMD_SECRET=cmd-s3cret CMD_PLAIN=cmd-plain";
        Assert.Equal($"(cd {dir} && env -u GONE_SECRET {assignments} tool token-s3cret public-id plain)", line);
    }

    [Fact]
    public void The_copy_message_says_secret_values_are_left_out()
    {
        CmdrSession session = Open(SecretConfig());

        Assert.Equal(SecretsLeftOut, CopyMessage(session, Cmd(session, "run")));
    }

    [Fact]
    public void The_copy_message_combines_secrets_and_scripts_not_run()
    {
        CmdrConfig config = SecretConfig();
        config.Groups[0].EnvScript = Prints("{\"X\":\"1\"}");
        CmdrSession session = Open(config);

        Assert.Equal(SecretsAndScripts, CopyMessage(session, Cmd(session, "run")));
    }

    [Fact]
    public void The_copy_message_keeps_the_script_message_when_no_secret_is_left_out()
    {
        CmdrConfig config = new()
        {
            Groups =
            {
                new GroupSpec
                {
                    Name = "h",
                    Secret = new() { "UNUSED" },
                    EnvScript = Prints("{\"X\":\"1\"}"),
                    Env = new() { ["PLAIN"] = "p" },
                    Items = { new CommandSpec { Name = "plain", Program = "tool", Args = new() { "x" } } },
                },
            },
        };
        CmdrSession session = Open(config);

        Assert.Equal(ScriptsNotIncluded, CopyMessage(session, Cmd(session, "plain")));
    }

    [Fact]
    public void The_copy_message_is_the_plain_one_when_a_secret_name_is_declared_but_not_used()
    {
        CmdrConfig config = new()
        {
            Groups =
            {
                new GroupSpec
                {
                    Name = "h",
                    Secret = new() { "UNUSED" },
                    Env = new() { ["PLAIN"] = "p" },
                    Items = { new CommandSpec { Name = "plain", Program = "tool", Args = new() { "x" } } },
                },
            },
        };
        CmdrSession session = Open(config);

        string expected = OperatingSystem.IsWindows() ? "Copied command line (PowerShell)" : "Copied command line";
        Assert.Equal(expected, CopyMessage(session, Cmd(session, "plain")));
    }

    [Fact]
    public void A_name_that_only_shares_a_prefix_with_a_secret_is_not_masked_or_left_out()
    {
        CmdrSession session = Open(new CmdrConfig
        {
            Groups =
            {
                new GroupSpec
                {
                    Name = "h",
                    Secret = new() { "TOKEN" },
                    Env = new() { ["TOKEN"] = "hidden-value", ["TOKEN_ID"] = "visible-id", ["TOKEN_ID2"] = "visible-two" },
                    Items = { new CommandSpec { Name = "run", Program = "tool", Args = new() { "${env:TOKEN_ID}" } } },
                },
            },
        });
        CommandEntry run = Cmd(session, "run");

        IReadOnlyList<string> lines = DetailsText.Plain(CommandLinesOf(session, run));
        string line = CommandLineFormatter.ForCopy(run, false, ProcessEnvironment, ':', null, session.SecretNamesFor(run));

        Assert.Contains($"TOKEN={Mask}", lines);
        Assert.Contains("TOKEN_ID=visible-id", lines);
        Assert.Contains("TOKEN_ID2=visible-two", lines);
        Assert.Contains("TOKEN_ID=visible-id", line, StringComparison.Ordinal);
        Assert.Contains("TOKEN_ID2=visible-two", line, StringComparison.Ordinal);
        Assert.Contains("tool visible-id", line, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden-value", line, StringComparison.Ordinal);
        // TOKEN's own assignment is left out, so the message reports it; TOKEN_ID is not part of that.
        Assert.Equal(SecretsLeftOut, CopyMessage(session, run));
    }

    [Fact]
    public void A_secret_name_that_is_not_in_the_copy_does_not_change_the_copy_message()
    {
        CmdrSession session = Open(new CmdrConfig
        {
            Groups =
            {
                new GroupSpec
                {
                    Name = "h",
                    Secret = new() { "TOKEN" },
                    Env = new() { ["TOKEN_ID"] = "visible-id" },
                    Items = { new CommandSpec { Name = "run", Program = "tool", Args = new() { "${env:TOKEN_ID}" } } },
                },
            },
        });

        string expected = OperatingSystem.IsWindows() ? "Copied command line (PowerShell)" : "Copied command line";
        Assert.Equal(expected, CopyMessage(session, Cmd(session, "run")));
    }

    /// <summary>
    /// A derived configuration: the file's secret FILE_SECRET; group <c>g</c> with DERIVED=${env:FILE_SECRET}/bin (secret through the file)
    /// and PLAIN_OK=fine (not referencing a secret); command <c>run</c> with CHAIN=${env:DERIVED}-x in its own env (a later level, so it
    /// sees DERIVED's value; secret through DERIVED), and the arguments <c>${env:DERIVED}</c> and <c>${env:PLAIN_OK}</c>.
    /// </summary>
    private static CmdrConfig DerivedConfig() => new()
    {
        Secret = new() { "FILE_SECRET" },
        Env = new() { ["FILE_SECRET"] = "file-s3cret" },
        Groups =
        {
            new GroupSpec
            {
                Name = "g",
                Env = new() { ["DERIVED"] = "${env:FILE_SECRET}/bin", ["PLAIN_OK"] = "fine" },
                Items =
                {
                    new CommandSpec
                    {
                        Name = "run",
                        Program = "tool",
                        Args = new() { "${env:DERIVED}", "${env:PLAIN_OK}" },
                        Env = new() { ["CHAIN"] = "${env:DERIVED}-x" },
                    },
                },
            },
        },
    };

    [Fact]
    public void A_derived_name_is_masked_in_the_command_and_the_group_and_a_plain_one_is_not()
    {
        CmdrSession session = Open(DerivedConfig());
        CommandEntry run = Cmd(session, "run");
        GroupEntry group = Grp(session, "g");

        IReadOnlyList<string> commandLines = DetailsText.Plain(CommandLinesOf(session, run));
        IReadOnlyList<string> groupLines = DetailsText.Plain(DetailsText.GroupLines(group, Dir(session), secret: session.SecretNamesFor(group)));

        Assert.Contains($"DERIVED={Mask}", commandLines);
        Assert.Contains($"CHAIN={Mask}", commandLines);
        Assert.Contains("PLAIN_OK=fine", commandLines);
        Assert.Contains($"DERIVED={Mask}", groupLines);
        Assert.Contains("PLAIN_OK=fine", groupLines);
        Assert.DoesNotContain(commandLines, line => line.Contains("file-s3cret", StringComparison.Ordinal));
        Assert.DoesNotContain(groupLines, line => line.Contains("file-s3cret", StringComparison.Ordinal));
    }

    [Fact]
    public void The_copy_leaves_out_derived_assignments_and_keeps_a_plain_one()
    {
        CmdrSession session = Open(DerivedConfig());
        CommandEntry run = Cmd(session, "run");
        string dir = CommandLineFormatter.QuotePosix(Dir(session));

        string line = CommandLineFormatter.ForCopy(run, false, ProcessEnvironment, ':', null, session.SecretNamesFor(run));

        Assert.DoesNotContain("DERIVED=", line, StringComparison.Ordinal);
        Assert.DoesNotContain("CHAIN=", line, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", line, StringComparison.Ordinal);
        Assert.Equal($"(cd {dir} && env PLAIN_OK=fine tool '${{env:DERIVED}}' fine)", line);
        Assert.Equal(SecretsLeftOut, CopyMessage(session, run));
    }

    [Fact]
    public void A_derived_name_in_a_command_env_is_masked_and_left_out_of_the_copy()
    {
        CmdrSession session = Open(new CmdrConfig
        {
            Secret = new() { "FILE_SECRET" },
            Env = new() { ["FILE_SECRET"] = "file-s3cret" },
            Groups =
            {
                new GroupSpec
                {
                    Name = "g",
                    Env = new() { ["X"] = "${env:FILE_SECRET}/bin", ["Z"] = "plain" },
                    Items =
                    {
                        new CommandSpec
                        {
                            Name = "run",
                            Program = "tool",
                            Env = new() { ["Y"] = "${env:X}" },
                        },
                    },
                },
            },
        });
        CommandEntry run = Cmd(session, "run");
        string dir = CommandLineFormatter.QuotePosix(Dir(session));

        IReadOnlyList<string> lines = DetailsText.Plain(CommandLinesOf(session, run));
        string line = CommandLineFormatter.ForCopy(run, false, ProcessEnvironment, ':', null, session.SecretNamesFor(run));

        Assert.Contains($"X={Mask}", lines);
        Assert.Contains($"Y={Mask}", lines);
        Assert.Contains("Z=plain", lines);
        Assert.Equal($"(cd {dir} && env Z=plain tool)", line);
    }

    /// <summary>A script that runs <c>sh -c shell</c>.</summary>
    private static EnvScriptSpec Shell(string shell) => new() { Program = "sh", Args = new List<string> { "-c", shell } };

    /// <summary>A script that prints <paramref name="json"/> as its output.</summary>
    private static EnvScriptSpec Prints(string json) => Shell("printf '%s' '" + json + "'");

    private static EnvPreparation Prepare(CmdrSession session, CommandEntry command) =>
        session.PrepareEnvAsync(new[] { command }, null, CancellationToken.None).GetAwaiter().GetResult();
}
