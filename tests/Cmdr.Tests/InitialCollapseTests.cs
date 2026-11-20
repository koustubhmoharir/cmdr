using Cmdr.Core.Session;
using Cmdr.Tui;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

/// <summary>The groups that start collapsed on load: those with <c>expanded: false</c>, nested or in a base file.</summary>
public sealed class InitialCollapseTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("cmdr-initial-collapse-").FullName;

    public void Dispose()
    {
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
    /// The opened file has top group "top" (expanded false) containing "mid" (expanded true) with "m1" and a nested "deep"
    /// (expanded false), and a top group "open". The base file has "shared" (expanded false) with "tool" and "sub" (expanded
    /// false, nested) with "inner".
    /// </summary>
    private CmdrSession OpenSample()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "shared"));
        File.WriteAllText(
            Path.Combine(_dir, "shared", "base.json"),
            """
            { "groups": [
              { "name": "shared", "expanded": false, "items": [
                { "name": "tool", "program": "tool" },
                { "name": "sub", "expanded": false, "items": [ { "name": "inner", "program": "inner" } ] }
              ] }
            ] }
            """);
        File.WriteAllText(
            Path.Combine(_dir, "cmdr.json"),
            """
            { "base": "shared/base.json", "groups": [
              { "name": "top", "expanded": false, "items": [
                { "name": "mid", "expanded": true, "items": [
                  { "name": "m1", "program": "p" },
                  { "name": "deep", "expanded": false, "items": [ { "name": "d1", "program": "p" } ] }
                ] }
              ] },
              { "name": "open", "items": [ { "name": "o1", "program": "p" } ] }
            ] }
            """);
        return CmdrSession.Load(Path.Combine(_dir, "cmdr.json"), SessionMode.Run);
    }

    [Fact]
    public void Expanded_false_groups_at_any_depth_and_in_base_files_are_initially_collapsed_in_file_order()
    {
        using CmdrSession session = OpenSample();

        IReadOnlyList<GroupEntry> collapsed = TreeRows.InitiallyCollapsed(session.AllGroups);

        Assert.Equal(new[] { "shared", "sub", "top", "deep" }, collapsed.Select(g => g.Name));
    }

    [Fact]
    public void Nothing_is_initially_collapsed_when_every_group_is_expanded()
    {
        File.WriteAllText(Path.Combine(_dir, "cmdr.json"), """{ "groups": [ { "name": "a", "expanded": true, "items": [ { "name": "b", "items": [] } ] } ] }""");
        using CmdrSession session = CmdrSession.Load(Path.Combine(_dir, "cmdr.json"), SessionMode.Run);

        Assert.Empty(TreeRows.InitiallyCollapsed(session.AllGroups));
    }

    [Fact]
    public void Initial_rows_hide_what_is_below_a_collapsed_group_and_keep_top_level_headers_visible()
    {
        using CmdrSession session = OpenSample();
        var collapsed = new HashSet<GroupEntry>(TreeRows.InitiallyCollapsed(session.AllGroups), ReferenceEqualityComparer.Instance);

        IReadOnlyList<ListRow> shown = TreeRows.Visible(session.BuildRows(), collapsed);

        Assert.Equal(new[] { "shared", "top", "open", "o1" }, shown.Select(row => TreeRows.ItemOf(row).Name));
        Assert.Null(TreeRows.ItemOf(shown[0]).Parent);
    }
}
