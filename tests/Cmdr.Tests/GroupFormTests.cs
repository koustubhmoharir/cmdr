using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

public sealed class GroupFormTests : IDisposable
{
    private readonly TempSession _temp = TempSession.Sample();

    public void Dispose() => _temp.Dispose();

    private CmdrSession Session => _temp.Session;

    private GroupEntry Group(string name) => Session.AllGroups.Single(g => g.Name == name);

    private static GroupFormValues Values(string name = "fresh", string cwd = "", string env = "", string description = "", bool expanded = true) =>
        new(name, description, GroupMode.Parallel, cwd, env, expanded);

    [Fact]
    public void Siblings_under_a_parent_are_its_groups_and_commands_together()
    {
        IReadOnlyList<string> names = GroupForm.SiblingNames(Group("top").Items, editing: null);

        Assert.Equal(new[] { "first", "mid", "last" }, names);
    }

    [Fact]
    public void The_edited_item_is_not_its_own_sibling()
    {
        IReadOnlyList<string> names = GroupForm.SiblingNames(Group("top").Items, editing: Group("mid"));

        Assert.Equal(new[] { "first", "last" }, names);
    }

    [Fact]
    public void A_group_name_clashes_with_a_sibling_command_of_the_chosen_parent()
    {
        IReadOnlyList<string> siblings = GroupForm.SiblingNames(Group("top").Items, editing: null);

        Assert.Single(GroupForm.Validate("first", siblings));
        Assert.Single(GroupForm.Validate("LAST", siblings));
    }

    [Fact]
    public void A_name_used_only_below_another_group_is_free()
    {
        IReadOnlyList<string> siblings = GroupForm.SiblingNames(Group("top").Items, editing: null);

        Assert.Empty(GroupForm.Validate("m1", siblings));
        Assert.Empty(GroupForm.Validate("oc", siblings));
    }

    [Fact]
    public void The_chosen_parent_decides_the_namespace_top_level_or_below()
    {
        // "other" is a top-level group, so it clashes at top level but not inside top.
        IReadOnlyList<string> topLevel = GroupForm.SiblingNames(Session.Groups, editing: null);
        IReadOnlyList<string> insideTop = GroupForm.SiblingNames(Group("top").Items, editing: null);

        Assert.Single(GroupForm.Validate("other", topLevel));
        Assert.Empty(GroupForm.Validate("other", insideTop));
    }

    [Fact]
    public void Renaming_a_group_to_its_own_name_is_allowed()
    {
        IReadOnlyList<string> siblings = GroupForm.SiblingNames(Session.Groups, editing: Group("top"));

        Assert.Empty(GroupForm.Validate("top", siblings));
        Assert.Empty(GroupForm.Validate(Values("TOP"), siblings));
    }

    [Fact]
    public void Blank_name_is_an_error_whatever_the_siblings()
    {
        Assert.Single(GroupForm.Validate(Values("   "), Array.Empty<string>()));
    }

    [Fact]
    public void Environment_errors_are_reported_with_the_name_errors()
    {
        IReadOnlyList<string> errors = GroupForm.Validate(Values(name: "  ", env: "A=1\nbogus"), Array.Empty<string>());

        Assert.Equal(2, errors.Count);
        Assert.StartsWith("Name", errors[0]);
        Assert.StartsWith("Env: Line 2", errors[1]);
    }

    [Fact]
    public void Build_trims_the_name_and_makes_blank_optional_fields_null()
    {
        GroupSettings settings = GroupForm.Build(Values(name: "  nav  ", cwd: "   ", env: "\n\n", description: "  "));

        Assert.Equal("nav", settings.Name);
        Assert.Null(settings.Cwd);
        Assert.Null(settings.Env);
        Assert.Null(settings.Description);
    }

    [Fact]
    public void Build_keeps_the_working_directory_trimmed_and_the_environment_with_removals()
    {
        GroupSettings settings = GroupForm.Build(Values(cwd: " ../web ", env: "A=1\n!B\nC=\n", description: "Text"));

        Assert.Equal("../web", settings.Cwd);
        Assert.Equal("Text", settings.Description);
        Assert.NotNull(settings.Env);
        Assert.Equal("1", settings.Env["A"]);
        Assert.True(settings.Env.ContainsKey("B"));
        Assert.Null(settings.Env["B"]);
        Assert.Equal(string.Empty, settings.Env["C"]);
    }

    [Fact]
    public void Build_passes_the_mode_through()
    {
        GroupSettings settings = GroupForm.Build(new GroupFormValues("x", null, GroupMode.Sequential, string.Empty, string.Empty, true));

        Assert.Equal(GroupMode.Sequential, settings.Mode);
    }

    [Fact]
    public void Build_passes_the_expanded_setting_through()
    {
        Assert.False(GroupForm.Build(Values(expanded: false)).Expanded);
        Assert.True(GroupForm.Build(Values(expanded: true)).Expanded);
    }

    [Fact]
    public void Kept_env_is_not_parsed_and_is_copied_into_the_settings()
    {
        var keptEnv = new Dictionary<string, string?> { ["A"] = "x\r\ny", ["B"] = null };
        GroupFormValues values = Values(env: "bogus\nA=unused") with { KeptEnv = keptEnv };

        Assert.Empty(GroupForm.Validate(values, Array.Empty<string>()));

        GroupSettings settings = GroupForm.Build(values);
        Assert.Equal(keptEnv, settings.Env);
        Assert.NotSame(keptEnv, settings.Env);
    }

    [Fact]
    public void Empty_kept_env_becomes_null()
    {
        GroupFormValues values = Values(env: "bogus") with { KeptEnv = new Dictionary<string, string?>() };

        Assert.Empty(GroupForm.Validate(values, Array.Empty<string>()));
        Assert.Null(GroupForm.Build(values).Env);
    }
}
