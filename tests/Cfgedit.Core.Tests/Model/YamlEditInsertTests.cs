using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// Insert on YAML: entries and items of block and flow collections, every value kind, literal blocks, the conversion of an empty flow collection
/// to block style, the first entry of an empty file, comments, and the refusals. Every expected text is written out by hand.
/// </summary>
public sealed class YamlEditInsertTests
{
    private static readonly IConfigFormat Yaml = EditTestSupport.Yaml;

    private static NodePath Path(params PathStep[] steps) => At(new PathStep[] { Item(0) }.Concat(steps).ToArray());

    private static EditOp Value(NodePath parent, int index, string? key, YamlNewKind kind, string? value = null) =>
        new InsertNode(parent, index, new NewYamlValue(key, kind, value));

    private static EditOp Comment(NodePath parent, int index, string text) => new InsertNode(parent, index, new NewYamlComment(text));

    // ----- Block mappings -----

    [Fact]
    public void A_key_is_appended_after_the_last_entry()
    {
        Assert.Equal("a: 1\nb: 2\nc: 3\n", Edit("a: 1\nb: 2\n", Yaml, Value(Path(), 2, "c", YamlNewKind.Number, "3")));
    }

    [Fact]
    public void A_key_is_inserted_before_the_entry_at_its_index()
    {
        Assert.Equal("a: 1\nm: x\nb: 2\n", Edit("a: 1\nb: 2\n", Yaml, Value(Path(), 1, "m", YamlNewKind.String, "x")));
        Assert.Equal("z: false\na: 1\nb: 2\n", Edit("a: 1\nb: 2\n", Yaml, Value(Path(), 0, "z", YamlNewKind.Boolean)));
    }

    [Fact]
    public void A_key_goes_before_the_nested_group_it_precedes_and_after_the_group_it_follows()
    {
        const string Text = "a:\n  x: 1\n  y: 2\nb: 3\n";

        Assert.Equal("a:\n  x: 1\n  y: 2\nn: null\nb: 3\n", Edit(Text, Yaml, Value(Path(), 1, "n", YamlNewKind.Null)));
    }

    [Fact]
    public void A_key_inserted_into_a_nested_mapping_takes_its_indentation()
    {
        const string Text = "a:\n  x: 1\n  y: 2\nb: 3\n";

        Assert.Equal("a:\n  x: 1\n  m: x\n  y: 2\nb: 3\n", Edit(Text, Yaml, Value(Path(Member("a")), 1, "m", YamlNewKind.String, "x")));
        Assert.Equal("a:\n  x: 1\n  y: 2\n  m: x\nb: 3\n", Edit(Text, Yaml, Value(Path(Member("a")), 2, "m", YamlNewKind.String, "x")));
    }

    [Fact]
    public void A_key_takes_the_line_after_a_trailing_comment_when_it_follows_that_entry()
    {
        Assert.Equal("a: 1 # one\nm: x\nb: 2\n", Edit("a: 1 # one\nb: 2\n", Yaml, Value(Path(), 2, "m", YamlNewKind.String, "x")));
    }

    [Fact]
    public void A_key_goes_before_the_leading_comment_of_the_entry_at_its_index()
    {
        Assert.Equal("a: 1\nm: x\n# about b\nb: 2\n", Edit("a: 1\n# about b\nb: 2\n", Yaml, Value(Path(), 1, "m", YamlNewKind.String, "x")));
    }

    [Fact]
    public void The_first_entry_of_a_mapping_that_shares_its_line_with_a_dash_takes_the_line_and_the_entry_follows_it()
    {
        Assert.Equal("- z: 0\n  a: 1\n  b: 2\n", Edit("- a: 1\n  b: 2\n", Yaml, Value(Path(Item(0)), 0, "z", YamlNewKind.Number, "0")));
    }

    [Fact]
    public void A_key_takes_the_indentation_of_a_mapping_in_a_sequence_item()
    {
        Assert.Equal("- a: 1\n  b: 2\n  c: 3\n", Edit("- a: 1\n  b: 2\n", Yaml, Value(Path(Item(0)), 2, "c", YamlNewKind.Number, "3")));
    }

    // ----- Values of each kind -----

