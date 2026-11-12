using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

public sealed class GroupTreeTests : IDisposable
{
    private readonly TempSession _temp = TempSession.Sample();

    public void Dispose() => _temp.Dispose();

    private CmdrSession Session => _temp.Session;

    private GroupEntry Group(string name) => Session.AllGroups.Single(g => g.Name == name);

    private CommandEntry Command(string name) => Session.AllEntries.Single(c => c.Name == name);

    [Fact]
    public void Path_text_joins_the_names_from_the_outermost_group()
    {
        Assert.Equal("top / mid / deep / d1", GroupTree.PathText(Command("d1"), " / "));
        Assert.Equal("top / mid / deep", GroupTree.PathText(Group("deep"), " / "));
        Assert.Equal("top", GroupTree.PathText(Group("top"), " / "));
        Assert.Equal("top › mid", GroupTree.PathText(Group("mid"), " › "));
    }

    [Fact]
    public void Chain_is_the_group_and_its_ancestors_outermost_first()
    {
        Assert.Equal(new[] { "top", "mid", "deep" }, GroupTree.ChainTo(Group("deep")).Select(g => g.Name));
        Assert.Empty(GroupTree.ChainTo(null));
    }

    [Fact]
    public void Spec_chain_carries_the_names_and_working_directory_settings()
    {
        IReadOnlyList<GroupSpec> chain = GroupTree.SpecChain(Group("deep"));

        Assert.Equal(new[] { "top", "mid", "deep" }, chain.Select(spec => spec.Name));
        Assert.Equal(new[] { "src", "sub", null }, chain.Select(spec => spec.Cwd));
    }

    [Fact]
    public void Self_or_descendant_covers_the_group_itself_and_what_is_below_it()
    {
        Assert.True(GroupTree.IsSelfOrDescendant(Group("deep"), Group("mid")));
        Assert.True(GroupTree.IsSelfOrDescendant(Group("deep"), Group("deep")));
        Assert.True(GroupTree.IsSelfOrDescendant(Group("deep"), Group("top")));
        Assert.False(GroupTree.IsSelfOrDescendant(Group("mid"), Group("deep")));
        Assert.False(GroupTree.IsSelfOrDescendant(Group("other"), Group("top")));
    }

    [Fact]
    public void Descendant_group_count_counts_every_group_below_at_any_depth()
    {
        Assert.Equal(2, GroupTree.DescendantGroupCount(Group("top")));
        Assert.Equal(1, GroupTree.DescendantGroupCount(Group("mid")));
        Assert.Equal(0, GroupTree.DescendantGroupCount(Group("other")));
    }

    [Fact]
    public void Delete_question_counts_subgroups_and_commands()
    {
        Assert.Equal("Delete group 'top' with 2 subgroup(s) and 4 command(s)?", GroupTree.DeleteQuestion(Group("top")));
    }

    [Fact]
    public void Delete_question_leaves_out_the_subgroup_part_when_there_are_none()
    {
        Assert.Equal("Delete group 'other' with 1 command(s)?", GroupTree.DeleteQuestion(Group("other")));
        Assert.Equal("Delete group 'deep' with 1 command(s)?", GroupTree.DeleteQuestion(Group("deep")));
    }

    [Fact]
    public void Details_title_is_the_full_path_of_a_command_and_of_a_group()
    {
        Assert.Equal("top / mid / deep / d1", DetailsText.Title(new CommandRow(Command("d1"))));
        Assert.Equal("top / mid", DetailsText.Title(new GroupRow(Group("mid"))));
        Assert.Equal(string.Empty, DetailsText.Title(null));
    }

    [Fact]
    public void Details_title_of_a_top_level_command_is_its_group_and_name()
    {
        Assert.Equal("top / first", DetailsText.Title(new CommandRow(Command("first"))));
    }
}
