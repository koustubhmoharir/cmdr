using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>A cwd that uses <c>${env:NAME}</c> in a running session: an environment script's cwd, and a start with a variable that is not set.</summary>
public class SessionCwdPlaceholdersTests
{
    private static CmdrSession Open(TempDir dir, CmdrConfig config)
    {
        ConfigStore.Save(config, dir.File("cmdr.json"));
        return CmdrSession.Load(dir.File("cmdr.json"), SessionMode.Run);
    }

    [Fact]
    public async Task EnvScriptCwd_UsesAVariableOfAnOuterGroup()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("sub"));
        string log = dir.File("pwd.txt");
        var script = new EnvScriptSpec
        {
            Program = "sh",
            Args = new List<string> { "-c", "pwd > '" + log + "'; printf '{}'" },
            Cwd = "${env:D}",
        };
        using CmdrSession session = Open(dir, new CmdrConfig
        {
            Groups = new List<GroupSpec>
            {
                new()
                {
                    Name = "outer",
                    Env = new Dictionary<string, string?> { ["D"] = "sub" },
                    Items = new List<ItemSpec>
                    {
                        new GroupSpec { Name = "inner", EnvScript = script, Items = new List<ItemSpec> { Sh("cmd", "true") } },
                    },
                },
            },
        });
        CommandEntry cmd = session.AllEntries.Single(entry => entry.Name == "cmd");

        EnvPreparation prep = await session.PrepareEnvAsync(new[] { cmd }, null, CancellationToken.None);

        Assert.True(prep.Succeeded, prep.Error);
        Assert.Equal("sub", Path.GetFileName(File.ReadAllText(log).Trim()));
    }

    [Fact]
    public async Task EnvScriptCwd_UnsetVariable_FailsTheStartWithoutRunningTheScript()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string log = dir.File("ran.txt");
        using CmdrSession session = Open(dir, new CmdrConfig
        {
            Groups = new List<GroupSpec>
            {
                new()
                {
                    Name = "g",
                    EnvScript = new EnvScriptSpec
                    {
                        Program = "sh",
                        Args = new List<string> { "-c", "touch '" + log + "'; printf '{}'" },
                        Cwd = "${env:CMDR_TEST_UNSET_SCRIPT_CWD}",
                    },
                    Items = new List<ItemSpec> { Sh("cmd", "true") },
                },
            },
        });
        CommandEntry cmd = session.AllEntries.Single(entry => entry.Name == "cmd");

        EnvPreparation prep = await session.PrepareEnvAsync(new[] { cmd }, null, CancellationToken.None);

        Assert.False(prep.Succeeded);
        Assert.Equal("group 'g'", prep.FailedOwnerLabel);
        Assert.Contains("env script of group 'g' failed: cwd '${env:CMDR_TEST_UNSET_SCRIPT_CWD}': variable CMDR_TEST_UNSET_SCRIPT_CWD is not set", Text(cmd.Process));
        Assert.False(File.Exists(log));
    }

    [Fact]
    public void Start_WithAnUnsetCwdVariable_WritesFailedToStart_AndLaunchesNothing()
    {
        using var dir = new TempDir();
        using CmdrSession session = Open(dir, new CmdrConfig
        {
            Groups = new List<GroupSpec>
            {
                new() { Name = "g", Cwd = "${env:CMDR_TEST_UNSET_START_CWD}", Items = new List<ItemSpec> { Sh("cmd", "true") } },
            },
        });
        CommandEntry cmd = session.AllEntries.Single(entry => entry.Name == "cmd");

        session.Activate(new[] { cmd });

        Assert.Contains("failed to start: cwd '${env:CMDR_TEST_UNSET_START_CWD}': variable CMDR_TEST_UNSET_START_CWD is not set", Text(cmd.Process));
        Assert.Equal(ProcessState.NotStarted, cmd.Process.Snapshot.State);
    }
}
