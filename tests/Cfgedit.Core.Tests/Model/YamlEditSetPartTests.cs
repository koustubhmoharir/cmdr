using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>SetPart on YAML: scalar values by style and kind, keys, and comments.</summary>
public sealed class YamlEditSetPartTests
{
    private static readonly IConfigFormat Yaml = EditTestSupport.Yaml;

    private static NodePath Path(params PathStep[] steps) => At(new PathStep[] { Item(0) }.Concat(steps).ToArray());

    private static SetPart Value(string value, params PathStep[] steps) => new(Path(steps), PartKind.Value, -1, value);

    // ----- Plain scalars -----

    [Fact]
    public void An_empty_quoted_string_given_a_text_takes_the_plainest_style_but_a_non_empty_one_keeps_its_quotes()
    {
        Assert.Equal("a: web\n", Edit("a: ''\n", Yaml, Value("web", Member("a"))));
        Assert.Equal("a: web\n", Edit("a: \"\"\n", Yaml, Value("web", Member("a"))));
        Assert.Equal("a: 'true'\n", Edit("a: ''\n", Yaml, Value("true", Member("a"))));
        Assert.Equal("a: 'web'\n", Edit("a: 'x'\n", Yaml, Value("web", Member("a"))));
    }

    [Fact]
    public void A_plain_string_stays_plain()
    {
        string text = Edit("a: foo\n", Yaml, Value("bar baz", Member("a")));

        Assert.Equal("a: bar baz\n", text);
    }

    [Fact]
    public void A_plain_string_edited_to_a_boolean_word_is_single_quoted_so_it_stays_a_string()
    {
        string text = Edit("a: foo\n", Yaml, Value("true", Member("a")));

        Assert.Equal("a: 'true'\n", text);
    }

    [Fact]
    public void A_plain_string_edited_to_a_number_word_is_quoted()
    {
        string text = Edit("a: foo\n", Yaml, Value("12", Member("a")));

        Assert.Equal("a: '12'\n", text);
    }

    [Fact]
    public void A_plain_string_edited_to_empty_is_single_quoted()
    {
        string text = Edit("a: foo\n", Yaml, Value(string.Empty, Member("a")));

        Assert.Equal("a: ''\n", text);
    }

    [Fact]
    public void A_plain_string_with_colon_space_is_single_quoted()
    {
        string text = Edit("a: foo\n", Yaml, Value("x: y", Member("a")));

        Assert.Equal("a: 'x: y'\n", text);
    }

    [Fact]
    public void A_plain_string_edited_to_a_value_with_a_line_break_becomes_a_literal_block()
    {
        string text = Edit("a: foo\n", Yaml, Value("x\ny", Member("a")));

        Assert.Equal("a: |-\n  x\n  y\n", text);
    }

    [Fact]
    public void A_plain_string_edited_in_a_CRLF_document_keeps_the_line_breaks()
    {
        string text = Edit("a: 1\r\nb: foo\r\n", Yaml, Value("x\ny", Member("b")));

        Assert.Equal("a: 1\r\nb: |-\r\n  x\r\n  y\r\n", text);
    }

    [Fact]
    public void A_plain_value_on_the_line_with_a_trailing_comment_keeps_the_comment()
    {
        string text = Edit("a: foo # note\n", Yaml, Value("bar", Member("a")));

        Assert.Equal("a: bar # note\n", text);
    }

    [Fact]
    public void A_plain_value_edited_to_a_multi_line_text_moves_its_trailing_comment_to_the_header_line()
    {
        string text = Edit("a: foo # note\nb: 1\n", Yaml, Value("x\ny", Member("a")));

        Assert.Equal("a: |- # note\n  x\n  y\nb: 1\n", text);
    }

    // ----- Quoted scalars keep their quotes -----

    [Fact]
    public void A_double_quoted_value_stays_double_quoted()
    {
        string text = Edit("a: \"foo\"\n", Yaml, Value("b c", Member("a")));

        Assert.Equal("a: \"b c\"\n", text);
    }

    [Fact]
    public void A_double_quoted_value_escapes_what_it_must()
    {
        string text = Edit("a: \"foo\"\n", Yaml, Value("q\"t\tx", Member("a")));

        Assert.Equal("a: \"q\\\"t\\tx\"\n", text);
    }

