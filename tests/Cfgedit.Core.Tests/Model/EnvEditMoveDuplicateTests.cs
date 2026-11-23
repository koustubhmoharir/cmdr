using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// MoveNode and DuplicateNode on .env: a unit is an entry with its attached comment lines and its trailing comment, or a group's
/// lines. Every case applies through <see cref="ConfigDocument.Apply"/>, so the logical verification runs too.
/// </summary>
public sealed class EnvEditMoveDuplicateTests
{
    private static readonly IConfigFormat Env = EditTestSupport.Env;

    private static readonly NodePath RootGroup = At(Item(0));

    private static NodePath Root(string key) => At(Item(0), Member(key));

    // ----- Move: entries in the root and in a group -----

    [Fact]
    public void An_entry_moves_down_past_the_entry_after_it()
    {
        string text = Edit("A=1\nB=2\nC=3\n", Env, new MoveNode(Root("A"), 1));

        Assert.Equal("B=2\nA=1\nC=3\n", text);
    }

    [Fact]
    public void An_entry_moves_up_past_the_entry_before_it()
    {
        string text = Edit("A=1\nB=2\nC=3\n", Env, new MoveNode(Root("C"), 1));

        Assert.Equal("A=1\nC=3\nB=2\n", text);
    }

    [Fact]
    public void A_group_member_moves_up_and_stays_in_its_group()
    {
        string text = Edit("A__X=1\nA__Y=2\nA__Z=3\n", Env, new MoveNode(At(Item(0), Member("A"), Member("Y")), 0));

        Assert.Equal("A__Y=2\nA__X=1\nA__Z=3\n", text);
    }

    [Fact]
    public void A_group_member_moves_down_one_step_as_move_by_gives_it()
    {
        const string text = "A__X=1\nA__Y=2\nA__Z=3\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Env);
        MoveNode? move = document.MoveBy(At(Item(0), Member("A"), Member("X")), 1);

        Assert.NotNull(move);
        Assert.Equal("A__Y=2\nA__X=1\nA__Z=3\n", Edit(text, Env, move));
    }

    [Fact]
    public void A_group_member_cannot_pass_a_line_of_another_entry_that_lies_between_them()
    {
        CfgeditException error = Fails("A__X=1\nB=2\nA__Y=3\n", Env, new MoveNode(At(Item(0), Member("A"), Member("Y")), 0));

        Assert.Equal("this cannot move there: a line that is not part of the siblings it passes lies between them", error.Message);
    }

    [Fact]
    public void A_group_moves_down_past_the_entry_after_it()
    {
        string text = Edit("A__X=1\nA__Y=2\nB=3\n", Env, new MoveNode(Root("A"), 1));

        Assert.Equal("B=3\nA__X=1\nA__Y=2\n", text);
    }

    [Fact]
    public void A_group_whose_lines_are_not_next_to_each_other_cannot_move()
    {
        CfgeditException error = Fails("A__X=1\nB=2\nA__Y=3\n", Env, new MoveNode(Root("A"), 1));

        Assert.Equal("this cannot move: its lines are not next to each other in the file", error.Message);
    }

    [Fact]
    public void A_neighbour_whose_lines_are_not_next_to_each_other_cannot_be_passed()
    {
        CfgeditException error = Fails("A__X=1\nB=2\nA__Y=3\nC=4\n", Env, new MoveNode(Root("C"), 0));

        Assert.Equal("this cannot move: the lines of the sibling it goes next to are not next to each other in the file", error.Message);
    }

    [Fact]
    public void A_move_to_the_position_it_already_has_changes_nothing()
    {
        (string text, NodePath? focus) = EditWithFocus("A=1\nB=2\n", Env, new MoveNode(Root("A"), 0));

        Assert.Equal("A=1\nB=2\n", text);
        Assert.Equal(Root("A"), focus);
    }

    // ----- Move: attached and trailing comments -----

    [Fact]
    public void An_entry_with_its_attached_comment_and_trailing_comment_moves_as_one_unit()
    {
        string text = Edit("# about A\nA=1 # t\nB=2\n", Env, new MoveNode(Root("A"), 1));

        Assert.Equal("B=2\n# about A\nA=1 # t\n", text);
    }

