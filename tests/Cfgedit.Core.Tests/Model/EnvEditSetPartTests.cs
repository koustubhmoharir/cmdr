using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>SetPart on .env: values in each quote style, keys (entries and groups), and comments.</summary>
public sealed class EnvEditSetPartTests
{
    private static readonly IConfigFormat Env = EditTestSupport.Env;

    private static NodePath Entry(params string[] keys)
    {
        var steps = new List<PathStep> { Item(0) };
        steps.AddRange(keys.Select(key => Member(key)));
        return At(steps.ToArray());
    }

    // ----- Values -----

    [Fact]
    public void A_double_quoted_value_stays_double_quoted()
    {
        string text = Edit("A=\"x\"\n", Env, new SetPart(Entry("A"), PartKind.Value, -1, "y z"));

        Assert.Equal("A=\"y z\"\n", text);
    }

    [Fact]
    public void A_single_quoted_value_stays_single_quoted()
    {
        string text = Edit("A='x'\n", Env, new SetPart(Entry("A"), PartKind.Value, -1, "y"));

        Assert.Equal("A='y'\n", text);
    }

    [Fact]
    public void An_unquoted_value_stays_unquoted_when_it_can()
    {
        string text = Edit("A=1\n", Env, new SetPart(Entry("A"), PartKind.Value, -1, "2"));

        Assert.Equal("A=2\n", text);
    }

    [Fact]
    public void An_unquoted_value_with_a_dollar_sign_moves_to_single_quotes()
    {
        string text = Edit("A=1\n", Env, new SetPart(Entry("A"), PartKind.Value, -1, "$HOME"));

        Assert.Equal("A='$HOME'\n", text);
    }

    [Fact]
    public void A_single_quoted_value_with_an_apostrophe_moves_to_double_quotes()
    {
        string text = Edit("A='x'\n", Env, new SetPart(Entry("A"), PartKind.Value, -1, "it's"));

        Assert.Equal("A=\"it's\"\n", text);
    }

    [Fact]
    public void An_empty_value_is_written_unquoted()
    {
        string text = Edit("A=1\n", Env, new SetPart(Entry("A"), PartKind.Value, -1, string.Empty));

        Assert.Equal("A=\n", text);
    }

    [Fact]
    public void A_value_edit_keeps_the_trailing_comment_and_its_spacing()
    {
        string text = Edit("A=1 # note\n", Env, new SetPart(Entry("A"), PartKind.Value, -1, "2"));

        Assert.Equal("A=2 # note\n", text);
    }

    [Fact]
    public void A_value_written_after_an_empty_value_keeps_its_comment_apart()
    {
        string text = Edit("A= # note\n", Env, new SetPart(Entry("A"), PartKind.Value, -1, "x"));

        Assert.Equal("A= x # note\n", text);
    }

    [Fact]
    public void A_quoted_value_written_over_an_empty_value_keeps_its_comment_apart()
    {
        string text = Edit("A= # note\n", Env, new SetPart(Entry("A"), PartKind.Value, -1, "a # b"));

        Assert.Equal("A= 'a # b' # note\n", text);
    }

    [Fact]
    public void An_empty_value_in_single_quotes_stays_in_single_quotes()
    {
        string text = Edit("A='x' # note\n", Env, new SetPart(Entry("A"), PartKind.Value, -1, string.Empty));

        Assert.Equal("A='' # note\n", text);
    }

    [Fact]
    public void A_multi_line_value_is_replaced_across_its_lines()
    {
        string text = Edit("A=\"a\nb\"\nB=1\n", Env, new SetPart(Entry("A"), PartKind.Value, -1, "c\nd"));

        Assert.Equal("A=\"c\nd\"\nB=1\n", text);
    }

    [Fact]
    public void In_a_CRLF_document_a_line_break_in_a_value_is_written_as_CRLF()
    {
        string text = Edit("A=1\r\nB=2\r\n", Env, new SetPart(Entry("B"), PartKind.Value, -1, "a\nb"));

        Assert.Equal("A=1\r\nB='a\r\nb'\r\n", text);
    }

