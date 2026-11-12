using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

public class SessionEditingTests
{
    [Fact]
    public void Load_MissingFile_YieldsEmptyCleanSession()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenEmpty(dir);

        Assert.Empty(session.Groups);
        Assert.Empty(session.LoadErrors);
        Assert.False(session.IsDirty);
        Assert.Equal(dir.File("cmdr.json"), session.ConfigPath);
        Assert.Equal(dir.Path, session.ConfigDir);
        Assert.Equal(10000, session.BufferLines);
    }

    [Fact]
    public void Load_ThenBuildConfig_RoundTripsAllFields()
    {
        using var dir = new TempDir();
        var web = new CommandSpec
        {
            Name = "web",
            Description = "multi\nline",
            Program = "pnpm",
            Args = new List<string> { "start" },
            Cwd = "../web",
            Env = new Dictionary<string, string?> { ["FORCE_COLOR"] = "1", ["GONE"] = null },
            Stop = new StopSpec { Method = StopMethod.Input, Input = "q\n" },
            Log = new LogSpec { Enabled = true, Path = "logs/web.log" },
        };
        var config = new CmdrConfig
        {
            BufferLines = 500,
            Groups = new List<GroupSpec>
            {
                new() { Name = "build", Description = "desc", Mode = GroupMode.Sequential, Items = new List<ItemSpec> { Sh("restore", "true") } },
                new() { Name = "servers", Items = new List<ItemSpec> { web } },
            },
        };
        string path = dir.File("cmdr.json");
        ConfigStore.Save(config, path);

        using CmdrSession session = CmdrSession.Load(path);
        CmdrConfig built = session.BuildConfig();

        Assert.Equal(500, built.BufferLines);
        Assert.Equal(2, built.Groups.Count);
        Assert.Equal("build", built.Groups[0].Name);
        Assert.Equal("desc", built.Groups[0].Description);
        Assert.Equal(GroupMode.Sequential, built.Groups[0].Mode);
        Assert.Equal(GroupMode.Parallel, built.Groups[1].Mode);

        var loaded = (CommandSpec)built.Groups[1].Items.Single();
        Assert.Equal("multi\nline", loaded.Description);
        Assert.Equal("pnpm", loaded.Program);
        Assert.Equal(new[] { "start" }, loaded.Args);
        Assert.Equal("../web", loaded.Cwd);
        Assert.Equal("1", loaded.Env["FORCE_COLOR"]);
        Assert.True(loaded.Env.ContainsKey("GONE"));
        Assert.Null(loaded.Env["GONE"]);
        Assert.Equal(StopMethod.Input, loaded.Stop.Method);
        Assert.Equal("q\n", loaded.Stop.Input);
        Assert.True(loaded.Log.Enabled);
        Assert.Equal("logs/web.log", loaded.Log.Path);

        Assert.Empty(session.LoadErrors);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Load_InvalidBufferLines_ReportsLoadErrors_AndStillAcceptsCommands()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        ConfigStore.Save(new CmdrConfig { BufferLines = 0, Groups = new List<GroupSpec> { Group("g", GroupMode.Parallel) } }, path);

        using CmdrSession session = CmdrSession.Load(path);

        Assert.Contains(session.LoadErrors, e => e.Contains("bufferLines", StringComparison.Ordinal));
        CommandEntry entry = session.AddCommand(session.Groups[0], Sh("a", "true"));
        Assert.Equal(0, entry.Process.Snapshot.LeftoverCount);
    }

    [Fact]
    public void Load_DuplicateNames_ReportsLoadErrors()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        ConfigStore.Save(new CmdrConfig
        {
            Groups = new List<GroupSpec>
            {
                Group("g", GroupMode.Parallel, Sh("a", "true"), Sh("A", "true")),
                Group("G", GroupMode.Parallel),
            },
        }, path);

        using CmdrSession session = CmdrSession.Load(path);

        Assert.Contains(session.LoadErrors, e => e.Contains("duplicate name 'a'", StringComparison.Ordinal));
        Assert.Contains(session.LoadErrors, e => e.Contains("Duplicate group name 'g'", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_MalformedJson_ThrowsConfigException()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, "{ not json");

        Assert.Throws<ConfigException>(() => CmdrSession.Load(path));
    }

    [Fact]
    public void AddGroup_AppendsMarksDirty_AndRaisesStructureChanged()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenEmpty(dir);
        int changes = 0;
        session.StructureChanged += () => changes++;

        GroupEntry group = session.AddGroup("build", "desc", GroupMode.Sequential);

        Assert.Same(group, Assert.Single(session.Groups));
        Assert.Equal("build", group.Name);
        Assert.Equal("desc", group.Description);
        Assert.Equal(GroupMode.Sequential, group.Mode);
        Assert.True(session.IsDirty);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void UpdateGroup_ChangesSettings()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, Group("build", GroupMode.Parallel));
        GroupEntry group = session.Groups[0];

        session.UpdateGroup(group, "release", null, GroupMode.Sequential);

        Assert.Equal("release", group.Name);
        Assert.Null(group.Description);
        Assert.Equal(GroupMode.Sequential, group.Mode);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void AddCommand_CopiesSpec()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel));
        CommandSpec spec = Sh("a", "true");

        CommandEntry entry = session.AddCommand(session.Groups[0], spec);
        spec.Args.Add("mutated");
        spec.Name = "renamed";

        Assert.NotSame(spec, entry.Spec);
        Assert.Equal("a", entry.Spec.Name);
        Assert.Equal(new[] { "-c", "true" }, entry.Spec.Args);
        Assert.Same(entry, Assert.Single(session.Groups[0].Commands));
    }

    [Fact]
    public void UpdateCommand_ReplacesSpecWithCopy()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("a", "true")));
        CommandEntry entry = session.Groups[0].Commands[0];
        CommandSpec spec = Sh("renamed", "false");

        session.UpdateCommand(entry, spec, entry.Group);
        spec.Args.Add("mutated");

        Assert.Equal("renamed", entry.Spec.Name);
        Assert.Equal(new[] { "-c", "false" }, entry.Spec.Args);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void UpdateCommand_ToOtherGroup_MovesToEndOfTarget()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(
            dir,
            Group("g1", GroupMode.Parallel, Sh("a", "true"), Sh("b", "true")),
            Group("g2", GroupMode.Parallel, Sh("x", "true")));
        GroupEntry g1 = session.Groups[0];
        GroupEntry g2 = session.Groups[1];
        CommandEntry a = g1.Commands[0];

        session.UpdateCommand(a, Sh("a2", "true"), g2);

        Assert.Same(g2, a.Group);
        Assert.Equal(new[] { "b" }, g1.Commands.Select(c => c.Spec.Name));
        Assert.Equal(new[] { "x", "a2" }, g2.Commands.Select(c => c.Spec.Name));
    }

    [Fact]
    public void MoveCommand_SwapsWithinGroup_AndRefusesAtBoundaries()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("a", "true"), Sh("b", "true"), Sh("c", "true")));
        IReadOnlyList<CommandEntry> commands = session.Groups[0].Commands;

        Assert.False(session.MoveCommand(commands[0], -1));
        Assert.False(session.MoveCommand(commands[2], 1));
        Assert.False(session.IsDirty);

        Assert.True(session.MoveCommand(commands[0], 1));
        Assert.Equal(new[] { "b", "a", "c" }, commands.Select(c => c.Spec.Name));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void RemoveCommand_RemovesFromGroup()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("a", "true"), Sh("b", "true")));
        CommandEntry a = session.Groups[0].Commands[0];

        session.RemoveCommand(a);

        Assert.Equal(new[] { "b" }, session.Groups[0].Commands.Select(c => c.Spec.Name));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void RemoveCommand_RefusedWhileRunning()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sleeper("loop")));
        try
        {
            CommandEntry loop = session.Groups[0].Commands[0];
            session.Activate(new[] { loop });
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running, "loop running", loop.Process);

            Assert.Throws<InvalidOperationException>(() => session.RemoveCommand(loop));
            Assert.Single(session.Groups[0].Commands);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void RemoveGroup_RefusedWhileCommandRunning()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sleeper("loop")));
        try
        {
            GroupEntry group = session.Groups[0];
            session.Activate(new[] { group.Commands[0] });
            WaitUntil(() => group.Commands[0].Process.Snapshot.State == ProcessState.Running, "loop running", group.Commands[0].Process);

            Assert.Throws<InvalidOperationException>(() => session.RemoveGroup(group));
            Assert.Single(session.Groups);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void RemoveGroup_WhenIdle_RemovesGroupAndCommands()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, Group("g1", GroupMode.Parallel, Sh("a", "true")), Group("g2", GroupMode.Parallel));

        session.RemoveGroup(session.Groups[0]);

        Assert.Equal(new[] { "g2" }, session.Groups.Select(g => g.Name));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void UpdateGroup_ModeChangeRefusedWhileSequenceRunning()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Sequential, Sleeper("loop")));
        try
        {
            GroupEntry group = session.Groups[0];
            Assert.Null(session.Activate(group.Commands));
            WaitUntil(() => group.Commands[0].Process.Snapshot.State == ProcessState.Running, "loop running", group.Commands[0].Process);

            Assert.Throws<InvalidOperationException>(() => session.UpdateGroup(group, "g", null, GroupMode.Parallel));
            session.UpdateGroup(group, "renamed", null, GroupMode.Sequential);
            Assert.Equal("renamed", group.Name);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void UpdateCommand_QueuedCommandToOtherGroup_Throws()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(
            dir,
            Group("seq", GroupMode.Sequential, Sleeper("loop"), Sh("next", "true")),
            Group("other", GroupMode.Parallel));
        try
        {
            GroupEntry seq = session.Groups[0];
            CommandEntry next = seq.Commands[1];
            Assert.Null(session.Activate(seq.Commands));
            WaitUntil(() => next.IsQueued, "next queued");

            Assert.Throws<InvalidOperationException>(() => session.UpdateCommand(next, next.Spec, session.Groups[1]));
            Assert.Same(seq, next.Group);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void Save_WritesFile_ClearsDirty_AndRaisesStructureChanged()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenEmpty(dir);
        session.AddGroup("build", null, GroupMode.Sequential);
        session.AddCommand(session.Groups[0], Sh("restore", "true"));
        int changes = 0;
        session.StructureChanged += () => changes++;

        session.Save();

        Assert.False(session.IsDirty);
        Assert.Equal(1, changes);
        CmdrConfig reloaded = ConfigStore.Load(dir.File("cmdr.json"));
        Assert.Equal("build", reloaded.Groups.Single().Name);
        Assert.Equal(GroupMode.Sequential, reloaded.Groups.Single().Mode);
        Assert.Equal("restore", reloaded.Groups.Single().Items.Single().Name);
    }

    [Fact]
    public void Save_WithValidationErrors_ThrowsAndStaysDirty()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenEmpty(dir);
        session.AddGroup("build", null, GroupMode.Parallel);
        session.AddCommand(session.Groups[0], new CommandSpec { Name = "bad", Program = "" });

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => session.Save());

        Assert.Contains("program must not be blank", ex.Message, StringComparison.Ordinal);
        Assert.True(session.IsDirty);
        Assert.False(File.Exists(dir.File("cmdr.json")));
    }

    [Fact]
    public void BuildRows_ListsEachGroupHeaderFollowedByItsCommands()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(
            dir,
            Group("g1", GroupMode.Parallel, Sh("a", "true"), Sh("b", "true")),
            Group("g2", GroupMode.Parallel, Sh("c", "true")));

        IReadOnlyList<ListRow> rows = session.BuildRows();

        Assert.Equal(5, rows.Count);
        Assert.IsType<GroupRow>(rows[0]);
        Assert.Equal("g1", ((GroupRow)rows[0]).Group.Name);
        Assert.Equal("a", ((CommandRow)rows[1]).Entry.Spec.Name);
        Assert.Equal("b", ((CommandRow)rows[2]).Entry.Spec.Name);
        Assert.Equal("g2", ((GroupRow)rows[3]).Group.Name);
        Assert.Equal("c", ((CommandRow)rows[4]).Entry.Spec.Name);
    }

    [Fact]
    public void Activate_TargetsFromTwoGroups_ThrowsArgumentException()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, Group("g1", GroupMode.Parallel, Sh("a", "true")), Group("g2", GroupMode.Parallel, Sh("b", "true")));
        CommandEntry a = session.Groups[0].Commands[0];
        CommandEntry b = session.Groups[1].Commands[0];

        Assert.Throws<ArgumentException>(() => session.Activate(new[] { a, b }));
        Assert.Throws<ArgumentException>(() => session.Stop(new[] { a, b }));
        Assert.Throws<ArgumentException>(() => session.ForceKill(new[] { a, b }));
    }

    [Fact]
    public async Task EmptyTargets_AreNoOps()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("a", "true")));

        Assert.Null(session.Activate(Array.Empty<CommandEntry>()));
        Assert.Null(session.Stop(Array.Empty<CommandEntry>()));
        session.ForceKill(Array.Empty<CommandEntry>());
        Assert.Null(await session.RestartAsync(Array.Empty<CommandEntry>()));
        Assert.False(session.IsDirty);
    }
}