    [Fact]
    public void A_single_quoted_value_stays_single_quoted_and_doubles_its_apostrophe()
    {
        string text = Edit("a: 'foo'\n", Yaml, Value("it's", Member("a")));

        Assert.Equal("a: 'it''s'\n", text);
    }

    [Fact]
    public void A_single_quoted_value_with_a_line_break_becomes_a_literal_block()
    {
        string text = Edit("a: 'foo'\n", Yaml, Value("x\ny", Member("a")));

        Assert.Equal("a: |-\n  x\n  y\n", text);
    }

    // ----- Block scalars -----

    [Fact]
    public void A_literal_block_stays_literal()
    {
        string text = Edit("a: |\n  one\n  two\nb: 1\n", Yaml, Value("x", Member("a")));

        Assert.Equal("a: |-\n  x\nb: 1\n", text);
    }

    [Fact]
    public void A_literal_block_keeps_its_final_line_break_when_the_value_ends_with_one()
    {
        string text = Edit("a: |\n  one\n", Yaml, Value("x\n", Member("a")));

        Assert.Equal("a: |\n  x\n", text);
    }

    [Fact]
    public void A_literal_block_keeps_extra_trailing_line_breaks_with_keep_chomping()
    {
        string text = Edit("a: |\n  one\n", Yaml, Value("x\n\n", Member("a")));

        Assert.Equal("a: |+\n  x\n\n", text);
    }

    [Fact]
    public void A_folded_block_becomes_literal()
    {
        string text = Edit("a: >\n  one\n  two\n", Yaml, Value("x y", Member("a")));

        Assert.Equal("a: |-\n  x y\n", text);
    }

    [Fact]
    public void A_literal_block_with_a_leading_space_gets_an_indentation_indicator()
    {
        string text = Edit("a: |\n  one\n", Yaml, Value(" lead\nx", Member("a")));

        Assert.Equal("a: |2-\n   lead\n  x\n", text);
    }

    [Fact]
    public void A_literal_block_inside_a_sequence_item_is_indented_from_the_dash()
    {
        string text = Edit("- |\n  one\n- b\n", Yaml, Value("x\ny", Item(0)));

        Assert.Equal("- |-\n  x\n  y\n- b\n", text);
    }

    [Fact]
    public void A_block_scalar_with_a_comment_on_its_header_is_refused()
    {
        CfgeditException ex = Fails("a: | # c\n  x\n", Yaml, Value("y", Member("a")));

        Assert.Equal("a block scalar with a comment on its header cannot be edited here", ex.Message);
    }

    // ----- Typed scalars -----

    [Fact]
    public void An_integer_takes_an_integer_and_refuses_other_text()
    {
        Assert.Equal("n: -3\n", Edit("n: 5\n", Yaml, Value("-3", Member("n"))));
        Assert.Equal("'abc' is not a valid integer", Fails("n: 5\n", Yaml, Value("abc", Member("n"))).Message);
    }

    [Fact]
    public void A_float_takes_a_float_and_refuses_an_integer_when_untagged()
    {
        Assert.Equal("f: 2.5e3\n", Edit("f: 1.5\n", Yaml, Value("2.5e3", Member("f"))));
        Assert.Equal("'2' is not a valid float", Fails("f: 1.5\n", Yaml, Value("2", Member("f"))).Message);
    }

    [Fact]
    public void A_boolean_takes_a_boolean_and_refuses_yes()
    {
        Assert.Equal("b: false\n", Edit("b: true\n", Yaml, Value("false", Member("b"))));
        Assert.Equal("'yes' is not a valid boolean", Fails("b: true\n", Yaml, Value("yes", Member("b"))).Message);
    }

    [Fact]
    public void A_null_takes_a_null_word_and_an_empty_text_removes_the_word()
    {
        Assert.Equal("n:\n", Edit("n: null\n", Yaml, Value(string.Empty, Member("n"))));
        Assert.Equal("'x' is not a valid null", Fails("n: null\n", Yaml, Value("x", Member("n"))).Message);
    }