    [Fact]
    public void A_value_with_a_control_character_is_refused()
    {
        CfgeditException error = Fails("A=1\n", Env, new SetPart(Entry("A"), PartKind.Value, -1, "a\u0001b"));

        Assert.Equal("a .env value cannot contain control character U+0001", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData(" lead")]
    [InlineData("trail ")]
    [InlineData("a # b")]
    [InlineData("it's")]
    [InlineData("say \"hi\"")]
    [InlineData("back\\slash")]
    [InlineData("ends\\")]
    [InlineData("$HOME")]
    [InlineData("tab\tin")]
    [InlineData("two\nlines")]
    [InlineData("cr\rlf")]
    [InlineData("'both\" kinds'")]
    public void Every_value_reads_back_as_written(string value)
    {
        EditResult result = ModelTestSupport.Parse("A=1\n", Env).Apply(new SetPart(Entry("A"), PartKind.Value, -1, value));

        Assert.Equal(value, Env.ToLogical(result.Document).Children[0].Value);
    }

    [Fact]
    public void Setting_a_value_focuses_the_same_entry()
    {
        (string text, NodePath? focus) = EditWithFocus("A=1\nB=2\n", Env, new SetPart(Entry("B"), PartKind.Value, -1, "3"));

        Assert.Equal("A=1\nB=3\n", text);
        Assert.Equal(Entry("B"), focus);
    }

    // ----- Keys -----

    [Fact]
    public void Renaming_an_entry_keeps_its_export_keyword()
    {
        string text = Edit("export A=1\n", Env, new SetPart(Entry("A"), PartKind.Key, -1, "B"));

        Assert.Equal("export B=1\n", text);
    }

    [Fact]
    public void Renaming_an_entry_keeps_the_spaces_around_equals()
    {
        string text = Edit("A = 1\n", Env, new SetPart(Entry("A"), PartKind.Key, -1, "B"));

        Assert.Equal("B = 1\n", text);
    }

    [Fact]
    public void Renaming_an_entry_inside_a_group_renames_its_last_segment_only()
    {
        string text = Edit("A__X=1\nA__Y=2\n", Env, new SetPart(Entry("A", "X"), PartKind.Key, -1, "Z"));

        Assert.Equal("A__Z=1\nA__Y=2\n", text);
    }

    [Fact]
    public void Renaming_a_group_renames_its_segment_in_every_entry_across_non_adjacent_lines()
    {
        string text = Edit("A__X=1\nB=2\nA__Y=3\n", Env, new SetPart(Entry("A"), PartKind.Key, -1, "C"));

        Assert.Equal("C__X=1\nB=2\nC__Y=3\n", text);
    }

    [Fact]
    public void Renaming_a_nested_group_renames_the_segment_at_its_depth()
    {
        string text = Edit("A__B__X=1\nA__C=2\n", Env, new SetPart(Entry("A", "B"), PartKind.Key, -1, "D"));

        Assert.Equal("A__D__X=1\nA__C=2\n", text);
    }

    [Fact]
    public void Renaming_a_group_focuses_the_group_under_its_new_name()
    {
        (string text, NodePath? focus) = EditWithFocus("A__X=1\nB=2\n", Env, new SetPart(Entry("A"), PartKind.Key, -1, "C"));

        Assert.Equal("C__X=1\nB=2\n", text);
        Assert.Equal(Entry("C"), focus);
    }

    [Fact]
    public void A_key_part_with_a_space_is_refused_as_an_invalid_key()
    {
        CfgeditException error = Fails("A=1\n", Env, new SetPart(Entry("A"), PartKind.Key, -1, "a b"));

        Assert.Equal("invalid key 'a b'", error.Message);
    }

    [Fact]
    public void A_key_part_that_starts_with_a_digit_is_refused_as_an_invalid_key()
    {
        CfgeditException error = Fails("A=1\n", Env, new SetPart(Entry("A"), PartKind.Key, -1, "1x"));

        Assert.Equal("invalid key '1x'", error.Message);
    }

    [Theory]
    [InlineData("a__b")]
    [InlineData("_x")]
    [InlineData("x_")]
    [InlineData("")]
    public void A_key_part_that_would_change_the_split_is_refused(string name)
    {
        CfgeditException error = Fails("A=1\n", Env, new SetPart(Entry("A"), PartKind.Key, -1, name));

        Assert.Equal("a key part must be a valid name without __ and must not start or end with _", error.Message);
    }

    [Fact]
    public void Renaming_to_the_name_of_a_sibling_entry_is_refused()
    {
        CfgeditException error = Fails("A=1\nB=2\n", Env, new SetPart(Entry("A"), PartKind.Key, -1, "B"));

        Assert.Equal("a key named B already exists here", error.Message);
    }

    [Fact]
    public void Renaming_a_group_to_the_name_of_a_sibling_group_is_refused()
    {
        CfgeditException error = Fails("A__X=1\nB=2\n", Env, new SetPart(Entry("B"), PartKind.Key, -1, "A"));

        Assert.Equal("a key named A already exists here", error.Message);
    }

    [Fact]
    public void The_root_has_no_key_to_rename()
    {
        CfgeditException error = Fails("A=1\n", Env, new SetPart(At(Item(0)), PartKind.Key, -1, "x"));

        Assert.Equal("this part cannot be edited", error.Message);
    }

    [Fact]
    public void A_group_summary_cannot_be_set()
    {
        CfgeditException error = Fails("A__X=1\n", Env, new SetPart(Entry("A"), PartKind.Summary, -1, "5"));

        Assert.Equal("this part cannot be edited", error.Message);
    }

    [Fact]
    public void Renaming_to_the_same_name_returns_the_same_document()
    {
        ConfigDocument document = ModelTestSupport.Parse("A=1\n", Env);

        EditResult result = document.Apply(new SetPart(Entry("A"), PartKind.Key, -1, "A"));

        Assert.Same(document, result.Document);
    }

    // ----- Comments -----

    [Fact]
    public void A_comment_line_is_edited_after_its_hash()
    {
        string text = Edit("# old\nA=1\n", Env, new SetPart(At(Item(0), Comment(0)), PartKind.Comment, -1, " new"));

        Assert.Equal("# new\nA=1\n", text);
    }

    [Fact]
    public void A_comment_with_a_line_break_is_refused()
    {
        CfgeditException error = Fails("# a\nA=1\n", Env, new SetPart(At(Item(0), Comment(0)), PartKind.Comment, -1, "x\ny"));

        Assert.Equal("a comment cannot contain a line break", error.Message);
    }

    [Fact]
    public void A_trailing_comment_is_edited_and_its_entry_keeps_its_value()
    {
        string text = Edit("A=1 # note\nB=2\n", Env, new SetPart(At(Item(0), Comment(0)), PartKind.Comment, -1, " changed"));

        Assert.Equal("A=1 # changed\nB=2\n", text);
    }
}
