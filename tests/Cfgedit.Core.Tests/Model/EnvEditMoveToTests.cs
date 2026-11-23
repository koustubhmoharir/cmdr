using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// MoveNodeTo and NestBy on .env: a node moves into another group, and its key takes the new prefix (every key under a moved group does).
/// The lines keep their text, and only the keys change. Every case applies through <see cref="ConfigDocument.Apply"/>, so the logical
/// verification runs too.
/// </summary>
public sealed class EnvEditMoveToTests
{
    private static readonly IConfigFormat Env = EditTestSupport.Env;

    private static readonly NodePath RootGroup = At(Item(0));

    private static NodePath Root(string key) => At(Item(0), Member(key));

    private static NodePath Group(string name) => At(Item(0), Member(name));

    // ----- Entries into a group -----

    [Fact]
    public void An_entry_moves_up_into_the_group_above_as_its_last_member()
    {
        (string text, NodePath? focus) = EditWithFocus("G__X=1\nA=2\n", Env, new MoveNodeTo(Root("A"), Group("G"), 1, null));

        Assert.Equal("G__X=1\nG__A=2\n", text);
        Assert.Equal(At(Item(0), Member("G"), Member("A")), focus);
    }

    [Fact]
    public void An_entry_moves_down_into_the_group_below_as_its_first_member()
    {
        string text = Edit("A=1\nG__X=2\n", Env, new MoveNodeTo(Root("A"), Group("G"), 0, null));

        Assert.Equal("G__A=1\nG__X=2\n", text);
    }

    [Fact]
    public void An_entry_moves_into_the_middle_of_a_group_before_one_of_its_members()
    {
        string text = Edit("G__X=1\nG__Y=2\nA=3\n", Env, new MoveNodeTo(Root("A"), Group("G"), 1, null));

        Assert.Equal("G__X=1\nG__A=3\nG__Y=2\n", text);
    }

    [Fact]
    public void An_entry_moves_into_the_start_of_a_group_it_is_not_in()
    {
        string text = Edit("A__X=1\nA__B__Y=2\nZ=3\n", Env, new MoveNodeTo(Root("Z"), Group("A"), 0, null));

        Assert.Equal("A__Z=3\nA__X=1\nA__B__Y=2\n", text);
    }

    // ----- Entries out of a group -----

    [Fact]
    public void An_entry_leaves_a_group_before_the_group_and_its_line_stays_in_place()
    {
        (string text, NodePath? focus) = EditWithFocus("G__X=1\nG__Y=2\n", Env, new MoveNodeTo(At(Item(0), Member("G"), Member("X")), RootGroup, 0, null));

        Assert.Equal("X=1\nG__Y=2\n", text);
        Assert.Equal(Root("X"), focus);
    }

    [Fact]
    public void An_entry_leaves_a_group_above_the_group_and_its_line_moves_up()
    {
        string text = Edit("G__Y=2\nG__X=1\n", Env, new MoveNodeTo(At(Item(0), Member("G"), Member("X")), RootGroup, 0, null));

        Assert.Equal("X=1\nG__Y=2\n", text);
    }

    [Fact]
    public void An_entry_leaves_a_group_after_the_group_and_its_line_moves_down()
    {
        (string text, NodePath? focus) = EditWithFocus("G__X=1\nG__Y=2\n", Env, new MoveNodeTo(At(Item(0), Member("G"), Member("X")), RootGroup, 1, null));

        Assert.Equal("G__Y=2\nX=1\n", text);
        Assert.Equal(Root("X"), focus);
    }

    [Fact]
    public void An_entry_at_the_edge_of_a_group_leaves_it_as_nest_by_gives_the_step()
    {
        const string text = "G__X=1\nG__Y=2\n";
        NestStep? step = ModelTestSupport.Parse(text, Env).NestBy(At(Item(0), Member("G"), Member("X")), -1);

        Assert.NotNull(step);
        Assert.Equal(new MoveNodeTo(At(Item(0), Member("G"), Member("X")), RootGroup, 0, null), step.Op);
        Assert.Equal("X=1\nG__Y=2\n", Edit(text, Env, step.Op));
    }

    // ----- Groups -----

    [Fact]
    public void A_group_moves_up_into_a_group_above_and_every_key_under_it_takes_the_new_prefix()
    {
        string text = Edit(
            "A__B__z=3\nA__G__p=1\nA__G__q=2\n",
            Env,
            new MoveNodeTo(At(Item(0), Member("A"), Member("G")), At(Item(0), Member("A"), Member("B")), 0, null));

        Assert.Equal("A__B__G__p=1\nA__B__G__q=2\nA__B__z=3\n", text);
    }

