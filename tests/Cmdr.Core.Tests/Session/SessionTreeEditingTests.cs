using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>Editing the group tree in edit mode: nested add, reparent, removal, moves, and their refusals.</summary>
public class SessionTreeEditingTests
{
    [Fact]
    public void AddGroup_Nested_AppendsToParent_AndSavesTheTree()
    {
        using var dir = new TempDir();
        using (CmdrSession session = OpenEmpty(dir, SessionMode.Edit))
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
            SessionMode.Edit,
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
        using CmdrSession session = OpenWith(dir, SessionMode.Edit, new GroupSpec
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
    public void RemoveGroup_IsRecursive()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, SessionMode.Edit, new GroupSpec
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
    public void MoveItem_AmongMixedSiblings_AndRefusesAtBoundaries()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(
            dir,
            SessionMode.Edit,
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
        using CmdrSession session = OpenWith(dir, SessionMode.Edit, new GroupSpec
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

    /// <summary>Adds groups g1 › g2 › … › g{levels} with AddGroup, each inside the previous one. Returns the deepest group.</summary>
    private static GroupEntry AddChain(CmdrSession session, int levels)
    {
        GroupEntry? parent = null;
        for (int level = 1; level <= levels; level++)
        {
            parent = session.AddGroup(parent, Settings($"g{level}"));
        }

        return parent!;
    }

    /// <summary>A group definition l1 › l2 › … › l{levels}, for loading into a session.</summary>
    private static GroupSpec SpecChain(int levels)
    {
        GroupSpec? inner = null;
        for (int level = levels; level >= 1; level--)
        {
            var group = new GroupSpec { Name = $"l{level}" };
            if (inner is not null)
            {
                group.Items.Add(inner);
            }

            inner = group;
        }

        return inner!;
    }

    [Fact]
    public void AddGroup_SixteenLevels_Allowed_SeventeenthRefused_NothingChanged()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenEmpty(dir, SessionMode.Edit);
        GroupEntry deepest = AddChain(session, CmdrConfig.MaxGroupDepth);
        Assert.Equal(CmdrConfig.MaxGroupDepth - 1, deepest.Depth);
        string[] namesBefore = session.AllGroups.Select(g => g.Name).ToArray();
        bool dirtyBefore = session.IsDirty;

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => session.AddGroup(deepest, Settings("too-deep")));

        Assert.Equal("Cannot add a group inside 'g16': groups can be nested at most 16 levels deep.", ex.Message);
        Assert.Equal(namesBefore, session.AllGroups.Select(g => g.Name));
        Assert.Empty(deepest.Items);
        Assert.Equal(dirtyBefore, session.IsDirty);
    }

    [Fact]
    public void Save_SixteenLevelSession_Succeeds_AndLoadsBackWithSixteenLevels()
    {
        using var dir = new TempDir();
        using (CmdrSession session = OpenEmpty(dir, SessionMode.Edit))
        {
            AddChain(session, CmdrConfig.MaxGroupDepth);

            session.Save();
        }

        CmdrConfig reloaded = ConfigStore.Load(dir.File("cmdr.json"));
        var names = new List<string>();
        GroupSpec? group = Assert.Single(reloaded.Groups);
        while (group is not null)
        {
            names.Add(group.Name);
            group = group.Items.OfType<GroupSpec>().SingleOrDefault();
        }

        Assert.Equal(Enumerable.Range(1, CmdrConfig.MaxGroupDepth).Select(l => $"g{l}"), names);
    }

    [Fact]
    public void UpdateGroup_MoveTwoLevelSubtreeUnderLevelFifteen_Refused_NothingChanged()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(
            dir,
            SessionMode.Edit,
            SpecChain(CmdrConfig.MaxGroupDepth - 1),
            new GroupSpec { Name = "sub", Items = { new GroupSpec { Name = "subchild" } } });
        GroupEntry sub = session.Groups[1];
        GroupEntry level15 = session.AllGroups.Single(g => g.Name == "l15");
        GroupEntry level14 = session.AllGroups.Single(g => g.Name == "l14");

        Assert.Throws<InvalidOperationException>(() => session.UpdateGroup(sub, Settings("renamed"), level15));

        Assert.Equal("sub", sub.Name);
        Assert.Null(sub.Parent);
        Assert.Equal(new[] { "l1", "sub" }, session.Groups.Select(g => g.Name));
        Assert.Empty(level15.Groups);
        Assert.False(session.IsDirty);

        session.UpdateGroup(sub, Settings("moved"), level14);

        Assert.Same(level14, sub.Parent);
        Assert.Equal(new[] { "l15", "moved" }, level14.Groups.Select(g => g.Name));
        Assert.Equal("moved", sub.Name);
        Assert.Equal(new[] { "l1" }, session.Groups.Select(g => g.Name));
        Assert.True(session.IsDirty);

        session.UpdateGroup(sub, Settings("top"), null);

        Assert.Null(sub.Parent);
        Assert.Equal("top", sub.Name);
    }
}