    [Fact]
    public void A_string_that_reads_as_another_kind_is_quoted()
    {
        Assert.Equal("a: 1\nk: 'true'\n", Edit("a: 1\n", Yaml, Value(Path(), 1, "k", YamlNewKind.String, "true")));
        Assert.Equal("a: 1\nk: '1.5'\n", Edit("a: 1\n", Yaml, Value(Path(), 1, "k", YamlNewKind.String, "1.5")));
        Assert.Equal("a: 1\nk: 'null'\n", Edit("a: 1\n", Yaml, Value(Path(), 1, "k", YamlNewKind.String, "null")));
    }

    [Fact]
    public void A_plain_string_is_written_plain_and_an_empty_string_is_quoted()
    {
        Assert.Equal("a: 1\nk: hello world\n", Edit("a: 1\n", Yaml, Value(Path(), 1, "k", YamlNewKind.String, "hello world")));
        Assert.Equal("a: 1\nk: ''\n", Edit("a: 1\n", Yaml, Value(Path(), 1, "k", YamlNewKind.String)));
    }

    [Fact]
    public void A_string_with_a_colon_and_space_is_single_quoted()
    {
        Assert.Equal("a: 1\nk: 'x: y'\n", Edit("a: 1\n", Yaml, Value(Path(), 1, "k", YamlNewKind.String, "x: y")));
    }

    [Fact]
    public void A_multi_line_string_is_a_literal_block_indented_under_its_key()
    {
        Assert.Equal("a: 1\nk: |-\n  one\n  two\n", Edit("a: 1\n", Yaml, Value(Path(), 1, "k", YamlNewKind.String, "one\ntwo")));
    }

    [Fact]
    public void A_number_is_written_as_given_and_must_be_a_number()
    {
        Assert.Equal("a: 1\nk: 0x1F\n", Edit("a: 1\n", Yaml, Value(Path(), 1, "k", YamlNewKind.Number, "0x1F")));
        Assert.Equal("a: 1\nk: 0\n", Edit("a: 1\n", Yaml, Value(Path(), 1, "k", YamlNewKind.Number)));

        CfgeditException ex = Fails("a: 1\n", Yaml, Value(Path(), 1, "k", YamlNewKind.Number, "1_000"));
        Assert.Equal("'1_000' is not a valid number", ex.Message);
    }

    [Fact]
    public void A_boolean_is_true_or_false_and_must_be_a_boolean()
    {
        Assert.Equal("a: 1\nk: true\n", Edit("a: 1\n", Yaml, Value(Path(), 1, "k", YamlNewKind.Boolean, "true")));

        CfgeditException ex = Fails("a: 1\n", Yaml, Value(Path(), 1, "k", YamlNewKind.Boolean, "yes"));
        Assert.Equal("'yes' is not a valid boolean", ex.Message);
    }

    [Fact]
    public void A_null_is_written_as_null()
    {
        Assert.Equal("a: 1\nn: null\n", Edit("a: 1\n", Yaml, Value(Path(), 1, "n", YamlNewKind.Null)));
    }

    [Fact]
    public void A_new_mapping_and_sequence_are_written_as_empty_flow_collections()
    {
        Assert.Equal("a: 1\nm: {}\n", Edit("a: 1\n", Yaml, Value(Path(), 1, "m", YamlNewKind.Mapping)));
        Assert.Equal("a: 1\ns: []\n", Edit("a: 1\n", Yaml, Value(Path(), 1, "s", YamlNewKind.Sequence)));
    }

    // ----- Block sequences -----

    [Fact]
    public void An_item_of_an_indented_sequence_is_inserted_at_the_dash_column()
    {
        Assert.Equal("items:\n  - a\n  - x\n  - b\n", Edit("items:\n  - a\n  - b\n", Yaml, Value(Path(Member("items")), 1, null, YamlNewKind.String, "x")));
    }

    [Fact]
    public void An_item_of_a_sequence_that_is_not_indented_under_its_key_is_not_indented()
    {
        Assert.Equal("items:\n- a\n- b\n- x\n", Edit("items:\n- a\n- b\n", Yaml, Value(Path(Member("items")), 2, null, YamlNewKind.String, "x")));
    }

