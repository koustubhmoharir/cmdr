using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>InsertNode on .env: where the line goes, what it copies from its neighbours, and the key and comment rules.</summary>
public sealed class EnvEditInsertTests
{
    private static readonly IConfigFormat Env = EditTestSupport.Env;

    private static readonly NodePath RootGroup = At(Item(0));

    [Fact]
    public void An_entry_inserted_into_an_empty_file_is_its_only_line()
    {
        string text = Edit("", Env, new InsertNode(RootGroup, 0, new NewEnvEntry("A", "1")));

        Assert.Equal("A=1\n", text);
    }

    [Fact]
    public void An_entry_inserted_at_the_end_of_a_file_without_a_final_newline_gets_the_newline_first_and_none_after()
    {
        string text = Edit("A=1", Env, new InsertNode(RootGroup, 1, new NewEnvEntry("B", "2")));

        Assert.Equal("A=1\nB=2", text);
    }

    [Fact]
    public void An_entry_inserted_before_a_sibling_copies_export_and_the_spaces_around_equals()
    {
        string text = Edit("export A = 1\nC=3\n", Env, new InsertNode(RootGroup, 1, new NewEnvEntry("B", "2")));

        Assert.Equal("export A = 1\nexport B = 2\nC=3\n", text);
    }

    [Fact]
    public void An_entry_inserted_after_the_last_entry_of_a_group_copies_its_indent_and_spacing()
    {
        string text = Edit("  A__X = 'v'\n", Env, new InsertNode(At(Item(0), Member("A")), 1, new NewEnvEntry("Y", "2")));

        Assert.Equal("  A__X = 'v'\n  A__Y = 2\n", text);
    }

    [Fact]
    public void An_entry_inserted_at_the_start_of_a_group_goes_before_the_group_and_its_attached_comments()
    {
        // The comment above the group's first entry belongs to that entry, so the new line goes above the comment.
        string text = Edit("# about X\nA__X=1\n", Env, new InsertNode(RootGroup, 0, new NewEnvEntry("Z", "0")));

        Assert.Equal("Z=0\n# about X\nA__X=1\n", text);
    }

    [Fact]
    public void An_entry_inserted_before_a_trailing_comment_goes_after_the_entry_line()
    {
        string text = Edit("A=1 # note\nC=3\n", Env, new InsertNode(RootGroup, 1, new NewEnvEntry("B", "2")));

        Assert.Equal("A=1 # note\nB=2\nC=3\n", text);
    }

    [Fact]
    public void An_entry_inserted_after_a_trailing_comment_at_the_end_goes_after_the_entry_line()
    {
        string text = Edit("A=1 # note\n", Env, new InsertNode(RootGroup, 2, new NewEnvEntry("B", "2")));

        Assert.Equal("A=1 # note\nB=2\n", text);
    }

    [Fact]
    public void A_key_with_double_underscores_creates_a_nested_group_and_focuses_the_new_entry()
    {
        (string text, NodePath? focus) = EditWithFocus("A=1\n", Env, new InsertNode(RootGroup, 1, new NewEnvEntry("B__C", "x")));

        Assert.Equal("A=1\nB__C=x\n", text);
        Assert.Equal(At(Item(0), Member("B"), Member("C")), focus);
    }

    [Fact]
    public void An_entry_whose_key_joins_a_group_shown_elsewhere_appears_in_that_group()
    {
        (string text, NodePath? focus) = EditWithFocus("A__X=1\nB=2\n", Env, new InsertNode(RootGroup, 2, new NewEnvEntry("A__Y", "3")));

        Assert.Equal("A__X=1\nB=2\nA__Y=3\n", text);
        Assert.Equal(At(Item(0), Member("A"), Member("Y")), focus);
    }

    [Fact]
    public void An_entry_inserted_into_a_group_writes_the_full_key()
    {
        string text = Edit("A__X=1\n", Env, new InsertNode(At(Item(0), Member("A")), 0, new NewEnvEntry("W", "0")));

        Assert.Equal("A__W=0\nA__X=1\n", text);
    }

    [Fact]
    public void A_value_with_a_hash_is_written_in_single_quotes()
    {
        string text = Edit("", Env, new InsertNode(RootGroup, 0, new NewEnvEntry("A", "a # b")));

        Assert.Equal("A='a # b'\n", text);
    }

    [Fact]
    public void A_key_that_does_not_split_into_the_parent_is_refused()
    {
        CfgeditException error = Fails("A__X=1\n", Env, new InsertNode(At(Item(0), Member("A")), 1, new NewEnvEntry("B____C", "1")));

        Assert.Equal("a key part must be a valid name without __ and must not start or end with _", error.Message);
    }

    [Fact]
    public void An_invalid_key_is_refused()
    {
        CfgeditException error = Fails("", Env, new InsertNode(RootGroup, 0, new NewEnvEntry("a b", "1")));

        Assert.Equal("invalid key 'a b'", error.Message);
    }

    [Fact]
    public void An_entry_whose_full_key_already_exists_is_refused()
    {
        CfgeditException error = Fails("A__X=1\n", Env, new InsertNode(RootGroup, 1, new NewEnvEntry("A__X", "2")));

        Assert.Equal("a key named A__X already exists", error.Message);
    }

    [Fact]
    public void An_entry_whose_key_repeats_an_entry_of_the_group_is_refused()
    {
        CfgeditException error = Fails("A__X=1\n", Env, new InsertNode(At(Item(0), Member("A")), 1, new NewEnvEntry("X", "2")));

        Assert.Equal("a key named A__X already exists", error.Message);
    }

    [Fact]
    public void An_index_past_the_children_is_refused()
    {
        CfgeditException error = Fails("A=1\n", Env, new InsertNode(RootGroup, 5, new NewEnvEntry("B", "2")));

        Assert.Equal("Index 5 is outside the children, 0 to 1", error.Message);
    }

    [Fact]
    public void An_entry_cannot_take_children()
    {
        CfgeditException error = Fails("A=1\n", Env, new InsertNode(At(Item(0), Member("A")), 0, new NewEnvComment(" x")));

        Assert.Equal("an entry has no children", error.Message);
    }

    [Fact]
    public void A_comment_is_written_as_a_hash_and_its_text_before_the_first_entry()
    {
        (string text, NodePath? focus) = EditWithFocus("A=1\n", Env, new InsertNode(RootGroup, 0, new NewEnvComment(" note")));

        Assert.Equal("# note\nA=1\n", text);
        Assert.Equal(At(Item(0), Comment(0)), focus);
    }

    [Fact]
    public void A_comment_with_a_line_break_is_refused()
    {
        CfgeditException error = Fails("A=1\n", Env, new InsertNode(RootGroup, 0, new NewEnvComment("a\nb")));

        Assert.Equal("a comment cannot contain a line break", error.Message);
    }
}
