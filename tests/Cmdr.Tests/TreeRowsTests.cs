using Cmdr.Core.Session;
using Cmdr.Tui;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

public sealed class TreeRowsTests : IDisposable
{
    private readonly TempSession _temp = TempSession.Sample();

    public void Dispose() => _temp.Dispose();

    private CmdrSession Session => _temp.Session;

    private GroupEntry Group(string name) => Session.AllGroups.Single(g => g.Name == name);

    private CommandEntry Command(string name) => Session.AllEntries.Single(c => c.Name == name);

    /// <summary>The names of the group and command rows shown, in order. File rows are left out; see the file row tests.</summary>
    private static IReadOnlyList<string> Names(IEnumerable<ListRow> rows) =>
        rows.Where(row => row is not FileRow).Select(row => TreeRows.ItemOf(row).Name).ToList();

    [Fact]
    public void Sample_rows_are_depth_first_in_file_order()
    {
        Assert.Equal(new[] { "top", "first", "mid", "m1", "deep", "d1", "last", "other", "oc" }, Names(Session.BuildRows()));
    }

    [Fact]
    public void The_file_row_comes_first_and_the_items_of_its_file_follow_it()
    {
        IReadOnlyList<ListRow> rows = Session.BuildRows();

        Assert.Single(rows.OfType<FileRow>());
        Assert.Same(Session.MainSource, Assert.IsType<FileRow>(rows[0]).Source);
    }

    [Fact]
    public void Nothing_collapsed_shows_every_row()
    {
        IReadOnlyList<ListRow> all = Session.BuildRows();

        Assert.Equal(all.Count, TreeRows.Visible(all, new HashSet<object>()).Count);
        Assert.Equal(Names(all), Names(TreeRows.Visible(all, new HashSet<object>())));
    }

    [Fact]
    public void Collapsing_a_file_hides_every_row_below_its_file_row()
    {
        var collapsed = new HashSet<object> { Session.MainSource };

        IReadOnlyList<ListRow> shown = TreeRows.Visible(Session.BuildRows(), collapsed);

        Assert.Single(shown);
        Assert.Same(Session.MainSource, Assert.IsType<FileRow>(shown[0]).Source);
    }

    [Fact]
    public void Collapsing_a_nested_group_hides_everything_below_it_but_keeps_its_own_header()
    {
        var collapsed = new HashSet<object> { Group("mid") };

        IReadOnlyList<ListRow> shown = TreeRows.Visible(Session.BuildRows(), collapsed);

        Assert.Equal(new[] { "top", "first", "mid", "last", "other", "oc" }, Names(shown));
    }

    [Fact]
    public void Collapsing_a_deeper_group_hides_only_what_is_below_it()
    {
        var collapsed = new HashSet<object> { Group("deep") };

        IReadOnlyList<ListRow> shown = TreeRows.Visible(Session.BuildRows(), collapsed);

        Assert.Equal(new[] { "top", "first", "mid", "m1", "deep", "last", "other", "oc" }, Names(shown));
    }

    [Fact]
    public void Collapsing_a_top_group_hides_all_of_its_descendants_at_any_depth()
    {
        var collapsed = new HashSet<object> { Group("top") };

        IReadOnlyList<ListRow> shown = TreeRows.Visible(Session.BuildRows(), collapsed);

        Assert.Equal(new[] { "top", "other", "oc" }, Names(shown));
    }

    [Fact]
    public void Collapsing_a_group_and_an_ancestor_gives_the_same_rows_as_the_ancestor_alone()
    {
        var both = new HashSet<object> { Group("top"), Group("mid") };
        var top = new HashSet<object> { Group("top") };

        Assert.Equal(Names(TreeRows.Visible(Session.BuildRows(), top)), Names(TreeRows.Visible(Session.BuildRows(), both)));
    }

