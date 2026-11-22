using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>DeleteNode on .env: an entry's block, a comment line, a trailing comment, a group's lines, and the root.</summary>
public sealed class EnvEditDeleteTests
{
    private static readonly IConfigFormat Env = EditTestSupport.Env;

    [Fact]
    public void Deleting_an_entry_deletes_the_comment_lines_attached_above_it()
    {
        string text = Edit("# about A\n# more\nA=1\nB=2\n", Env, new DeleteNode(At(Item(0), Member("A"))));

        Assert.Equal("B=2\n", text);
    }

    [Fact]
    public void Deleting_an_entry_keeps_the_blank_line_after_it_and_the_comment_of_the_next_entry()
    {
        string text = Edit("A=1\n\n# c\nB=2\n", Env, new DeleteNode(At(Item(0), Member("A"))));

        Assert.Equal("\n# c\nB=2\n", text);
    }

    [Fact]
    public void Deleting_an_entry_deletes_its_trailing_comment_with_its_line()
    {
        string text = Edit("A=1 # note\nB=2\n", Env, new DeleteNode(At(Item(0), Member("A"))));

        Assert.Equal("B=2\n", text);
    }

    [Fact]
    public void Deleting_a_trailing_comment_keeps_the_entry_and_its_line_break()
    {
        string text = Edit("A=1 # note\nB=2\n", Env, new DeleteNode(At(Item(0), Comment(0))));

        Assert.Equal("A=1\nB=2\n", text);
    }

    [Fact]
    public void Deleting_a_comment_line_deletes_only_that_line()
    {
        string text = Edit("# a\nA=1\n", Env, new DeleteNode(At(Item(0), Comment(0))));

        Assert.Equal("A=1\n", text);
    }

    [Fact]
    public void Deleting_a_group_deletes_its_lines_and_leaves_the_others_standing()
    {
        string text = Edit("A__X=1\nB=2\nA__Y=3 # t\n", Env, new DeleteNode(At(Item(0), Member("A"))));

        Assert.Equal("B=2\n", text);
    }

    [Fact]
    public void Deleting_a_group_deletes_the_comment_attached_to_its_first_entry()
    {
        string text = Edit("# x\nA__X=1\nB=2\n", Env, new DeleteNode(At(Item(0), Member("A"))));

        Assert.Equal("B=2\n", text);
    }

    [Fact]
    public void Deleting_the_last_line_of_a_file_without_a_final_newline_keeps_the_line_before_it()
    {
        string text = Edit("A=1\nB=2", Env, new DeleteNode(At(Item(0), Member("B"))));

        Assert.Equal("A=1\n", text);
    }

    [Fact]
    public void The_root_cannot_be_deleted()
    {
        CfgeditException error = Fails("A=1\n", Env, new DeleteNode(At(Item(0))));

        Assert.Equal("the root cannot be deleted", error.Message);
    }

    [Fact]
    public void Deleting_an_entry_focuses_the_next_sibling()
    {
        (string text, NodePath? focus) = EditWithFocus("A=1\nB=2\nC=3\n", Env, new DeleteNode(At(Item(0), Member("B"))));

        Assert.Equal("A=1\nC=3\n", text);
        Assert.Equal(At(Item(0), Member("C")), focus);
    }

    [Fact]
    public void Deleting_the_last_entry_focuses_the_previous_one()
    {
        (string text, NodePath? focus) = EditWithFocus("A=1\nB=2\n", Env, new DeleteNode(At(Item(0), Member("B"))));

        Assert.Equal("A=1\n", text);
        Assert.Equal(At(Item(0), Member("A")), focus);
    }
}