    [Fact]
    public void A_typed_value_in_a_sequence_is_written_in_place()
    {
        string text = Edit("- 1\n- 2\n", Yaml, Value("7", Item(1)));

        Assert.Equal("- 1\n- 7\n", text);
    }

    [Fact]
    public void A_null_with_no_text_takes_a_string_after_its_colon()
    {
        string text = Edit("key:\nb: 1\n", Yaml, Value("text", Member("key")));

        Assert.Equal("key: text\nb: 1\n", text);
    }

    [Fact]
    public void A_null_with_no_text_stays_null_when_set_to_empty()
    {
        string text = Edit("key:\n", Yaml, Value(string.Empty, Member("key")));

        Assert.Equal("key:\n", text);
    }

    [Fact]
    public void A_missing_sequence_item_takes_its_text_after_the_dash()
    {
        string text = Edit("-\n- b\n", Yaml, Value("x", Item(0)));

        Assert.Equal("- x\n- b\n", text);
    }

    [Fact]
    public void A_value_written_on_the_line_after_its_key_moves_up_to_the_key_line()
    {
        string text = Edit("a:\n  foo\nb: 1\n", Yaml, Value("bar", Member("a")));

        Assert.Equal("a: bar\nb: 1\n", text);
    }

    [Fact]
    public void A_top_level_literal_whose_first_line_starts_with_a_space_is_double_quoted_so_it_reads_the_same()
    {
        string text = Edit("foo\n", Yaml, Value(" lead\nx"));

        Assert.Equal("\" lead\\nx\"\n", text);
    }

    [Fact]
    public void A_literal_in_a_sequence_item_with_a_leading_space_gets_an_indentation_indicator()
    {
        string text = Edit("- |\n  one\n", Yaml, Value(" lead\nx", Item(0)));

        Assert.Equal("- |2-\n   lead\n  x\n", text);
    }

    [Fact]
    public void A_root_scalar_takes_a_new_text_and_keeps_its_marker()
    {
        Assert.Equal("bar\n", Edit("foo\n", Yaml, Value("bar")));
        Assert.Equal("--- bar\n", Edit("--- foo\n", Yaml, Value("bar")));
    }

    // ----- Flow context -----

    [Fact]
    public void A_value_in_a_flow_mapping_is_written_in_flow_style()
    {
        string text = Edit("{a: foo, b: 1}\n", Yaml, Value("bar", Member("a")));

        Assert.Equal("{a: bar, b: 1}\n", text);
    }

    [Fact]
    public void A_value_with_a_comma_in_a_flow_mapping_is_single_quoted()
    {
        string text = Edit("{a: foo, b: 1}\n", Yaml, Value("x,y", Member("a")));

        Assert.Equal("{a: 'x,y', b: 1}\n", text);
    }

    [Fact]
    public void A_value_with_a_line_break_in_flow_is_double_quoted()
    {
        string text = Edit("{a: foo}\n", Yaml, Value("x\ny", Member("a")));

        Assert.Equal("{a: \"x\\ny\"}\n", text);
    }

    [Fact]
    public void A_flow_sequence_item_is_written_in_place()
    {
        string text = Edit("[1, foo]\n", Yaml, Value("bar", Item(1)));

        Assert.Equal("[1, bar]\n", text);
    }

    // ----- Properties stay -----

    [Fact]
    public void An_anchored_value_keeps_its_anchor()
    {
        string text = Edit("a: &x foo\n", Yaml, Value("bar", Member("a")));

        Assert.Equal("a: &x bar\n", text);
    }

    [Fact]
    public void A_null_with_an_anchor_takes_its_text_after_the_anchor()
    {
        string text = Edit("a: &x\n", Yaml, Value("t", Member("a")));

        Assert.Equal("a: &x t\n", text);
    }

    [Fact]
    public void A_tagged_string_keeps_its_tag_and_quotes_a_number_word()
    {
        string text = Edit("a: !!str 5\n", Yaml, Value("6", Member("a")));

        Assert.Equal("a: !!str '6'\n", text);
    }

