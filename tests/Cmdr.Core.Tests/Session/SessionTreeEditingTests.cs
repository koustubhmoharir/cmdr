using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>Editing the group tree: nested add, reparent, removal, moves, and the refusals that keep runs consistent.</summary>
public class SessionTreeEditingTests
{
    private static GroupSpec Par(string name, params ItemSpec[] items) => new() { Name = name, Mode = GroupMode.Parallel, Items = items.ToList() };

    private static GroupSpec Seq(string name, params ItemSpec[] items) => new() { Name = name, Mode = GroupMode.Sequential, Items = items.ToList() };

    [Fact]
    public void AddGroup_Nested_AppendsToParent_AndSavesTheTree()
    {
        using var dir = new TempDir();
        using (CmdrSession session = OpenEmpty(dir))
        {
            GroupEntry outer = session.AddGroup(null, Settings("outer"));
            GroupEntry inner = session.AddGroup(outer, Settings("inner", GroupMode.Sequential));
            CommandEntry a = session.AddCommand(inner, Sh("a", "true"));

            Assert.Same(outer, inner.Parent);
            Assert.Same(inner, Assert.Single(outer.Groups));
            Assert.Equal(1, inner.Depth);
            Assert.Same(inner, a.Group);
            Assert.Equal(new[] { "outer", "inner" }, session.AllGroups.Select(g => g.Name));
            Assert.True(session.IsDirty);

            session.Save();
        }

        CmdrConfig reloaded = ConfigStore.Load(dir.File("cmdr.json"));
        GroupSpec outerSpec = Assert.Single(reloaded.Groups);
        var innerSpec = Assert.IsType<GroupSpec>(Assert.Single(outerSpec.Items));
        Assert.Equal(GroupMode.Sequential, innerSpec.Mode);
        Assert.Equal("a", Assert.Single(innerSpec.Items).Name);
    }

    [Fact]
    public void UpdateGroup_Reparent_MovesToEndOfNewParent()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(
            dir,
            new GroupSpec
            {
                Name = "g1",
                Items = { Sh("x", "true"), new GroupSpec { Name = "a", Items = { Sh("ax", "true") } } },
            },
            new GroupSpec { Name = "g2", Items = { Sh("y", "true") } });
        GroupEntry g1 = session.Groups[0];
        GroupEntry g2 = session.Groups[1];
        GroupEntry a = g1.Groups[0];

        session.UpdateGroup(a, new GroupSettings("a", null, a.Mode, null, null), g2);