    [Fact]
    public void Hidden_command_is_focused_on_the_outermost_collapsed_ancestor_header()
    {
        var collapsed = new HashSet<object> { Group("mid") };

        Assert.Same(Group("mid"), TreeRows.FocusHeaderFor(Command("d1"), collapsed));
        Assert.Same(Group("mid"), TreeRows.FocusHeaderFor(Command("m1"), collapsed));
        Assert.Same(Group("mid"), TreeRows.FocusHeaderFor(Group("deep"), collapsed));
    }

    [Fact]
    public void Outermost_collapsed_ancestor_wins_when_two_ancestors_are_collapsed()
    {
        var collapsed = new HashSet<object> { Group("top"), Group("mid") };

        Assert.Same(Group("top"), TreeRows.FocusHeaderFor(Command("d1"), collapsed));
        Assert.Same(Group("top"), TreeRows.FocusHeaderFor(Group("mid"), collapsed));
    }

    [Fact]
    public void A_collapsed_file_is_the_outermost_header_of_everything_in_it()
    {
        var collapsed = new HashSet<object> { Session.MainSource, Group("mid") };

        Assert.Same(Session.MainSource, TreeRows.FocusHeaderFor(Command("d1"), collapsed));
        Assert.Same(Session.MainSource, TreeRows.FocusHeaderFor(Group("mid"), collapsed));
        Assert.Same(Session.MainSource, TreeRows.FocusHeaderFor(Group("top"), collapsed));
    }

    [Fact]
    public void A_collapsed_header_is_not_hidden_by_itself()
    {
        var collapsed = new HashSet<object> { Group("deep") };

        Assert.Null(TreeRows.FocusHeaderFor(Group("deep"), collapsed));
        Assert.False(TreeRows.IsHidden(Group("deep"), collapsed));
        Assert.Same(Group("deep"), TreeRows.FocusHeaderFor(Command("d1"), collapsed));
    }

    [Fact]
    public void A_shown_item_needs_no_focus_header()
    {
        var collapsed = new HashSet<object> { Group("other") };

        Assert.Null(TreeRows.FocusHeaderFor(Command("d1"), collapsed));
        Assert.Null(TreeRows.FocusHeaderFor(Group("top"), collapsed));
        Assert.Null(TreeRows.FocusHeaderFor(Command("oc"), new HashSet<object>()));
    }

    [Fact]
    public void A_file_row_is_keyed_by_its_file_and_searched_by_its_display_path()
    {
        var file = new FileRow(Session.MainSource);

        Assert.Same(Session.MainSource, TreeRows.KeyOf(file));
        Assert.Equal(Session.MainSource.DisplayPath, TreeRows.SearchTextOf(file));
    }

    [Fact]
    public void The_list_depth_of_a_top_level_group_is_one_below_its_file_row()
    {
        Assert.Equal(1, TreeRows.ListDepth(Group("top")));
        Assert.Equal(2, TreeRows.ListDepth(Group("mid")));
        Assert.Equal(3, TreeRows.ListDepth(Group("deep")));
    }

    [Fact]
    public void Default_parent_of_a_focused_group_is_its_parent()
    {
        Assert.Same(Group("mid"), TreeRows.DefaultParentFor(new GroupRow(Group("deep"))));
        Assert.Null(TreeRows.DefaultParentFor(new GroupRow(Group("top"))));
    }

    [Fact]
    public void Default_parent_of_a_focused_command_is_the_parent_of_its_group()
    {
        Assert.Same(Group("mid"), TreeRows.DefaultParentFor(new CommandRow(Command("d1"))));
        Assert.Null(TreeRows.DefaultParentFor(new CommandRow(Command("first"))));
    }

    [Fact]
    public void Default_parent_with_nothing_focused_is_top_level()
    {
        Assert.Null(TreeRows.DefaultParentFor(null));
    }

    [Fact]
    public void Default_parent_of_a_focused_file_row_is_top_level()
    {
        Assert.Null(TreeRows.DefaultParentFor(new FileRow(Session.MainSource)));
    }
}
