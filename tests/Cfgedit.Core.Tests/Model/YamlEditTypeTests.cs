using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>ChangeYamlType on YAML: scalars by kind, block and flow collections to {} and [], tags that stop fitting, and aliases.</summary>
public sealed class YamlEditTypeTests
{
    private static readonly IConfigFormat Yaml = EditTestSupport.Yaml;

    private static NodePath Path(params PathStep[] steps) => At(new PathStep[] { Item(0) }.Concat(steps).ToArray());

    private static ChangeYamlType Change(YamlNewKind kind, string? value, params PathStep[] steps) => new(Path(steps), kind, value);

    // ----- Scalars -----

    [Fact]
    public void A_string_becomes_a_number_and_a_null_number_is_zero()
    {
        Assert.Equal("a: 5\n", Edit("a: foo\n", Yaml, Change(YamlNewKind.Number, "5", Member("a"))));
        Assert.Equal("a: 0\n", Edit("a: foo\n", Yaml, Change(YamlNewKind.Number, null, Member("a"))));
    }

    [Fact]
    public void A_number_must_read_as_a_number()
    {
        Assert.Equal("'x' is not a valid number", Fails("a: foo\n", Yaml, Change(YamlNewKind.Number, "x", Member("a"))).Message);
    }

    [Fact]
    public void A_number_becomes_a_string_quoted_when_it_reads_as_a_number()
    {
        Assert.Equal("a: ''\n", Edit("a: 5\n", Yaml, Change(YamlNewKind.String, null, Member("a"))));
        Assert.Equal("a: '5'\n", Edit("a: 5\n", Yaml, Change(YamlNewKind.String, "5", Member("a"))));
        Assert.Equal("a: hi\n", Edit("a: 5\n", Yaml, Change(YamlNewKind.String, "hi", Member("a"))));
    }

    [Fact]
    public void A_string_becomes_a_boolean_and_a_null_boolean_is_false()
    {
        Assert.Equal("a: false\n", Edit("a: foo\n", Yaml, Change(YamlNewKind.Boolean, null, Member("a"))));
        Assert.Equal("a: true\n", Edit("a: foo\n", Yaml, Change(YamlNewKind.Boolean, "true", Member("a"))));
    }

    [Fact]
    public void A_string_becomes_null_written_as_the_word()
    {
        Assert.Equal("a: null\n", Edit("a: foo\n", Yaml, Change(YamlNewKind.Null, null, Member("a"))));
        Assert.Equal("a: null\n", Edit("a: 5\n", Yaml, Change(YamlNewKind.Null, "ignored", Member("a"))));
    }

    [Fact]
    public void A_null_stays_null_when_its_type_is_chosen_again()
    {
        Assert.Equal("a: null\n", Edit("a: null\n", Yaml, Change(YamlNewKind.Null, null, Member("a"))));
        Assert.Equal("a:\n", Edit("a:\n", Yaml, Change(YamlNewKind.Null, null, Member("a"))));
    }

    [Fact]
    public void A_string_with_its_own_type_and_no_text_is_unchanged()
    {
        Assert.Equal("a: 'x'\n", Edit("a: 'x'\n", Yaml, Change(YamlNewKind.String, null, Member("a"))));
    }

    [Fact]
    public void A_null_with_no_text_becomes_an_empty_string_when_a_string_is_chosen()
    {
        Assert.Equal("a: ''\n", Edit("a:\n", Yaml, Change(YamlNewKind.String, null, Member("a"))));
    }

    [Fact]
    public void A_scalar_in_a_flow_mapping_becomes_a_flow_mapping()
    {
        Assert.Equal("{a: {}, b: 1}\n", Edit("{a: 1, b: 1}\n", Yaml, Change(YamlNewKind.Mapping, null, Member("a"))));
    }

    // ----- Containers -----