    [Fact]
    public void A_core_integer_tag_takes_an_integer_and_refuses_text()
    {
        Assert.Equal("a: !!int 7\n", Edit("a: !!int 5\n", Yaml, Value("7", Member("a"))));
        Assert.Equal("'x' is not a valid integer", Fails("a: !!int 5\n", Yaml, Value("x", Member("a"))).Message);
    }

    // ----- Keys -----

    [Fact]
    public void A_key_is_renamed_plain_when_it_is_safe()
    {
        string text = Edit("a: 1\nb: 2\n", Yaml, new SetPart(Path(Member("a")), PartKind.Key, -1, "c"));

        Assert.Equal("c: 1\nb: 2\n", text);
    }

    [Fact]
    public void A_key_that_needs_quotes_is_double_quoted()
    {
        string text = Edit("a: 1\n", Yaml, new SetPart(Path(Member("a")), PartKind.Key, -1, "x: y"));

        Assert.Equal("\"x: y\": 1\n", text);
    }

    [Fact]
    public void A_key_that_reads_as_a_number_is_double_quoted()
    {
        string text = Edit("a: 1\n", Yaml, new SetPart(Path(Member("a")), PartKind.Key, -1, "1"));

        Assert.Equal("\"1\": 1\n", text);
    }

    [Fact]
    public void A_quoted_key_is_renamed_to_a_plain_key()
    {
        string text = Edit("\"a b\": 1\n", Yaml, new SetPart(Path(Member("a b")), PartKind.Key, -1, "c"));

        Assert.Equal("c: 1\n", text);
    }

    [Fact]
    public void A_key_keeps_its_anchor_when_it_is_renamed()
    {
        string text = Edit("&k a: 1\n", Yaml, new SetPart(Path(Member("a")), PartKind.Key, -1, "b"));

        Assert.Equal("&k b: 1\n", text);
    }

    [Fact]
    public void A_key_renamed_onto_a_sibling_is_refused()
    {
        CfgeditException ex = Fails("a: 1\nb: 2\n", Yaml, new SetPart(Path(Member("a")), PartKind.Key, -1, "b"));

        Assert.Equal("a key named 'b' already exists here", ex.Message);
    }

    [Fact]
    public void A_flow_key_is_renamed_in_flow_style()
    {
        string text = Edit("{a: 1}\n", Yaml, new SetPart(Path(Member("a")), PartKind.Key, -1, "b"));

        Assert.Equal("{b: 1}\n", text);
    }

    // ----- Comments -----

    [Fact]
    public void A_comment_above_an_entry_takes_new_text()
    {
        string text = Edit("# old\na: 1\n", Yaml, new SetPart(Path(Comment(0)), PartKind.Comment, -1, " new"));

        Assert.Equal("# new\na: 1\n", text);
    }

    [Fact]
    public void A_trailing_comment_takes_new_text_and_keeps_its_place()
    {
        string text = Edit("a: 1 # c\n", Yaml, new SetPart(Path(Comment(0)), PartKind.Comment, -1, "new"));

        Assert.Equal("a: 1 #new\n", text);
    }

    [Fact]
    public void A_comment_text_with_a_line_break_is_refused()
    {
        CfgeditException ex = Fails("# c\na: 1\n", Yaml, new SetPart(Path(Comment(0)), PartKind.Comment, -1, "x\ny"));

        Assert.Equal("a comment cannot contain a line break", ex.Message);
    }

    [Fact]
    public void After_a_value_edit_the_same_node_takes_the_focus()
    {
        var (_, focus) = EditWithFocus("a: foo\n", Yaml, Value("bar", Member("a")));

        Assert.Equal(Path(Member("a")), focus);
    }

    [Fact]
    public void After_a_rename_the_renamed_node_takes_the_focus()
    {
        var (_, focus) = EditWithFocus("a: 1\n", Yaml, new SetPart(Path(Member("a")), PartKind.Key, -1, "c"));

        Assert.Equal(Path(Member("c")), focus);
    }

    [Fact]
    public void A_mapping_cannot_be_set_as_a_value()
    {
        CfgeditException ex = Fails("a:\n  b: 1\n", Yaml, Value("x", Member("a")));

        Assert.Equal("this part cannot be edited", ex.Message);
    }
}
