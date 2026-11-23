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
    public void A_scalar_becomes_an_empty_mapping_or_sequence()
    {
        Assert.Equal("a: {}\n", Edit("a: foo\n", Yaml, Change(YamlNewKind.Mapping, null, Member("a"))));
        Assert.Equal("a: []\n", Edit("a: foo\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
    }

    [Fact]
    public void A_null_key_becomes_an_empty_mapping_after_its_colon()
    {
        Assert.Equal("a: {}\nb: 1\n", Edit("a:\nb: 1\n", Yaml, Change(YamlNewKind.Mapping, null, Member("a"))));
    }

    [Fact]
    public void A_block_mapping_becomes_an_empty_sequence_on_its_key_line_and_drops_its_lines()
    {
        Assert.Equal("a: []\nb: 3\n", Edit("a:\n  x: 1\n  y: 2\nb: 3\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
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
        Assert.Equal("a: [] # tail\n", Edit("a:\n  x: 1 # tail\n", Yaml, Change(YamlNewKind.Sequence, null, Member("a"))));
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
    public void An_alias_cannot_change_type()
    {
        CfgeditException ex = Fails("a: &x 1\nb: *x\n", Yaml, Change(YamlNewKind.String, "y", Member("b")));

        Assert.Equal("an alias cannot change type", ex.Message);
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
