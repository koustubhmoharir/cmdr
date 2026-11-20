using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>The group <c>expanded</c> setting: read at load, kept in the session, written only when false.</summary>
public class SessionExpandedTests
{
    private static GroupSettings Settings(string name, bool expanded) =>
        new(name, null, GroupMode.Parallel, null, null, expanded);

    [Fact]
    public void Loaded_group_with_expanded_false_is_not_expanded_and_nested_groups_too()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(
            path,
            """{ "groups": [ { "name": "outer", "expanded": false, "items": [ { "name": "inner", "expanded": false, "items": [] } ] }, { "name": "open" } ] }""");

        using CmdrSession session = CmdrSession.Load(path, SessionMode.Run);

        Assert.False(session.Groups[0].Expanded);
        Assert.False(session.Groups[0].Groups[0].Expanded);
        Assert.True(session.Groups[1].Expanded);
    }

    [Fact]
    public void BuildConfig_writes_false_only_and_drops_true()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(
            path,
            """{ "groups": [ { "name": "a", "expanded": true, "items": [ { "name": "inner", "expanded": false, "items": [] } ] }, { "name": "b", "expanded": false } ] }""");

        using CmdrSession session = CmdrSession.Load(path, SessionMode.Run);
        CmdrConfig config = session.BuildConfig();

        Assert.Null(config.Groups[0].Expanded);
        Assert.False(config.Groups[1].Expanded);
        Assert.False(Assert.IsType<GroupSpec>(config.Groups[0].Items[0]).Expanded);
    }

    [Fact]
    public void Save_does_not_write_expanded_true()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "groups": [ { "name": "a", "expanded": true, "items": [] } ] }""");

        using (CmdrSession session = CmdrSession.Load(path, SessionMode.Edit))
        {
            session.UpdateGroup(session.Groups[0], Settings("a", expanded: true), parent: null);
            session.Save();
        }

        Assert.DoesNotContain("expanded", File.ReadAllText(path));
    }

    [Fact]
    public void AddGroup_keeps_the_expanded_setting()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenEmpty(dir, SessionMode.Edit);

        GroupEntry group = session.AddGroup(null, Settings("g", expanded: false));

        Assert.False(group.Expanded);
        Assert.False(session.BuildConfig().Groups[0].Expanded);
    }

    [Fact]
    public void UpdateGroup_changes_the_setting_and_marks_the_session_dirty()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, SessionMode.Edit, Group("g", GroupMode.Parallel));
        GroupEntry group = session.Groups[0];
        Assert.True(group.Expanded);
        Assert.False(session.IsDirty);

        session.UpdateGroup(group, Settings("g", expanded: false), parent: null);

        Assert.False(group.Expanded);
        Assert.True(session.IsDirty);
        Assert.False(session.BuildConfig().Groups[0].Expanded);

        session.UpdateGroup(group, Settings("g", expanded: true), parent: null);

        Assert.True(group.Expanded);
        Assert.Null(session.BuildConfig().Groups[0].Expanded);
    }

    [Fact]
    public void Loading_does_not_mark_the_session_dirty()
    {
        using var dir = new TempDir();
        string path = dir.File("cmdr.json");
        File.WriteAllText(path, """{ "groups": [ { "name": "a", "expanded": false, "items": [] } ] }""");

        using CmdrSession session = CmdrSession.Load(path, SessionMode.Run);

        Assert.False(session.Groups[0].Expanded);
        Assert.False(session.IsDirty);
    }
}