    [Fact]
    public void A_group_moves_down_into_the_middle_of_a_group_below()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "A__G__p=1\nA__G__q=2\nA__B__z=3\nA__B__w=4\n",
            Env,
            new MoveNodeTo(At(Item(0), Member("A"), Member("G")), At(Item(0), Member("A"), Member("B")), 1, null));

        Assert.Equal("A__B__z=3\nA__B__G__p=1\nA__B__G__q=2\nA__B__w=4\n", text);
        Assert.Equal(At(Item(0), Member("A"), Member("B"), Member("G")), focus);
    }

    [Fact]
    public void A_group_whose_lines_sit_just_above_the_destination_changes_only_its_keys()
    {
        string text = Edit(
            "A__G__p=1\nA__G__q=2\nA__B__z=3\n",
            Env,
            new MoveNodeTo(At(Item(0), Member("A"), Member("G")), At(Item(0), Member("A"), Member("B")), 0, null));

        Assert.Equal("A__B__G__p=1\nA__B__G__q=2\nA__B__z=3\n", text);
    }

    [Fact]
    public void A_group_moves_out_of_its_group_to_the_root_and_its_keys_lose_the_prefix()
    {
        string text = Edit("A__G__p=1\nA__G__q=2\nA__B__z=3\n", Env, new MoveNodeTo(At(Item(0), Member("A"), Member("G")), RootGroup, 1, null));

        Assert.Equal("A__B__z=3\nG__p=1\nG__q=2\n", text);
    }

    [Fact]
    public void A_group_with_a_new_key_takes_the_new_segment_in_every_key()
    {
        string text = Edit("G__p=1\nG__q=2\nH__z=3\n", Env, new MoveNodeTo(Group("G"), Group("H"), 0, "K"));

        Assert.Equal("H__K__p=1\nH__K__q=2\nH__z=3\n", text);
    }

    [Fact]
    public void A_group_whose_segment_collides_with_a_sibling_group_is_refused()
    {
        CfgeditException error = Fails("G__p=1\nH__G__q=2\n", Env, new MoveNodeTo(Group("G"), Group("H"), 0, null));

        Assert.Equal("a key named G already exists here", error.Message);
    }

    // ----- New keys -----

    [Fact]
    public void A_new_key_renames_the_entry_and_focus_follows_it()
    {
        (string text, NodePath? focus) = EditWithFocus("A=1\nG__X=2\n", Env, new MoveNodeTo(Root("A"), Group("G"), 0, "Q"));

        Assert.Equal("G__Q=1\nG__X=2\n", text);
        Assert.Equal(At(Item(0), Member("G"), Member("Q")), focus);
    }

    [Fact]
    public void A_new_key_with_a_double_underscore_is_refused()
    {
        CfgeditException error = Fails("A=1\nG__X=2\n", Env, new MoveNodeTo(Root("A"), Group("G"), 0, "B__C"));

        Assert.Equal("a key part must be a valid name without __ and must not start or end with _", error.Message);
    }

    [Fact]
    public void A_key_that_collides_with_a_sibling_is_refused()
    {
        CfgeditException error = Fails("G__X=1\nX=2\n", Env, new MoveNodeTo(Root("X"), Group("G"), 1, null));

        Assert.Equal("a key named X already exists here", error.Message);
    }

    [Fact]
    public void A_new_key_that_collides_with_a_sibling_is_refused()
    {
        CfgeditException error = Fails("G__X=1\nA=2\n", Env, new MoveNodeTo(Root("A"), Group("G"), 1, "X"));

        Assert.Equal("a key named X already exists here", error.Message);
    }

    [Fact]
    public void A_key_that_would_pass_the_nesting_cap_is_refused()
    {
        string long255 = string.Join("__", Enumerable.Repeat("s", 255));
        CfgeditException error = Fails($"G__{long255}=1\nH__z=2\n", Env, new MoveNodeTo(Group("G"), Group("H"), 0, null));

        Assert.Equal("a key can have at most 256 parts", error.Message);
    }

    // ----- Groups and entries that cannot move -----

    [Fact]
    public void A_group_whose_lines_are_not_next_to_each_other_cannot_move()
    {
        CfgeditException error = Fails("G__X=1\nB=2\nG__Y=3\nH__Z=4\n", Env, new MoveNodeTo(Group("G"), Group("H"), 0, null));

        Assert.Equal("this cannot move: its lines are not next to each other in the file", error.Message);
    }

    [Fact]
    public void A_group_cannot_move_into_itself()
    {
        CfgeditException error = Fails("G__H__X=1\nG__H__Y=2\n", Env, new MoveNodeTo(Group("G"), At(Item(0), Member("G"), Member("H")), 0, null));

        Assert.Equal("a node cannot move into itself", error.Message);
    }

    [Fact]
    public void An_entry_already_in_the_group_is_refused()
    {
        CfgeditException error = Fails("G__X=1\nG__Y=2\n", Env, new MoveNodeTo(At(Item(0), Member("G"), Member("X")), Group("G"), 0, null));

        Assert.Equal("this cannot move there: it is already in that group", error.Message);
    }

    [Fact]
    public void An_index_past_the_destination_children_is_refused()
    {
        CfgeditException error = Fails("A=1\nG__X=2\n", Env, new MoveNodeTo(Root("A"), Group("G"), 5, null));

        Assert.Equal("Index 5 is outside the children, 0 to 1", error.Message);
    }

    [Fact]
    public void An_entry_cannot_be_the_destination()
    {
        CfgeditException error = Fails("A=1\nB=2\n", Env, new MoveNodeTo(Root("B"), Root("A"), 0, null));

        Assert.Equal("an entry has no children", error.Message);
    }

    // ----- Comments -----

    [Fact]
    public void An_entry_with_its_attached_comment_moves_with_it_and_the_comment_shows_in_the_group()
    {
        string text = Edit("# about A\nA=1\nG__X=2\n", Env, new MoveNodeTo(Root("A"), Group("G"), 0, null));

        Assert.Equal("# about A\nG__A=1\nG__X=2\n", text);
    }

    [Fact]
    public void An_entry_with_a_trailing_comment_keeps_it_on_its_line()
    {
        string text = Edit("A=1 # note\nG__X=2\n", Env, new MoveNodeTo(Root("A"), Group("G"), 0, null));

        Assert.Equal("G__A=1 # note\nG__X=2\n", text);
    }

    [Fact]
    public void A_lone_comment_moves_into_a_group_when_the_entry_below_it_is_in_that_group()
    {
        (string text, NodePath? focus) = EditWithFocus("# lone\n\nG__X=1\n", Env, new MoveNodeTo(At(Item(0), Comment(0)), Group("G"), 0, null));

        Assert.Equal("\n# lone\nG__X=1\n", text);
        Assert.Equal(At(Item(0), Member("G"), Comment(0)), focus);
    }

    [Fact]
    public void A_lone_comment_is_refused_where_the_entry_below_it_is_not_in_the_group()
    {
        CfgeditException error = Fails("# c\n\nG__X=1\nA=2\n", Env, new MoveNodeTo(At(Item(0), Comment(0)), Group("G"), 1, null));

        Assert.Equal("this cannot move there: the comment would show with the entry below it, which is not in that group", error.Message);
    }

    [Fact]
    public void A_comment_cannot_take_a_new_key()
    {
        CfgeditException error = Fails("# c\n\nG__X=1\n", Env, new MoveNodeTo(At(Item(0), Comment(0)), Group("G"), 0, "k"));

        Assert.Equal("a comment has no key", error.Message);
    }

    // ----- Text kept -----

    [Fact]
    public void The_export_prefix_spacing_and_a_multi_line_quoted_value_are_kept()
    {
        string text = Edit("B=1\nexport A__X =  \"a\nb\"\n", Env, new MoveNodeTo(At(Item(0), Member("A"), Member("X")), RootGroup, 0, null));

        Assert.Equal("export X =  \"a\nb\"\nB=1\n", text);
    }

    [Fact]
    public void In_a_crlf_document_the_moved_line_keeps_its_crlf()
    {
        string text = Edit("A=1\r\nG__X=2\r\n", Env, new MoveNodeTo(Root("A"), Group("G"), 0, null));

        Assert.Equal("G__A=1\r\nG__X=2\r\n", text);
    }

    [Fact]
    public void A_last_line_without_a_final_break_moves_into_the_group_above_and_the_file_still_ends_without_one()
    {
        string text = Edit("G__X=1\nA=2", Env, new MoveNodeTo(Root("A"), Group("G"), 1, null));

        Assert.Equal("G__X=1\nG__A=2", text);
    }

    [Fact]
    public void A_line_moves_down_to_the_end_of_a_file_without_a_final_break_and_the_file_still_ends_without_one()
    {
        string text = Edit("A=1\nG__X=2", Env, new MoveNodeTo(Root("A"), Group("G"), 1, null));

        Assert.Equal("G__X=2\nG__A=1", text);
    }

    [Fact]
    public void A_blank_line_between_the_moved_line_and_the_destination_stays()
    {
        string text = Edit("G__X=1\n\nA=2\n", Env, new MoveNodeTo(Root("A"), Group("G"), 1, null));

        Assert.Equal("G__X=1\nG__A=2\n\n", text);
    }

    // ----- NestBy -----

    [Fact]
    public void Nest_up_enters_the_group_above_at_its_end_and_keeps_the_key()
    {
        NestStep? step = ModelTestSupport.Parse("G__X=1\nA=2\n", Env).NestBy(Root("A"), -1);

        Assert.Equal(new MoveNodeTo(Root("A"), Group("G"), 1, null), step!.Op);
        Assert.False(step.NeedsKey);
    }

    [Fact]
    public void Nest_down_enters_the_group_below_at_its_start()
    {
        NestStep? step = ModelTestSupport.Parse("A=1\nG__X=2\n", Env).NestBy(Root("A"), 1);

        Assert.Equal(new MoveNodeTo(Root("A"), Group("G"), 0, null), step!.Op);
    }

    [Fact]
    public void Nest_past_an_entry_neighbour_is_the_ordinary_move()
    {
        NestStep? step = ModelTestSupport.Parse("X=1\nA=2\n", Env).NestBy(Root("A"), -1);

        Assert.Equal(new MoveNode(Root("A"), 0), step!.Op);
    }

    [Fact]
    public void Nest_at_the_root_edge_has_no_step()
    {
        ConfigDocument document = ModelTestSupport.Parse("A=1\nB=2\n", Env);

        Assert.Null(document.NestBy(Root("A"), -1));
        Assert.Null(document.NestBy(Root("B"), 1));
    }
}