    [Fact]
    public void An_item_of_a_root_sequence_goes_before_the_first_item()
    {
        Assert.Equal("- 1\n- a\n- b\n", Edit("- a\n- b\n", Yaml, Value(Path(), 0, null, YamlNewKind.Number, "1")));
    }

    [Fact]
    public void A_sequence_item_with_a_key_is_refused_and_a_mapping_entry_without_one_too()
    {
        Assert.Equal("an item of a sequence has no key", Fails("- a\n", Yaml, Value(Path(), 1, "k", YamlNewKind.String, "x")).Message);
        Assert.Equal("a key is needed for an entry of a mapping", Fails("a: 1\n", Yaml, Value(Path(), 1, null, YamlNewKind.String, "x")).Message);
    }

    [Fact]
    public void The_first_item_of_a_sequence_that_shares_its_line_with_a_dash_takes_the_line()
    {
        Assert.Equal("- - x\n  - a\n  - b\n", Edit("- - a\n  - b\n", Yaml, Value(Path(Item(0)), 0, null, YamlNewKind.String, "x")));
    }

    // ----- Flow collections -----

    [Fact]
    public void An_entry_of_a_one_line_flow_mapping_is_inserted_with_a_comma_and_the_spacing_of_the_file()
    {
        Assert.Equal("{a: 1, b: 2}\n", Edit("{a: 1}\n", Yaml, Value(Path(), 1, "b", YamlNewKind.Number, "2")));
        Assert.Equal("{b: 2, a: 1}\n", Edit("{a: 1}\n", Yaml, Value(Path(), 0, "b", YamlNewKind.Number, "2")));
    }

    [Fact]
    public void An_item_of_a_one_line_flow_sequence_is_inserted_at_its_index()
    {
        Assert.Equal("[1, 2, x]\n", Edit("[1, 2]\n", Yaml, Value(Path(), 2, null, YamlNewKind.String, "x")));
        Assert.Equal("[1, x, 2]\n", Edit("[1, 2]\n", Yaml, Value(Path(), 1, null, YamlNewKind.String, "x")));
    }

    [Fact]
    public void An_item_of_a_one_per_line_flow_sequence_gets_its_own_line_and_the_comma_of_the_last_item()
    {
        Assert.Equal("[\n  1,\n  2,\n  3\n]\n", Edit("[\n  1,\n  2\n]\n", Yaml, Value(Path(), 2, null, YamlNewKind.Number, "3")));
    }

    [Fact]
    public void A_trailing_comma_of_a_one_per_line_flow_sequence_is_kept_on_the_new_item()
    {
        Assert.Equal("[\n  1,\n  2,\n  3,\n]\n", Edit("[\n  1,\n  2,\n]\n", Yaml, Value(Path(), 2, null, YamlNewKind.Number, "3")));
    }

    [Fact]
    public void An_entry_of_a_flow_collection_inside_a_flow_collection_stays_flow()
    {
        Assert.Equal("{a: [1, 2]}\n", Edit("{a: [1]}\n", Yaml, Value(Path(Member("a")), 1, null, YamlNewKind.Number, "2")));
        Assert.Equal("{a: {k: v}}\n", Edit("{a: {}}\n", Yaml, Value(Path(Member("a")), 0, "k", YamlNewKind.String, "v")));
    }

    [Fact]
    public void A_duplicate_key_in_a_flow_mapping_is_refused()
    {
        Assert.Equal("a key named 'a' already exists here", Fails("{a: 1}\n", Yaml, Value(Path(), 1, "a", YamlNewKind.Number, "2")).Message);
    }

    // ----- Empty collections become block style -----

    [Fact]
    public void An_empty_flow_mapping_under_a_two_space_key_becomes_block_with_two_spaces()
    {
        Assert.Equal("key:\n  x: 1\nb: 2\n", Edit("key: {}\nb: 2\n", Yaml, Value(Path(Member("key")), 0, "x", YamlNewKind.Number, "1")));
    }

    [Fact]
    public void An_empty_flow_mapping_under_a_four_space_key_becomes_block_with_the_files_step()
    {
        Assert.Equal("key:\n    x: 1\nother:\n    a: 1\n", Edit("key: {}\nother:\n    a: 1\n", Yaml, Value(Path(Member("key")), 0, "x", YamlNewKind.Number, "1")));
    }