    [Fact]
    public void A_comment_line_moves_alone_past_the_entry_after_it()
    {
        string text = Edit("# c\nA=1\nB=2\n", Env, new MoveNode(At(Item(0), Comment(0)), 1));

        Assert.Equal("A=1\n# c\nB=2\n", text);
    }

    [Fact]
    public void A_trailing_comment_cannot_move()
    {
        CfgeditException error = Fails("A=1 # note\nB=2\n", Env, new MoveNode(At(Item(0), Comment(0)), 0));

        Assert.Equal("a trailing comment moves with its entry", error.Message);
    }

    [Fact]
    public void An_entry_cannot_move_between_a_comment_and_the_entry_it_is_attached_to()
    {
        CfgeditException error = Fails("A=1\n# c\nB=2\n", Env, new MoveNode(Root("A"), 1));

        Assert.Equal("this cannot move there: it would come between a comment and the entry it is attached to", error.Message);
    }

    [Fact]
    public void An_entry_cannot_move_between_an_entry_and_its_trailing_comment()
    {
        CfgeditException error = Fails("X=0\nA=1 # t\nB=2\n", Env, new MoveNode(Root("B"), 2));

        Assert.Equal("this cannot move there: it would come between an entry and its trailing comment", error.Message);
    }

    // ----- Move: the end of the file -----

    [Fact]
    public void The_last_line_of_a_file_without_a_final_newline_moves_up_and_the_file_still_ends_without_one()
    {
        string text = Edit("A=1\nB=2", Env, new MoveNode(Root("B"), 0));

        Assert.Equal("B=2\nA=1", text);
    }

    [Fact]
    public void The_last_line_cannot_move_up_over_a_blank_line_in_a_file_without_a_final_newline()
    {
        CfgeditException error = Fails("A=1\n\nB=2", Env, new MoveNode(Root("B"), 0));

        Assert.Equal(
            "this cannot move up: the blank line before the last line would have to go, and the file must keep ending as it does",
            error.Message);
    }

    [Fact]
    public void The_last_line_of_a_file_with_a_final_newline_moves_up_and_keeps_it()
    {
        string text = Edit("A=1\nB=2\n", Env, new MoveNode(Root("B"), 0));

        Assert.Equal("B=2\nA=1\n", text);
    }

    [Fact]
    public void A_line_moves_down_to_the_end_of_a_file_without_a_final_newline_and_the_file_still_ends_without_one()
    {
        string text = Edit("A=1\nB=2", Env, new MoveNode(Root("A"), 1));

        Assert.Equal("B=2\nA=1", text);
    }

    [Fact]
    public void A_line_moves_down_to_the_end_of_a_file_with_a_final_newline_and_keeps_it()
    {
        string text = Edit("A=1\nB=2\n", Env, new MoveNode(Root("A"), 1));

        Assert.Equal("B=2\nA=1\n", text);
    }

    [Fact]
    public void In_a_crlf_document_a_moved_line_keeps_its_crlf()
    {
        string text = Edit("A=1\r\nB=2\r\n", Env, new MoveNode(Root("A"), 1));

        Assert.Equal("B=2\r\nA=1\r\n", text);
    }

    [Fact]
    public void In_a_crlf_document_without_a_final_newline_the_last_line_moves_up_with_crlf()
    {
        string text = Edit("A=1\r\nB=2", Env, new MoveNode(Root("B"), 0));

        Assert.Equal("B=2\r\nA=1", text);
    }

    [Fact]
    public void A_group_that_is_the_last_lines_moves_up_with_its_text_kept_and_the_file_still_ends_without_a_newline()
    {
        string text = Edit("B=3\nA__X=1 # t\nA__Y=2", Env, new MoveNode(Root("A"), 0));

        Assert.Equal("A__X=1 # t\nA__Y=2\nB=3", text);
    }

    [Fact]
    public void Moving_focuses_the_moved_entry_at_its_new_place()
    {
        (string text, NodePath? focus) = EditWithFocus("A=1\nB=2\nC=3\n", Env, new MoveNode(Root("A"), 1));

        Assert.Equal("B=2\nA=1\nC=3\n", text);
        Assert.Equal(Root("A"), focus);
    }

    // ----- Duplicate -----

