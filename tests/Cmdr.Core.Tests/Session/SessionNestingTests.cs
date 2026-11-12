using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Tests.Config;
using Cmdr.Core.Tests.Processes;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;

namespace Cmdr.Core.Tests.Session;

/// <summary>The temporary flat session over a config that has group settings but no nested groups.</summary>
public class SessionNestingTests
{
    [Fact]
    public void Load_NestedGroup_IsRejected()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        ConfigStore.Save(
            new CmdrConfig
            {
                Groups =
                {
                    new GroupSpec
                    {
                        Name = "outer",
                        Items = { new GroupSpec { Name = "inner", Items = { new CommandSpec { Name = "a", Program = "p" } } } },
                    },
                },
            },
            path);

        var ex = Assert.Throws<ConfigException>(() => Cmdr.Core.Session.Session.Load(path));

        Assert.Equal("Nested groups are not supported yet.", ex.Message);
    }

    [Fact]
    public void Save_PreservesGroupCwdAndEnv_AndItems()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        ConfigStore.Save(
            new CmdrConfig
            {
                Groups =
                {
                    new GroupSpec
                    {
                        Name = "g",
                        Cwd = "sub",
                        Env = new Dictionary<string, string?> { ["A"] = "1", ["GONE"] = null },
                        Items = { Sh("one", "true"), Sh("two", "true") },
                    },
                },
            },
            path);

        using (var session = Cmdr.Core.Session.Session.Load(path))
        {
            session.Save();
        }

        GroupSpec group = Assert.Single(ConfigStore.Load(path).Groups);
        Assert.Equal("sub", group.Cwd);
        Assert.Equal(new Dictionary<string, string?> { ["A"] = "1", ["GONE"] = null }, group.Env);
        Assert.Equal(new[] { "one", "two" }, group.Items.Select(i => i.Name));
        Assert.All(group.Items, i => Assert.IsType<CommandSpec>(i));
    }

    [Fact]
    public void Launch_AppliesGroupEnvAndCwd()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));
        string path = dir.File("cmdr.json");
        var command = new CommandSpec
        {
            Name = "show",
            Program = "sh",
            Args = { "-c", "echo \"$FOO\"; pwd" },
        };
        ConfigStore.Save(
            new CmdrConfig
            {
                Groups = { new GroupSpec { Name = "g", Cwd = "sub", Env = new Dictionary<string, string?> { ["FOO"] = "from-group" }, Items = { command } } },
            },
            path);

        using var session = Cmdr.Core.Session.Session.Load(path);
        var entry = session.Groups[0].Commands[0];
        Assert.Null(session.Activate(new[] { entry }));
        try
        {
            ProcessTestHelpers.WaitForExited(entry.Process);
            Assert.Contains("from-group", ProcessTestHelpers.Text(entry.Process));
        }
        finally
        {
            ProcessTestHelpers.Cleanup(entry.Process);
        }
    }
}
