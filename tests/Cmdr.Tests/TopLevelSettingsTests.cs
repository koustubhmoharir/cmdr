using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

/// <summary>
/// The file-level environment in the info pane and the copy, and that the settings the dialogs do not edit (envScript, secret)
/// survive an edit.
/// </summary>
public sealed class TopLevelSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("cmdr-tui-toplevel-").FullName;
    private readonly CmdrSession _session;

    public TopLevelSettingsTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "shared"));
        File.WriteAllText(
            Path.Combine(_dir, "shared", "base.json"),
            """{ "env": { "IMPORTED": "from-base", "NOT_IMPORTED": "x" }, "groups": [ { "name": "shared" } ] }""");
        File.WriteAllText(
            Path.Combine(_dir, "cmdr.json"),
            """
            {
              "base": "shared/base.json",
              "baseEnv": [ "IMPORTED" ],
              "env": { "TOPVAR": "top-${cwd}" },
              "secret": [ "TOKEN" ],
              "groups": [
                {
                  "name": "main",
                  "envScript": { "program": "sh", "args": [ "-c", "true" ], "timeoutSeconds": 5 },
                  "secret": [ "GROUP_SECRET" ],
                  "items": [ { "name": "mine", "program": "tool", "secret": [ "CMD_SECRET" ] } ]
                }
              ]
            }
            """);
        _session = CmdrSession.Load(Path.Combine(_dir, "cmdr.json"), SessionMode.Edit);
    }

    public void Dispose()
    {
        _session.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: the temporary folder is left behind when it cannot be removed.
        }
    }

    [Fact]
    public void Details_ListTopLevelAndImportedVariables_WithTheirFinalValues()
    {
        CommandEntry entry = _session.AllEntries.Single(e => e.Name == "mine");
        var environment = new Dictionary<string, string?> { ["KEEP"] = "k" };

        IReadOnlyList<string> lines = DetailsText.Plain(DetailsText.CommandLines(entry, _session.ConfigDir, windows: false, environment, ':'));

        Assert.Contains($"TOPVAR={"top-" + _session.ConfigDir}", lines);
        Assert.Contains("IMPORTED=from-base", lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("NOT_IMPORTED", StringComparison.Ordinal));
    }

    [Fact]
    public void Copy_IncludesTopLevelAndImportedVariables()
    {
        CommandEntry entry = _session.AllEntries.Single(e => e.Name == "mine");
        var environment = new Dictionary<string, string?> { ["KEEP"] = "k" };

        string copy = CommandLineFormatter.ForCopy(entry, powerShell: false, environment, ':');

        Assert.Contains("TOPVAR=", copy, StringComparison.Ordinal);
        Assert.Contains("IMPORTED=from-base", copy, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandForm_KeepsTheCommandSecretOnEdit()
    {
        var values = new CommandFormValues("mine", string.Empty, "tool", string.Empty, string.Empty, string.Empty, StopMethod.CtrlC, string.Empty, false, string.Empty, false, StdinMode.Pipe, KeptSecret: new[] { "CMD_SECRET" });

        Assert.Equal(new[] { "CMD_SECRET" }, CommandForm.Build(values).Secret);
    }

    [Fact]
    public void EditingAGroup_WithTheCurrentEnvScriptAndSecret_KeepsThem()
    {
        GroupEntry group = _session.Groups.Single(g => g.Name == "main");

        _session.UpdateGroup(group, new GroupSettings("renamed", null, GroupMode.Parallel, null, null, true, group.EnvScript, group.Secret), null);

        Assert.Equal("renamed", group.Name);
        Assert.Equal("sh", group.EnvScript!.Program);
        Assert.Equal(new[] { "GROUP_SECRET" }, group.Secret);
    }

    [Fact]
    public void EditingAGroup_WithoutThemInTheSettings_ClearsThem()
    {
        // The settings are applied as they are: a caller that does not pass the script and the secrets clears them.
        GroupEntry group = _session.Groups.Single(g => g.Name == "main");

        _session.UpdateGroup(group, new GroupSettings("renamed", null, GroupMode.Parallel, null, null), null);

        Assert.Null(group.EnvScript);
        Assert.Null(group.Secret);
    }

    [Fact]
    public void Save_KeepsTheTopLevelSettingsAndTheGroupAndCommandSecrets()
    {
        CmdrConfig config = _session.BuildConfig();

        Assert.Equal("top-${cwd}", config.Env!["TOPVAR"]);
        Assert.Equal(new[] { "IMPORTED" }, config.BaseEnv);
        Assert.Equal(new[] { "TOKEN" }, config.Secret);
        GroupSpec group = Assert.Single(config.Groups);
        Assert.Equal(new[] { "GROUP_SECRET" }, group.Secret);
        Assert.Equal(new[] { "CMD_SECRET" }, Assert.IsType<CommandSpec>(Assert.Single(group.Items)).Secret);
        Assert.Equal(5, group.EnvScript!.TimeoutSeconds);
    }
}