    [Fact]
    public void A_scalar_becomes_an_empty_mapping_and_a_sequence_wraps_it()
    {
        Assert.Equal("a: {}\n", Edit("a: foo\n", Yaml, Change(YamlNewKind.Mapping, null, Member("a"))));
        Assert.Equal("a:\n  - foo\n", Edit("a: foo\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void A_null_key_becomes_an_empty_mapping_after_its_colon()
    {
        Assert.Equal("a: {}\nb: 1\n", Edit("a:\nb: 1\n", Yaml, Change(YamlNewKind.Mapping, null, Member("a"))));
    }

    [Fact]
    public void A_block_mapping_is_wrapped_in_a_sequence_and_its_entries_are_kept()
    {
        Assert.Equal("a:\n  - x: 1\n    y: 2\nb: 3\n", Edit("a:\n  x: 1\n  y: 2\nb: 3\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void A_block_sequence_becomes_an_empty_mapping_and_keeps_its_anchor()
    {
        Assert.Equal("a: &x {}\nb: 3\n", Edit("a: &x\n  - 1\nb: 3\n", Yaml, Change(YamlNewKind.Mapping, null, Member("a"))));
    }

    [Fact]
    public void A_block_scalar_becomes_a_flow_mapping_and_its_lines_go()
    {
        Assert.Equal("a: {}\nb: 1\n", Edit("a: |\n  x\nb: 1\n", Yaml, Change(YamlNewKind.Mapping, null, Member("a"))));
    }

    [Fact]
    public void A_root_scalar_becomes_a_root_mapping_and_a_root_mapping_becomes_a_string()
    {
        Assert.Equal("{}\n", Edit("foo\n", Yaml, Change(YamlNewKind.Mapping, null)));
        Assert.Equal("x\n", Edit("a: 1\n", Yaml, Change(YamlNewKind.String, "x")));
    }

    [Fact]
    public void A_collection_of_its_own_kind_is_unchanged()
    {
        Assert.Equal("a:\n  x: 1\n", Edit("a:\n  x: 1\n", Yaml, Change(YamlNewKind.Mapping, null, Member("a"))));
        Assert.Equal("{a: [1]}\n", Edit("{a: [1]}\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void A_block_collection_keeps_the_comment_after_its_last_entry()
    {
        Assert.Equal("a:\n  - x: 1 # tail\n", Edit("a:\n  x: 1 # tail\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    // ----- Wrapping in a sequence -----

    [Fact]
    public void A_scalar_in_a_flow_sequence_is_wrapped_in_brackets()
    {
        Assert.Equal("[[a], b]\n", Edit("[a, b]\n", Yaml, Change(YamlNewKind.Sequence, null, Item(0))));
    }

    [Fact]
    public void A_flow_mapping_value_is_wrapped_in_brackets()
    {
        Assert.Equal("a: [{x: 1}]\n", Edit("a: {x: 1}\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void A_scalar_in_a_flow_mapping_is_wrapped_in_brackets()
    {
        Assert.Equal("{a: [1]}\n", Edit("{a: 1}\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void A_scalar_with_a_trailing_comment_keeps_the_comment_on_the_item()
    {
        Assert.Equal("a:\n  - foo # c\n", Edit("a: foo # c\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void A_scalar_on_the_line_after_its_key_is_wrapped_on_that_line()
    {
        Assert.Equal("a:\n  - foo\nb: 1\n", Edit("a:\n  foo\nb: 1\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void A_multi_line_plain_scalar_keeps_its_continuation_under_the_item()
    {
        Assert.Equal("a:\n  - foo\n    bar\nb: 1\n", Edit("a: foo\n  bar\nb: 1\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void A_literal_block_scalar_keeps_its_value_and_moves_under_the_item()
    {
        Assert.Equal("a:\n  - |\n    x\n    y\nb: 1\n", Edit("a: |\n  x\n  y\nb: 1\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void A_block_scalar_with_an_explicit_indentation_indicator_is_refused()
    {
        CfgeditException ex = Fails("a: >2\n   x\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a")));

        Assert.Equal("a block scalar with an explicit indentation indicator cannot be wrapped in a sequence yet", ex.Message);
    }

    [Fact]
    public void A_block_mapping_keeps_a_comment_between_its_entries_with_the_entries()
    {
        Assert.Equal("a:\n  - x: 1\n    # note\n    y: 2\n", Edit("a:\n  x: 1\n  # note\n  y: 2\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void A_sequence_under_a_key_with_unindented_sequences_is_wrapped_at_the_key_column()
    {
        Assert.Equal("k:\n- 1\na:\n- foo\n", Edit("k:\n- 1\na: foo\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void A_nested_sequence_item_is_wrapped_under_its_dash()
    {
        Assert.Equal("a:\n  - - x\n  - y\n", Edit("a:\n  - x\n  - y\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"), Item(0))));
    }

    [Fact]
    public void A_block_sequence_is_a_sequence_already_and_is_unchanged()
    {
        Assert.Equal("a:\n  - x\n", Edit("a:\n  - x\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void An_anchored_and_tagged_scalar_keeps_them_on_the_item()
    {
        Assert.Equal("a:\n  - &x !!str foo\n", Edit("a: &x !!str foo\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void An_alias_is_wrapped_in_a_sequence()
    {
        Assert.Equal("a: &x 1\nb:\n  - *x\n", Edit("a: &x 1\nb: *x\n", Yaml, Change(YamlNewKind.Sequence, null, Member("b"))));
    }

    [Fact]
    public void An_alias_changed_to_a_mapping_is_still_refused()
    {
        CfgeditException ex = Fails("a: &x 1\nb: *x\n", Yaml, Change(YamlNewKind.Mapping, null, Member("b")));

        Assert.Equal("an alias can only be wrapped in a sequence", ex.Message);
    }

    [Fact]
    public void A_root_scalar_becomes_a_root_sequence_item()
    {
        Assert.Equal("- foo\n", Edit("foo\n", Yaml, Change(YamlNewKind.Sequence, null)));
    }

    [Fact]
    public void A_root_block_mapping_is_wrapped_and_its_entries_move_under_the_dash()
    {
        Assert.Equal("- a: 1\n  b: 2\n", Edit("a: 1\nb: 2\n", Yaml, Change(YamlNewKind.Sequence, null)));
    }

    [Fact]
    public void A_mapping_item_that_starts_on_its_dash_is_wrapped_as_a_nested_item()
    {
        Assert.Equal("- - a: 1\n    b: 2\n", Edit("- a: 1\n  b: 2\n", Yaml, Change(YamlNewKind.Sequence, null, Item(0))));
    }

    [Fact]
    public void A_block_mapping_with_an_anchor_on_its_key_line_keeps_the_anchor_on_the_item()
    {
        Assert.Equal("a:\n  - &x\n    b: 1\n", Edit("a: &x\n  b: 1\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void A_missing_value_becomes_an_empty_sequence()
    {
        Assert.Equal("a: []\nb: 1\n", Edit("a:\nb: 1\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void An_explicit_null_becomes_an_empty_sequence()
    {
        Assert.Equal("a: []\nb: 1\n", Edit("a: null\nb: 1\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void A_tilde_and_a_capitalised_null_become_an_empty_sequence()
    {
        Assert.Equal("a: []\n", Edit("a: ~\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
        Assert.Equal("a: []\n", Edit("a: Null\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void An_anchored_null_becomes_an_empty_sequence_and_keeps_its_anchor()
    {
        Assert.Equal("a: &x []\n", Edit("a: &x null\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void A_missing_item_becomes_an_empty_sequence()
    {
        Assert.Equal("- []\n- 1\n", Edit("-\n- 1\n", Yaml, Change(YamlNewKind.Sequence, null, Item(0))));
    }

    [Fact]
    public void Entries_indented_deeper_than_the_new_item_move_left_under_it()
    {
        // The file's unit is two (the first step it takes is two, and ties go to the first): the new dash goes two columns under the key,
        // and the entries move left to meet it.
        Assert.Equal("c:\n  z: 1\n  q: 2\na:\n  - x: 1\n    y: 2\n", Edit("c:\n  z: 1\n  q: 2\na:\n        x: 1\n        y: 2\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void A_comment_between_a_key_and_its_value_is_refused()
    {
        CfgeditException ex = Fails("a: # c\n  foo\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a")));

        Assert.Equal("a comment between a key and its value cannot be wrapped in a sequence yet", ex.Message);
    }

    [Fact]
    public void A_root_value_on_the_document_marker_line_is_refused()
    {
        CfgeditException ex = Fails("--- foo\n", Yaml, Change(YamlNewKind.Sequence, null));

        Assert.Equal("a root value on the document's marker line cannot be wrapped in a sequence yet", ex.Message);
    }

    // ----- Tags -----

    [Fact]
    public void A_core_tag_that_does_not_fit_the_new_type_is_removed()
    {
        Assert.Equal("a: 5\n", Edit("a: !!str foo\n", Yaml, Change(YamlNewKind.Number, "5", Member("a"))));
        Assert.Equal("a: {}\n", Edit("a: !!int 5\n", Yaml, Change(YamlNewKind.Mapping, null, Member("a"))));
    }

    [Fact]
    public void A_core_tag_that_fits_the_new_type_stays()
    {
        Assert.Equal("a: !!str bar\n", Edit("a: !!str foo\n", Yaml, Change(YamlNewKind.String, "bar", Member("a"))));
    }

    [Fact]
    public void A_non_core_tag_fits_only_a_string()
    {
        Assert.Equal("a: !x bar\n", Edit("a: !x foo\n", Yaml, Change(YamlNewKind.String, "bar", Member("a"))));
        Assert.Equal("a: 5\n", Edit("a: !x foo\n", Yaml, Change(YamlNewKind.Number, "5", Member("a"))));
    }

    [Fact]
    public void A_float_tag_takes_an_integer_text()
    {
        Assert.Equal("a: !!float 3\n", Edit("a: !!float 1.5\n", Yaml, Change(YamlNewKind.Number, "3", Member("a"))));
    }

    // ----- Refusals -----

    [Fact]
    public void An_alias_can_only_be_wrapped_in_a_sequence()
    {
        CfgeditException ex = Fails("a: &x 1\nb: *x\n", Yaml, Change(YamlNewKind.String, "y", Member("b")));

        Assert.Equal("an alias can only be wrapped in a sequence", ex.Message);
    }

    [Fact]
    public void A_comment_cannot_change_type()
    {
        CfgeditException ex = Fails("# c\na: 1\n", Yaml, Change(YamlNewKind.String, "y", Comment(0)));

        Assert.Equal("only a value has a type", ex.Message);
    }

    [Fact]
    public void A_json_or_toml_type_change_is_refused_for_yaml()
    {
        var json = new ChangeJsonType(Path(Member("a")), JsonValueKind.String, "x");
        var toml = new ChangeTomlType(Path(Member("a")), TomlNewKind.String, "x");

        Assert.Equal("use the YAML type change", Fails("a: 1\n", Yaml, json).Message);
        Assert.Equal("use the YAML type change", Fails("a: 1\n", Yaml, toml).Message);
    }
}