        Assert.Same(g2, a.Parent);
        Assert.Equal(new[] { "y", "a" }, g2.Items.Select(i => i.Name));
        Assert.Empty(g1.Groups);
        Assert.Equal(new[] { "x" }, g1.Items.Select(i => i.Name));
        Assert.Equal(new[] { "g1", "g2" }, session.Groups.Select(g => g.Name));
        Assert.Equal(new[] { "ax" }, a.AllCommands.Select(c => c.Name));
    }

    [Fact]
    public void UpdateGroup_MoveIntoItselfOrDescendant_Throws()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, new GroupSpec
        {
            Name = "outer",
            Items = { new GroupSpec { Name = "mid", Items = { new GroupSpec { Name = "leaf" } } } },
        });
        GroupEntry outer = session.Groups[0];
        GroupEntry mid = outer.Groups[0];
        GroupEntry leaf = mid.Groups[0];

        Assert.Throws<InvalidOperationException>(() => session.UpdateGroup(outer, Settings("outer"), outer));
        Assert.Throws<InvalidOperationException>(() => session.UpdateGroup(outer, Settings("outer"), leaf));
        Assert.Same(mid, Assert.Single(outer.Groups));
        Assert.Null(outer.Parent);
    }

    [Fact]
    public void UpdateGroup_ModeOrMoveRefused_WhileSubtreeIsRunning_RenameStillAllowed()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Par("outer", Seq("inner", Sleeper("loop"))));
        try
        {
            GroupEntry outer = session.Groups[0];
            GroupEntry inner = outer.Groups[0];
            CommandEntry loop = inner.Commands[0];
            Assert.Null(session.Activate(inner));
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running, "loop running", loop.Process);

            Assert.Throws<InvalidOperationException>(() => session.UpdateGroup(outer, Settings("outer", GroupMode.Sequential), null));
            Assert.Throws<InvalidOperationException>(() => session.UpdateGroup(inner, Settings("inner", GroupMode.Sequential), null));
            session.UpdateGroup(outer, Settings("renamed"), null);

            Assert.Equal("renamed", outer.Name);
            Assert.Same(outer, session.Groups.Single());
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void RemoveGroup_IsRecursive()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, new GroupSpec
        {
            Name = "outer",
            Items = { new GroupSpec { Name = "inner", Items = { Sh("a", "true") } }, Sh("b", "true") },
        }, Group("keep", GroupMode.Parallel));

        session.RemoveGroup(session.Groups[0]);

        Assert.Equal(new[] { "keep" }, session.Groups.Select(g => g.Name));
        Assert.Empty(session.AllEntries);
        Assert.Equal(new[] { "keep" }, session.AllGroups.Select(g => g.Name));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void RemoveGroup_RefusedWhileDescendantCommandRuns()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, new GroupSpec
        {
            Name = "outer",
            Items = { new GroupSpec { Name = "inner", Items = { Sleeper("loop") } } },
        });
        try
        {
            GroupEntry outer = session.Groups[0];
            CommandEntry loop = outer.Groups[0].Commands[0];
            Assert.Null(session.Activate(new[] { loop }));
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running, "loop running", loop.Process);

            Assert.Throws<InvalidOperationException>(() => session.RemoveGroup(outer));
            Assert.Same(outer, Assert.Single(session.Groups));
            Assert.Single(session.AllEntries);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void MoveItem_AmongMixedSiblings_AndRefusesAtBoundaries()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(
            dir,
            new GroupSpec
            {
                Name = "g",
                Items =
                {
                    Sh("c1", "true"),
                    new GroupSpec { Name = "sub", Items = { Sh("s1", "true") } },
                    Sh("c2", "true"),
                },
            },
            Group("h", GroupMode.Parallel));
        GroupEntry g = session.Groups[0];
        CommandEntry c1 = (CommandEntry)g.Items[0];
        GroupEntry sub = (GroupEntry)g.Items[1];
        CommandEntry c2 = (CommandEntry)g.Items[2];

        Assert.False(session.MoveItem(c1, -1));
        Assert.False(session.MoveItem(c1, 0));
        Assert.False(session.MoveItem(c2, 1));
        Assert.Equal(new[] { "c1", "sub", "c2" }, g.Items.Select(i => i.Name));

        Assert.True(session.MoveItem(c1, 1));
        Assert.Equal(new[] { "sub", "c1", "c2" }, g.Items.Select(i => i.Name));
        Assert.False(session.MoveItem(sub, -1));
        Assert.Equal(new[] { "c1", "c2" }, g.Commands.Select(c => c.Name));
        Assert.True(session.MoveItem(c2, -1));
        Assert.Equal(new[] { "sub", "c2", "c1" }, g.Items.Select(i => i.Name));
        Assert.Equal(new[] { "c2", "c1" }, g.Commands.Select(c => c.Name));

        Assert.True(session.MoveItem(session.Groups[1], -1));
        Assert.Equal(new[] { "h", "g" }, session.Groups.Select(x => x.Name));
        Assert.False(session.MoveItem(session.Groups[0], -1));
    }

    [Fact]
    public void UpdateCommand_MovesIntoNestedGroup_AtEnd()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, new GroupSpec
        {
            Name = "g",
            Items = { Sh("c", "true"), new GroupSpec { Name = "inner", Items = { Sh("i1", "true") } } },
        });
        GroupEntry g = session.Groups[0];
        GroupEntry inner = g.Groups[0];
        CommandEntry c = g.Commands[0];

        session.UpdateCommand(c, Sh("c2", "true"), inner);

        Assert.Same(inner, c.Group);
        Assert.Equal(new[] { "i1", "c2" }, inner.Commands.Select(x => x.Name));
        Assert.Empty(g.Commands);
    }
}
