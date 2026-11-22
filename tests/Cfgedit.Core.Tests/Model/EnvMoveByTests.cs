using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// The sibling layout of .env: <see cref="ConfigDocument.InsertionIndexBefore"/>, <see cref="ConfigDocument.InsertionIndexAfter"/> and
/// <see cref="ConfigDocument.MoveBy"/> over entries with attached and trailing comments, and over groups. An entry's attached comment
/// lines come before it and its trailing comment after it, and a group moves with all its lines.
/// </summary>
public sealed class EnvMoveByTests
{
    private static readonly IConfigFormat Env = EditTestSupport.Env;

    private static NodePath Root(string key) => At(Item(0), Member(key));

    private static NodePath InGroup(string group, string key) => At(Item(0), Member(group), Member(key));

    [Fact]
    public void An_entry_with_attached_and_trailing_comments_is_inserted_before_its_comment_and_after_its_trailing_comment()
    {
        ConfigDocument document = ModelTestSupport.Parse("# c\nA=1 # t\nB=2\n", Env);

        Assert.Equal(0, document.InsertionIndexBefore(Root("A")));
        Assert.Equal(3, document.InsertionIndexAfter(Root("A")));
        Assert.Equal(3, document.InsertionIndexBefore(Root("B")));
        Assert.Equal(4, document.InsertionIndexAfter(Root("B")));
    }

    [Fact]
    public void A_comment_separated_by_a_blank_line_is_not_attached_to_the_entry_below_it()
    {
        ConfigDocument document = ModelTestSupport.Parse("# c\n\nA=1\n", Env);

        Assert.Equal(1, document.InsertionIndexBefore(Root("A")));
        Assert.Equal(1, document.InsertionIndexAfter(At(Item(0), Comment(0))));
    }

    [Fact]
    public void A_group_is_one_sibling_at_its_place_and_its_members_are_siblings_within_it()
    {
        ConfigDocument document = ModelTestSupport.Parse("A__X=1 # t\nA__Y=2\nB=3\n", Env);

        Assert.Equal(0, document.InsertionIndexBefore(Root("A")));
        Assert.Equal(1, document.InsertionIndexAfter(Root("A")));
        Assert.Equal(0, document.InsertionIndexBefore(InGroup("A", "X")));
        Assert.Equal(2, document.InsertionIndexAfter(InGroup("A", "X")));
        Assert.Equal(2, document.InsertionIndexBefore(InGroup("A", "Y")));
    }

    [Fact]
    public void MoveBy_moves_an_entry_up_past_the_comment_attached_entry_as_one_unit()
    {
        const string text = "# c\nA=1\nB=2\n";
        MoveNode move = Move(text, Root("B"), -1);

        Assert.Equal(0, move.NewIndex);
        Assert.Equal("B=2\n# c\nA=1\n", Edit(text, Env, move));
    }

    [Fact]
    public void MoveBy_moves_a_comment_attached_entry_down_past_the_entry_after_it()
    {
        const string text = "# c\nA=1\nB=2\n";
        MoveNode move = Move(text, Root("A"), 1);

        Assert.Equal(1, move.NewIndex);
        Assert.Equal("B=2\n# c\nA=1\n", Edit(text, Env, move));
    }

    [Fact]
    public void MoveBy_moves_a_comment_line_down_past_the_entry_after_it()
    {
        const string text = "# c\nA=1\nB=2\n";
        MoveNode move = Move(text, At(Item(0), Comment(0)), 1);

        Assert.Equal(1, move.NewIndex);
        Assert.Equal("A=1\n# c\nB=2\n", Edit(text, Env, move));
    }

    [Fact]
    public void MoveBy_moves_a_group_member_with_a_trailing_comment_past_the_member_after_it()
    {
        const string text = "A__X=1 # t\nA__Y=2\n";
        MoveNode move = Move(text, InGroup("A", "X"), 1);

        Assert.Equal(1, move.NewIndex);
        Assert.Equal("A__Y=2\nA__X=1 # t\n", Edit(text, Env, move));
    }

    [Fact]
    public void MoveBy_moves_a_group_past_the_entry_after_it_and_back()
    {
        const string text = "A__X=1\nA__Y=2\nB=3\n";

        Assert.Equal("B=3\nA__X=1\nA__Y=2\n", Edit(text, Env, Move(text, Root("A"), 1)));
        Assert.Equal("B=3\nA__X=1\nA__Y=2\n", Edit(text, Env, Move(text, Root("B"), -1)));
    }

    [Fact]
    public void MoveBy_gives_no_move_at_the_ends_of_the_siblings()
    {
        ConfigDocument document = ModelTestSupport.Parse("A=1\n# c\nB=2\n", Env);

        Assert.Null(document.MoveBy(Root("A"), -1));
        Assert.Null(document.MoveBy(Root("B"), 1));
        Assert.Null(document.MoveBy(At(Item(0)), 1));
    }

    [Fact]
    public void MoveBy_on_a_trailing_comment_gives_a_move_that_the_planner_refuses_with_the_reason()
    {
        const string text = "A=1 # t\nB=2\n";
        MoveNode? move = ModelTestSupport.Parse(text, Env).MoveBy(At(Item(0), Comment(0)), 1);

        Assert.NotNull(move);
        Assert.Equal("a trailing comment moves with its entry", Fails(text, Env, move).Message);
    }

    private static MoveNode Move(string text, NodePath path, int delta)
    {
        MoveNode? move = ModelTestSupport.Parse(text, Env).MoveBy(path, delta);
        Assert.NotNull(move);
        return move;
    }
}