    [Fact]
    public void An_empty_flow_sequence_under_a_key_becomes_an_indented_block_sequence_when_the_file_indents_them()
    {
        Assert.Equal("other:\n  - x\nkey:\n  - a\n", Edit("other:\n  - x\nkey: []\n", Yaml, Value(Path(Member("key")), 0, null, YamlNewKind.String, "a")));
    }

    [Fact]
    public void An_empty_flow_sequence_under_a_key_becomes_a_block_sequence_at_the_key_column_when_the_file_does_not_indent_them()
    {
        Assert.Equal("other:\n- x\nkey:\n- a\n", Edit("other:\n- x\nkey: []\n", Yaml, Value(Path(Member("key")), 0, null, YamlNewKind.String, "a")));
    }

    [Fact]
    public void An_empty_flow_sequence_in_a_sequence_item_becomes_block_one_level_deeper_than_its_dash()
    {
        Assert.Equal("-\n  - a\n", Edit("- []\n", Yaml, Value(Path(Item(0)), 0, null, YamlNewKind.String, "a")));
    }

    [Fact]
    public void An_empty_flow_mapping_in_a_sequence_item_becomes_a_compact_mapping_on_the_dash_line()
    {
        Assert.Equal("- k: v\n", Edit("- {}\n", Yaml, Value(Path(Item(0)), 0, "k", YamlNewKind.String, "v")));
        Assert.Equal("- k: v\n- x\n", Edit("- {}\n- x\n", Yaml, Value(Path(Item(0)), 0, "k", YamlNewKind.String, "v")));
    }

    [Fact]
    public void Later_members_of_a_compact_mapping_item_align_with_its_first_key()
    {
        string first = Edit("- {}\n", Yaml, Value(Path(Item(0)), 0, "name", YamlNewKind.String));

        Assert.Equal("- name: ''\n", first);
        Assert.Equal("- name: ''\n  image: ''\n", Edit(first, Yaml, Value(Path(Item(0)), 1, "image", YamlNewKind.String)));
    }

    [Fact]
    public void An_entry_added_to_an_existing_compact_item_goes_at_the_column_of_its_first_key()
    {
        Assert.Equal("- a: 1\n  b: 2\n", Edit("- a: 1\n", Yaml, Value(Path(Item(0)), 1, "b", YamlNewKind.Number, "2")));
    }

    [Fact]
    public void An_empty_flow_root_becomes_a_block_mapping_or_sequence()
    {
        Assert.Equal("a: 1\n", Edit("{}\n", Yaml, Value(Path(), 0, "a", YamlNewKind.Number, "1")));
        Assert.Equal("- x\n", Edit("[]\n", Yaml, Value(Path(), 0, null, YamlNewKind.String, "x")));
    }

    [Fact]
    public void An_empty_flow_root_after_a_marker_moves_to_its_own_line()
    {
        Assert.Equal("---\na: 1\n", Edit("--- {}\n", Yaml, Value(Path(), 0, "a", YamlNewKind.Number, "1")));
    }

    [Fact]
    public void An_empty_flow_under_a_key_on_a_later_line_is_not_converted()
    {
        Assert.Equal("key:\n  {x: 1}\n", Edit("key:\n  {}\n", Yaml, Value(Path(Member("key")), 0, "x", YamlNewKind.Number, "1")));
    }

    // ----- Empty file -----

    [Fact]
    public void The_first_entry_of_an_empty_file_is_a_key_and_a_newline()
    {
        Assert.Equal("a: x\n", Edit(string.Empty, Yaml, Value(Path(), 0, "a", YamlNewKind.String, "x")));
    }

    // ----- Comments -----

    [Fact]
    public void A_comment_line_is_written_as_the_hash_and_its_text_at_the_sibling_indentation()
    {
        Assert.Equal("a: 1\n#x\nb: 2\n", Edit("a: 1\nb: 2\n", Yaml, Comment(Path(), 1, "x")));
        Assert.Equal("a:\n  x: 1\n  # inside\nb: 2\n", Edit("a:\n  x: 1\nb: 2\n", Yaml, Comment(Path(Member("a")), 1, " inside")));
    }