    [Fact]
    public void A_duplicate_entry_is_named_with_a_number_and_focused()
    {
        (string text, NodePath? focus) = EditWithFocus("A=1\nB=2\n", Env, new DuplicateNode(Root("A")));

        Assert.Equal("A=1\nA2=1\nB=2\n", text);
        Assert.Equal(Root("A2"), focus);
    }

    [Fact]
    public void A_second_copy_takes_the_next_number_that_is_free()
    {
        string text = Edit("A=1\nA2=9\n", Env, new DuplicateNode(Root("A")));

        Assert.Equal("A=1\nA3=1\nA2=9\n", text);
    }

    [Fact]
    public void The_copy_name_avoids_a_group_of_the_same_name_among_the_siblings()
    {
        string text = Edit("A=1\nA2__X=2\n", Env, new DuplicateNode(Root("A")));

        Assert.Equal("A=1\nA3=1\nA2__X=2\n", text);
    }

    [Fact]
    public void An_entry_duplicate_copies_its_attached_comment_and_trailing_comment()
    {
        string text = Edit("# about A\nA=1 # t\nB=2\n", Env, new DuplicateNode(Root("A")));

        Assert.Equal("# about A\nA=1 # t\n# about A\nA2=1 # t\nB=2\n", text);
    }

    [Fact]
    public void A_group_duplicate_renames_the_segment_in_every_key_and_is_focused()
    {
        (string text, NodePath? focus) = EditWithFocus("A__X=1\nA__Y=2\nB=3\n", Env, new DuplicateNode(Root("A")));

        Assert.Equal("A__X=1\nA__Y=2\nA2__X=1\nA2__Y=2\nB=3\n", text);
        Assert.Equal(Root("A2"), focus);
    }

    [Fact]
    public void A_nested_group_duplicate_renames_only_its_own_segment()
    {
        string text = Edit("P__A__X=1\nP__A__Y=2\n", Env, new DuplicateNode(At(Item(0), Member("P"), Member("A"))));

        Assert.Equal("P__A__X=1\nP__A__Y=2\nP__A2__X=1\nP__A2__Y=2\n", text);
    }

    [Fact]
    public void A_duplicate_at_the_end_of_a_file_without_a_final_newline_gets_a_newline_and_none_after()
    {
        string text = Edit("A=1\nB=2", Env, new DuplicateNode(Root("B")));

        Assert.Equal("A=1\nB=2\nB2=2", text);
    }

    [Fact]
    public void A_duplicate_of_a_line_in_a_file_without_a_final_newline_is_placed_after_it()
    {
        string text = Edit("A=1\nB=2", Env, new DuplicateNode(Root("A")));

        Assert.Equal("A=1\nA2=1\nB=2", text);
    }

    [Fact]
    public void In_a_crlf_document_the_copy_keeps_crlf()
    {
        string text = Edit("A=1\r\nB=2\r\n", Env, new DuplicateNode(Root("A")));

        Assert.Equal("A=1\r\nA2=1\r\nB=2\r\n", text);
    }

    [Fact]
    public void A_comment_line_is_copied_after_itself()
    {
        string text = Edit("# a\nA=1\n", Env, new DuplicateNode(At(Item(0), Comment(0))));

        Assert.Equal("# a\n# a\nA=1\n", text);
    }

    [Fact]
    public void A_trailing_comment_is_not_duplicated_on_its_own()
    {
        CfgeditException error = Fails("A=1 # note\nB=2\n", Env, new DuplicateNode(At(Item(0), Comment(0))));

        Assert.Equal("a trailing comment is duplicated with its entry", error.Message);
    }

    [Fact]
    public void The_root_cannot_be_duplicated()
    {
        CfgeditException error = Fails("A=1\n", Env, new DuplicateNode(RootGroup));

        Assert.Equal("the root cannot be duplicated", error.Message);
    }

    [Fact]
    public void A_group_whose_lines_are_not_next_to_each_other_cannot_be_duplicated()
    {
        CfgeditException error = Fails("A__X=1\nB=2\nA__Y=3\n", Env, new DuplicateNode(Root("A")));

        Assert.Equal("this cannot be copied: its lines are not next to each other in the file", error.Message);
    }
}
