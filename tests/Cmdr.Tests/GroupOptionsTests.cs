using Cmdr.Core.Session;
using Cmdr.Tui;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

public sealed class GroupOptionsTests : IDisposable
{
    private readonly TempSession _temp = TempSession.Sample();

    public void Dispose() => _temp.Dispose();

    private CmdrSession Session => _temp.Session;

    private GroupEntry Group(string name) => Session.AllGroups.Single(g => g.Name == name);

    [Fact]
    public void Parent_choices_start_with_top_level_then_every_group_by_path_depth_first()
    {
        IReadOnlyList<GroupOption> options = GroupOptions.ParentChoices(Session.AllGroups, excluded: null);

        Assert.Equal(
            new[] { "(top level)", "top", "top › mid", "top › mid › deep", "other" },
            GroupOptions.Labels(options));
        Assert.Null(options[0].Group);
        Assert.Same(Group("top"), options[1].Group);
        Assert.Same(Group("deep"), options[3].Group);
    }

    [Fact]
    public void Editing_a_group_excludes_it_and_its_descendants()
    {
        IReadOnlyList<GroupOption> options = GroupOptions.ParentChoices(Session.AllGroups, excluded: Group("mid"));

        Assert.Equal(new[] { "(top level)", "top", "other" }, GroupOptions.Labels(options));
        Assert.DoesNotContain(options, option => ReferenceEquals(option.Group, Group("deep")));
    }

    [Fact]
    public void Editing_a_top_group_leaves_only_top_level_and_the_other_top_groups()
    {
        IReadOnlyList<GroupOption> options = GroupOptions.ParentChoices(Session.AllGroups, excluded: Group("top"));

        Assert.Equal(new[] { "(top level)", "other" }, GroupOptions.Labels(options));
    }

    [Fact]
    public void Editing_a_deep_group_still_offers_its_ancestors()
    {
        IReadOnlyList<GroupOption> options = GroupOptions.ParentChoices(Session.AllGroups, excluded: Group("deep"));

        Assert.Equal(new[] { "(top level)", "top", "top › mid", "other" }, GroupOptions.Labels(options));
    }

    [Fact]
    public void Target_choices_list_every_group_without_a_top_level_choice()
    {
        IReadOnlyList<GroupOption> options = GroupOptions.TargetChoices(Session.AllGroups);

        Assert.Equal(new[] { "top", "top › mid", "top › mid › deep", "other" }, GroupOptions.Labels(options));
        Assert.All(options, option => Assert.NotNull(option.Group));
    }

    [Fact]
    public void Target_choices_of_no_groups_are_empty()
    {
        Assert.Empty(GroupOptions.TargetChoices(Array.Empty<GroupEntry>()));
    }

    [Fact]
    public void Labels_are_made_unique_when_a_group_is_named_like_the_top_level_choice()
    {
        using var temp = TempSession.Open(new Cmdr.Core.Config.GroupSpec { Name = "(top level)" }, new Cmdr.Core.Config.GroupSpec { Name = "x" });

        IReadOnlyList<GroupOption> options = GroupOptions.ParentChoices(temp.Session.AllGroups, excluded: null);

        Assert.Equal(new[] { "(top level)", "(top level) (2)", "x" }, GroupOptions.Labels(options));
        Assert.Null(options[0].Group);
        Assert.Same(temp.Session.AllGroups[0], options[1].Group);
    }

    [Fact]
    public void Labels_are_made_unique_when_a_name_contains_the_separator()
    {
        using var temp = TempSession.Open(
            new Cmdr.Core.Config.GroupSpec { Name = "a › b" },
            new Cmdr.Core.Config.GroupSpec { Name = "a", Items = { new Cmdr.Core.Config.GroupSpec { Name = "b" } } });

        IReadOnlyList<GroupOption> options = GroupOptions.TargetChoices(temp.Session.AllGroups);

        Assert.Equal(new[] { "a › b", "a", "a › b (2)" }, GroupOptions.Labels(options));
    }

    [Fact]
    public void Index_of_label_finds_the_option_and_is_minus_one_when_missing()
    {
        IReadOnlyList<GroupOption> options = GroupOptions.ParentChoices(Session.AllGroups, excluded: null);

        Assert.Equal(3, GroupOptions.IndexOfLabel(options, "top › mid › deep"));
        Assert.Equal(0, GroupOptions.IndexOfLabel(options, "(top level)"));
        Assert.Equal(-1, GroupOptions.IndexOfLabel(options, "nope"));
        Assert.Equal(-1, GroupOptions.IndexOfLabel(options, null));
    }
}