    [Fact]
    public void A_comment_at_the_end_of_a_block_collection_is_written_after_its_last_entry()
    {
        Assert.Equal("a: 1\nb: 2\n# end\n", Edit("a: 1\nb: 2\n", Yaml, Comment(Path(), 2, " end")));
    }

    [Fact]
    public void A_comment_inside_a_one_per_line_flow_sequence_gets_a_line_before_the_item_at_its_index()
    {
        Assert.Equal("[\n  1,\n  #c\n  2\n]\n", Edit("[\n  1,\n  2\n]\n", Yaml, Comment(Path(), 1, "c")));
    }

    [Fact]
    public void A_comment_in_a_one_line_flow_collection_is_refused()
    {
        Assert.Equal(
            "a comment in a flow collection needs a line of its own here",
            Fails("[1, 2]\n", Yaml, Comment(Path(), 1, "c")).Message);
        Assert.Equal(
            "a comment in a flow collection needs a line of its own here",
            Fails("{}\n", Yaml, Comment(Path(), 0, "c")).Message);
    }

    // ----- Line layout -----

    [Fact]
    public void An_entry_added_after_a_last_line_with_no_final_break_gets_the_break_first_and_keeps_none()
    {
        Assert.Equal("a: 1\nb: 2", Edit("a: 1", Yaml, Value(Path(), 1, "b", YamlNewKind.Number, "2")));
    }

    [Fact]
    public void An_entry_added_to_a_CRLF_document_ends_with_its_break()
    {
        Assert.Equal("a: 1\r\nb: 2\r\nc: x\r\n", Edit("a: 1\r\nb: 2\r\n", Yaml, Value(Path(), 2, "c", YamlNewKind.String, "x")));
    }

    // ----- Refusals -----

    [Fact]
    public void Adding_a_document_is_refused()
    {
        CfgeditException ex = Fails("a: 1\n---\nb: 2\n", Yaml, Value(At(), 2, null, YamlNewKind.Mapping));

        Assert.Equal("adding a document is not supported", ex.Message);
    }

    [Fact]
    public void A_value_has_no_children_to_insert_into()
    {
        Assert.Equal("a YAML value has no children", Fails("a: 1\n", Yaml, Value(Path(Member("a")), 0, "k", YamlNewKind.String, "x")).Message);
    }

    [Fact]
    public void An_index_outside_the_children_is_refused()
    {
        Assert.Equal("index 3 is outside the children, 0 to 2", Fails("a: 1\nb: 2\n", Yaml, Value(Path(), 3, "c", YamlNewKind.Null)).Message);
    }

    // ----- Anchors, tags and literal blocks -----

    [Fact]
    public void An_anchor_on_an_empty_flow_mapping_stays_and_the_block_goes_below_its_key()
    {
        Assert.Equal("key: &x\n  a: 1\n", Edit("key: &x {}\n", Yaml, Value(Path(Member("key")), 0, "a", YamlNewKind.Number, "1")));
    }

    [Fact]
    public void A_literal_block_in_a_sequence_is_indented_under_its_dash()
    {
        Assert.Equal("- x\n- |-\n  a\n  b\n", Edit("- x\n", Yaml, Value(Path(), 1, null, YamlNewKind.String, "a\nb")));
    }

    // ----- Focus -----

    [Fact]
    public void After_an_insert_the_new_entry_takes_the_focus()
    {
        var (_, focus) = EditWithFocus("a: 1\nb: 2\n", Yaml, Value(Path(), 1, "m", YamlNewKind.Number, "3"));

        Assert.Equal(Path(Member("m")), focus);
    }

    [Fact]
    public void After_an_insert_into_a_sequence_the_new_item_takes_the_focus()
    {
        var (_, focus) = EditWithFocus("- a\n- b\n", Yaml, Value(Path(), 1, null, YamlNewKind.String, "x"));

        Assert.Equal(Path(Item(1)), focus);
    }

    [Fact]
    public void After_a_comment_is_inserted_the_comment_takes_the_focus()
    {
        var (_, focus) = EditWithFocus("a: 1\nb: 2\n", Yaml, Comment(Path(), 2, " end"));

        Assert.Equal(Path(EditTestSupport.Comment(0)), focus);
    }
}
