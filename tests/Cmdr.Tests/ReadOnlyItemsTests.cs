using Cmdr.Core.Session;
using Cmdr.Tui;

namespace Cmdr.Tests;

/// <summary>The refusal text for base items (ItemEntry.ReadOnlyMessage), and the filter of groups that can be edited.</summary>
public sealed class ReadOnlyItemsTests : IDisposable
{
    private readonly TempChain _chain = TempChain.Open();

    public void Dispose() => _chain.Dispose();

    private GroupEntry Group(string name) => _chain.Session.AllGroups.Single(g => g.Name == name);

    [Fact]
    public void Message_for_a_base_group_names_it_and_its_file()
    {
        string? message = Group("shared").ReadOnlyMessage;

        Assert.Equal($"'shared' comes from {Path.Combine("shared", "base.json")}; edit that file.", message);
    }

    [Fact]
    public void Message_for_a_base_command_names_the_command_and_its_file()
    {
        CommandEntry tool = _chain.Session.AllEntries.Single(e => e.Name == "tool");

        Assert.Equal($"'tool' comes from {Path.Combine("shared", "base.json")}; edit that file.", tool.ReadOnlyMessage);
    }

    [Fact]
    public void Message_is_null_for_items_of_the_opened_file()
    {
        Assert.Null(Group("main").ReadOnlyMessage);
        Assert.Null(_chain.Session.AllEntries.Single(e => e.Name == "mine").ReadOnlyMessage);
    }

    [Fact]
    public void Message_matches_the_session_refusal()
    {
        string? message = Group("shared").ReadOnlyMessage;

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => _chain.Session.RemoveGroup(Group("shared")));
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public void Editable_leaves_out_base_groups_and_keeps_order()
    {
        IReadOnlyList<GroupEntry> editable = GroupTree.Editable(_chain.Session.AllGroups);

        Assert.Equal(new[] { "main" }, editable.Select(g => g.Name));
    }

    [Fact]
    public void Parent_and_target_choices_offer_only_the_opened_file_groups()
    {
        IReadOnlyList<GroupEntry> editable = GroupTree.Editable(_chain.Session.AllGroups);

        Assert.Equal(new[] { "(top level)", "main" }, GroupOptions.Labels(GroupOptions.ParentChoices(editable, excluded: null)));
        Assert.Equal(new[] { "main" }, GroupOptions.Labels(GroupOptions.TargetChoices(editable)));
    }

    [Fact]
    public void Top_level_names_of_the_opened_file_do_not_include_base_groups()
    {
        IReadOnlyList<string> names = GroupForm.SiblingNames(GroupTree.Editable(_chain.Session.Groups), editing: null);

        Assert.Equal(new[] { "main" }, names);
    }
}
