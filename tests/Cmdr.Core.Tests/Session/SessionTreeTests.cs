using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>The group tree: loading, saving, row and entry order, depth, and the settings that nested items inherit.</summary>
public class SessionTreeTests
{
    /// <summary>top [first, mid [m1, deep [d1]], last], with cwd and env on top and on mid.</summary>
    private static GroupSpec ThreeLevelTree() => new()
    {
        Name = "top",
        Cwd = "a",
        Env = new Dictionary<string, string?> { ["X"] = "1" },
        Items =
        {
            Sh("first", "true"),
            new GroupSpec
            {
                Name = "mid",
                Mode = GroupMode.Parallel,
                Cwd = "b",
                Env = new Dictionary<string, string?> { ["Y"] = "2", ["GONE"] = null },
                Items =
                {
                    Sh("m1", "true"),
                    new GroupSpec { Name = "deep", Mode = GroupMode.Sequential, Items = { Sh("d1", "true") } },
                },
            },
            Sh("last", "true"),
        },
    };

    [Fact]
    public void Load_ThreeLevelTree_KeepsMixedOrderParentsAndSettings()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        ConfigStore.Save(new CmdrConfig { Groups = { ThreeLevelTree() } }, path);

        using CmdrSession session = CmdrSession.Load(path);

        Assert.Empty(session.LoadErrors);
        GroupEntry top = Assert.Single(session.Groups);
        Assert.Null(top.Parent);
        Assert.Equal(new[] { "first", "mid", "last" }, top.Items.Select(i => i.Name));
        Assert.Equal(new[] { "first", "last" }, top.Commands.Select(c => c.Name));
        GroupEntry mid = Assert.Single(top.Groups);
        Assert.Same(top, mid.Parent);
        Assert.Equal("b", mid.Cwd);
        Assert.Equal("2", mid.Env!["Y"]);
        Assert.True(mid.Env.ContainsKey("GONE"));
        Assert.Null(mid.Env["GONE"]);
        Assert.Equal(new[] { "m1", "deep" }, mid.Items.Select(i => i.Name));
        GroupEntry deep = Assert.Single(mid.Groups);
        Assert.Same(mid, deep.Parent);
        Assert.Equal(GroupMode.Sequential, deep.Mode);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void BuildConfig_And_Save_WriteTheTreeWithMixedOrder()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        ConfigStore.Save(new CmdrConfig { Groups = { ThreeLevelTree() } }, path);

        using (CmdrSession session = CmdrSession.Load(path))
        {
            GroupSpec built = Assert.Single(session.BuildConfig().Groups);
            Assert.Equal(new[] { "first", "mid", "last" }, built.Items.Select(i => i.Name));
            Assert.IsType<CommandSpec>(built.Items[0]);
            var mid = Assert.IsType<GroupSpec>(built.Items[1]);
            Assert.Equal("b", mid.Cwd);
            Assert.Equal(new[] { "m1", "deep" }, mid.Items.Select(i => i.Name));
            Assert.Equal(GroupMode.Sequential, Assert.IsType<GroupSpec>(mid.Items[1]).Mode);

            session.Save();
        }

        CmdrConfig reloaded = ConfigStore.Load(path);
        GroupSpec top = Assert.Single(reloaded.Groups);
        Assert.Equal("a", top.Cwd);
        Assert.Equal("1", top.Env!["X"]);
        Assert.Equal(new[] { "first", "mid", "last" }, top.Items.Select(i => i.Name));
        var mid2 = Assert.IsType<GroupSpec>(top.Items[1]);
        Assert.Equal("2", mid2.Env!["Y"]);
        Assert.Equal(new[] { "m1", "deep" }, mid2.Items.Select(i => i.Name));
    }

    [Fact]
    public void BuildRows_IsDepthFirstInFileOrder()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        ConfigStore.Save(new CmdrConfig { Groups = { ThreeLevelTree() } }, path);

        using CmdrSession session = CmdrSession.Load(path);

        string[] labels = session.BuildRows()
            .Select(row => row switch
            {
                GroupRow group => "G:" + group.Group.Name,
                CommandRow command => "C:" + command.Entry.Name,
                _ => "?",
            })
            .ToArray();
        Assert.Equal(new[] { "G:top", "C:first", "G:mid", "C:m1", "G:deep", "C:d1", "C:last" }, labels);
    }

    [Fact]
    public void AllEntries_AndAllGroups_AreDepthFirst_AndDepthIsNestingLevel()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        ConfigStore.Save(new CmdrConfig { Groups = { ThreeLevelTree() } }, path);

        using CmdrSession session = CmdrSession.Load(path);

        Assert.Equal(new[] { "first", "m1", "d1", "last" }, session.AllEntries.Select(e => e.Name));
        Assert.Equal(new[] { "top", "mid", "deep" }, session.AllGroups.Select(g => g.Name));
        GroupEntry deep = session.AllGroups[2];
        Assert.Equal(new[] { 0, 1, 2 }, session.AllGroups.Select(g => g.Depth));
        CommandEntry d1 = session.AllEntries[2];
        Assert.Same(deep, d1.Group);
        Assert.Same(deep, d1.Parent);
    }

    [Fact]
    public void Launch_NestedCommand_InheritsGroupCwdEnv_AndDefaultLogPath()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));
        var command = new CommandSpec { Name = "show", Program = "sh", Args = { "-c", "echo \"[$BAR]\"; pwd" } };
        command.Log.Enabled = true;
        var inner = new GroupSpec { Name = "inner", Env = new Dictionary<string, string?> { ["BAR"] = "${FOO}b" }, Items = { command } };
        var outer = new GroupSpec { Name = "outer", Cwd = "sub", Env = new Dictionary<string, string?> { ["FOO"] = "a" }, Items = { inner } };
        string path = dir.File("cmdr.json");
        ConfigStore.Save(new CmdrConfig { Groups = { outer } }, path);

        CmdrSession session = CmdrSession.Load(path);
        try
        {
            CommandEntry entry = session.AllEntries.Single();
            Assert.Null(session.Activate(new[] { entry }));
            WaitForExited(entry.Process);

            string text = Text(entry.Process);
            Assert.Contains("[ab]", text, StringComparison.Ordinal);
            Assert.Contains("/sub", text, StringComparison.Ordinal);

            string logText = File.ReadAllText(dir.File(Path.Combine("logs", "outer", "inner", "show.log")));
            Assert.Contains("[ab]", logText, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(session);
        }
    }
}
