using Cfgedit.Core;
using Cfgedit.Core.Json;
using Cfgedit.Core.Model;

namespace Cfgedit.Core.Tests.Model;

/// <summary>SetPart on JSON: keys, string and number values, comments, and the no-op and invalid cases.</summary>
public sealed class JsonEditSetPartTests
{
    private static readonly IConfigFormat Strict = EditTestSupport.Strict;
    private static readonly IConfigFormat Jsonc = EditTestSupport.Jsonc;
    private static readonly IConfigFormat Json5 = EditTestSupport.Json5;

    [Fact]
    public void Renaming_a_key_keeps_its_double_quotes()
    {
        string text = EditTestSupport.Edit(
            "{\"a\": 1}", Strict, new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("a")), PartKind.Key, -1, "b"));

        Assert.Equal("{\"b\": 1}", text);
    }

    [Fact]
    public void Renaming_a_nested_key_changes_only_that_key()
    {
        string text = EditTestSupport.Edit(
            "{\"a\": {\"b\": 1}}",
            Strict,
            new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("a"), EditTestSupport.Member("b")), PartKind.Key, -1, "c"));

        Assert.Equal("{\"a\": {\"c\": 1}}", text);
    }

    [Fact]
    public void A_bare_json5_key_stays_bare_when_the_name_is_an_identifier()
    {
        string text = EditTestSupport.Edit("{a: 1}", Json5, new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("a")), PartKind.Key, -1, "b"));

        Assert.Equal("{b: 1}", text);
    }

    [Fact]
    public void A_bare_json5_key_becomes_double_quoted_when_the_name_is_not_an_identifier()
    {
        string text = EditTestSupport.Edit("{a: 1}", Json5, new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("a")), PartKind.Key, -1, "b c"));

        Assert.Equal("{\"b c\": 1}", text);
    }

    [Fact]
    public void A_single_quoted_json5_key_keeps_its_quote()
    {
        string text = EditTestSupport.Edit("{'a': 1}", Json5, new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("a")), PartKind.Key, -1, "b"));

        Assert.Equal("{'b': 1}", text);
    }

    [Fact]
    public void Renaming_a_key_to_a_sibling_key_is_refused()
    {
        CfgeditException ex = EditTestSupport.Fails(
            "{\"a\": 1, \"b\": 2}", Strict, new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("a")), PartKind.Key, -1, "b"));

        Assert.Equal("a member named 'b' already exists", ex.Message);
    }

    [Fact]
    public void Renaming_a_key_to_its_own_name_returns_the_same_document()
    {
        ConfigDocument document = ModelTestSupport.Parse("{\"a\": 1}", Strict);

        EditResult result = document.Apply(new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("a")), PartKind.Key, -1, "a"));

        Assert.Same(document, result.Document);
    }

    [Fact]
    public void A_string_value_is_replaced_inside_its_double_quotes()
    {
        string text = EditTestSupport.Edit("{\"a\": \"x\"}", Strict, new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("a")), PartKind.Value, -1, "y"));

        Assert.Equal("{\"a\": \"y\"}", text);
    }

    [Fact]
    public void A_string_value_with_special_characters_is_escaped()
    {
        string text = EditTestSupport.Edit(
            "{\"a\": \"x\"}", Strict, new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("a")), PartKind.Value, -1, "a\nb\"c"));

        Assert.Equal("""{"a": "a\nb\"c"}""", text);
    }

    [Fact]
    public void Escapes_elsewhere_in_the_document_are_not_touched()
    {
        string text = EditTestSupport.Edit(
            "{\"a\": \"\\u00e9\", \"b\": \"x\"}", Strict, new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("b")), PartKind.Value, -1, "y"));

        Assert.Equal("""{"a": "\u00e9", "b": "y"}""", text);
    }

    [Fact]
    public void A_single_quoted_json5_value_keeps_its_quote_and_escapes_it()
    {
        string text = EditTestSupport.Edit("{a: 'x'}", Json5, new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("a")), PartKind.Value, -1, "it's"));

        Assert.Equal("""{a: 'it\'s'}""", text);
    }

    [Fact]
    public void An_unchanged_string_value_returns_the_same_document()
    {
        ConfigDocument document = ModelTestSupport.Parse("{\"a\": \"x\"}", Strict);

        EditResult result = document.Apply(new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("a")), PartKind.Value, -1, "x"));

        Assert.Same(document, result.Document);
    }

    [Fact]
    public void A_root_string_value_is_replaced()
    {
        string text = EditTestSupport.Edit("\"x\"", Strict, new SetPart(EditTestSupport.RootValue, PartKind.Value, -1, "y"));

        Assert.Equal("\"y\"", text);
    }

    [Fact]
    public void A_number_is_written_with_the_text_given()
    {
        string text = EditTestSupport.Edit("{\"a\": 1}", Strict, new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("a")), PartKind.Value, -1, "2.5e3"));

        Assert.Equal("{\"a\": 2.5e3}", text);
    }

    [Fact]
    public void An_invalid_strict_json_number_is_refused()
    {
        CfgeditException ex = EditTestSupport.Fails(
            "{\"a\": 1}", Strict, new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("a")), PartKind.Value, -1, "1."));

        Assert.Equal("'1.' is not a valid number", ex.Message);
    }

    [Fact]
    public void Json5_numbers_in_hexadecimal_and_with_a_leading_point_are_accepted()
    {
        string leadingPoint = EditTestSupport.Edit("{a: 1}", Json5, new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("a")), PartKind.Value, -1, ".5"));
        string hex = EditTestSupport.Edit("{a: 1}", Json5, new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("a")), PartKind.Value, -1, "0x1F"));

        Assert.Equal("{a: .5}", leadingPoint);
        Assert.Equal("{a: 0x1F}", hex);
    }

    [Fact]
    public void A_true_value_cannot_be_retyped_by_setting_its_text()
    {
        CfgeditException ex = EditTestSupport.Fails(
            "{\"a\": true}", Strict, new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("a")), PartKind.Value, -1, "x"));

        Assert.Equal("'true' cannot be changed to 'x'; change its type instead", ex.Message);
    }

    [Fact]
    public void A_line_comment_is_replaced_after_its_markers()
    {
        string text = EditTestSupport.Edit("// old\n{\"a\": 1}", Jsonc, new SetPart(EditTestSupport.At(EditTestSupport.Comment(0)), PartKind.Comment, -1, " new"));

        Assert.Equal("// new\n{\"a\": 1}", text);
    }

    [Fact]
    public void A_comment_inside_an_object_is_replaced_and_its_indentation_kept()
    {
        string text = EditTestSupport.Edit(
            "{\n  // old\n  \"a\": 1\n}",
            Jsonc,
            new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Comment(0)), PartKind.Comment, -1, " new"));

        Assert.Equal("{\n  // new\n  \"a\": 1\n}", text);
    }

    [Fact]
    public void A_block_comment_is_replaced_between_its_markers()
    {
        string text = EditTestSupport.Edit("/* old */ {}", Jsonc, new SetPart(EditTestSupport.At(EditTestSupport.Comment(0)), PartKind.Comment, -1, " new "));

        Assert.Equal("/* new */ {}", text);
    }

    [Fact]
    public void A_block_comment_cannot_contain_the_closing_marker()
    {
        CfgeditException ex = EditTestSupport.Fails("/* old */ {}", Jsonc, new SetPart(EditTestSupport.At(EditTestSupport.Comment(0)), PartKind.Comment, -1, "a */ b"));

        Assert.Equal("a block comment cannot contain */", ex.Message);
    }

    [Fact]
    public void A_line_comment_cannot_contain_a_line_break()
    {
        CfgeditException ex = EditTestSupport.Fails("// old\n{}", Jsonc, new SetPart(EditTestSupport.At(EditTestSupport.Comment(0)), PartKind.Comment, -1, "a\nb"));

        Assert.Equal("a line comment cannot contain a line break", ex.Message);
    }

    [Fact]
    public void A_container_has_no_editable_value_part()
    {
        CfgeditException ex = EditTestSupport.Fails(
            "{\"a\": {}}", Strict, new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("a")), PartKind.Value, -1, "x"));

        Assert.Equal("This part cannot be edited", ex.Message);
    }

    [Fact]
    public void An_index_part_cannot_be_edited()
    {
        CfgeditException ex = EditTestSupport.Fails("[1]", Strict, new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Item(0)), PartKind.Index, -1, "5"));

        Assert.Equal("This part cannot be edited", ex.Message);
    }

    [Fact]
    public void A_missing_node_is_reported_with_its_path()
    {
        CfgeditException ex = EditTestSupport.Fails(
            "{\"a\": 1}", Strict, new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("x")), PartKind.Value, -1, "2"));

        Assert.Equal("There is no node at /Item(0)/Member(\"x\", 0)", ex.Message);
    }

    [Fact]
    public void Setting_a_value_moves_focus_to_the_same_node()
    {
        (string text, NodePath? focus) = EditTestSupport.EditWithFocus(
            "{\"a\": 1, \"b\": 2}", Strict, new SetPart(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("b")), PartKind.Value, -1, "3"));

        Assert.Equal("{\"a\": 1, \"b\": 3}", text);
        Assert.Equal(EditTestSupport.At(EditTestSupport.Item(0), EditTestSupport.Member("b")), focus);
    }
}
