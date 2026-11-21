using Cfgedit.Core.Json;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>InsertNode on JSON: layout of new members and items, separators, quotes and comments.</summary>
public sealed class JsonEditInsertTests
{
    [Fact]
    public void A_member_is_appended_to_a_multi_line_object_after_the_last_member()
    {
        string text = Edit("{\n  \"a\": 1\n}", Strict, new InsertNode(At(Item(0)), 1, new NewJsonValue("b", JsonValueKind.Number, "2")));

        Assert.Equal("{\n  \"a\": 1,\n  \"b\": 2\n}", text);
    }

    [Fact]
    public void A_member_is_inserted_at_the_start_of_a_multi_line_object()
    {
        string text = Edit("{\n  \"a\": 1\n}", Strict, new InsertNode(At(Item(0)), 0, new NewJsonValue("z", JsonValueKind.True, null)));

        Assert.Equal("{\n  \"z\": true,\n  \"a\": 1\n}", text);
    }

    [Fact]
    public void An_item_is_inserted_in_the_middle_of_a_multi_line_array()
    {
        string text = Edit("[\n  1,\n  3\n]", Strict, new InsertNode(At(Item(0)), 1, new NewJsonValue(null, JsonValueKind.Number, "2")));

        Assert.Equal("[\n  1,\n  2,\n  3\n]", text);
    }

    [Fact]
    public void An_item_is_appended_to_a_single_line_array_with_the_separator_copied()
    {
        string text = Edit("[1, 2]", Strict, new InsertNode(At(Item(0)), 2, new NewJsonValue(null, JsonValueKind.Number, "3")));

        Assert.Equal("[1, 2, 3]", text);
    }

    [Fact]
    public void A_member_is_inserted_into_an_empty_single_line_object()
    {
        string text = Edit("{}", Strict, new InsertNode(At(Item(0)), 0, new NewJsonValue("a", JsonValueKind.Number, "1")));

        Assert.Equal("{\"a\": 1}", text);
    }

    [Fact]
    public void A_member_is_inserted_into_an_empty_multi_line_document_as_a_multi_line_object()
    {
        string text = Edit("{\n}", Strict, new InsertNode(At(Item(0)), 0, new NewJsonValue("a", JsonValueKind.Number, "1")));

        Assert.Equal("{\n  \"a\": 1\n}", text);
    }

    [Fact]
    public void A_string_item_is_inserted_into_an_empty_single_line_array()
    {
        string text = Edit("[]", Strict, new InsertNode(At(Item(0)), 0, new NewJsonValue(null, JsonValueKind.String, "x")));

        Assert.Equal("[\"x\"]", text);
    }

    [Fact]
    public void An_item_is_inserted_into_an_empty_array_nested_in_a_multi_line_object()
    {
        string text = Edit(
            "{\n  \"a\": []\n}",
            Strict,
            new InsertNode(At(Item(0), Member("a")), 0, new NewJsonValue(null, JsonValueKind.Number, "1")));

        Assert.Equal("{\n  \"a\": [\n    1\n  ]\n}", text);
    }

    [Fact]
    public void An_item_is_inserted_into_an_empty_object_when_the_indentation_unit_is_four_spaces()
    {
        string text = Edit(
            "{\n    \"a\": {}\n}",
            Strict,
            new InsertNode(At(Item(0), Member("a")), 0, new NewJsonValue("x", JsonValueKind.Number, "1")));

        Assert.Equal("{\n    \"a\": {\n        \"x\": 1\n    }\n}", text);
    }

    [Fact]
    public void A_member_in_a_trailing_comma_object_gets_its_own_trailing_comma()
    {
        string text = Edit("{\n  \"a\": 1,\n}", Jsonc, new InsertNode(At(Item(0)), 1, new NewJsonValue("b", JsonValueKind.Number, "2")));

        Assert.Equal("{\n  \"a\": 1,\n  \"b\": 2,\n}", text);
    }

    [Fact]
    public void A_member_in_a_single_line_trailing_comma_object_keeps_the_trailing_comma()
    {
        string text = Edit("{\"a\": 1,}", Jsonc, new InsertNode(At(Item(0)), 1, new NewJsonValue("b", JsonValueKind.Number, "2")));

        Assert.Equal("{\"a\": 1, \"b\": 2,}", text);
    }

    [Fact]
    public void A_member_copies_the_compact_colon_and_comma_separators()
    {
        string text = Edit("{\"a\":1,\"b\":2}", Strict, new InsertNode(At(Item(0)), 1, new NewJsonValue("c", JsonValueKind.Number, "3")));

        Assert.Equal("{\"a\":1,\"c\":3,\"b\":2}", text);
    }

    [Fact]
    public void A_member_copies_the_spaced_separators_of_its_siblings()
    {
        string text = Edit("{\"a\": 1, \"b\": 2}", Strict, new InsertNode(At(Item(0)), 1, new NewJsonValue("c", JsonValueKind.Number, "3")));

        Assert.Equal("{\"a\": 1, \"c\": 3, \"b\": 2}", text);
    }

    [Fact]
    public void A_member_in_a_tab_indented_object_is_indented_with_a_tab()
    {
        string text = Edit("{\n\t\"a\": 1\n}", Strict, new InsertNode(At(Item(0)), 1, new NewJsonValue("b", JsonValueKind.Number, "2")));

        Assert.Equal("{\n\t\"a\": 1,\n\t\"b\": 2\n}", text);
    }

    [Fact]
    public void A_new_line_in_a_crlf_document_is_crlf()
    {
        string text = Edit("{\r\n  \"a\": 1\r\n}", Strict, new InsertNode(At(Item(0)), 1, new NewJsonValue("b", JsonValueKind.Number, "2")));

        Assert.Equal("{\r\n  \"a\": 1,\r\n  \"b\": 2\r\n}", text);
    }

    [Fact]
    public void Appending_to_a_document_without_a_final_newline_keeps_it_without_one()
    {
        string text = Edit("{\n  \"a\": 1}", Strict, new InsertNode(At(Item(0)), 1, new NewJsonValue("b", JsonValueKind.Number, "2")));

        Assert.Equal("{\n  \"a\": 1,\n  \"b\": 2}", text);
    }

    [Fact]
    public void A_json5_key_is_bare_when_its_siblings_are_bare()
    {
        string text = Edit("{\n  a: 1\n}", Json5, new InsertNode(At(Item(0)), 1, new NewJsonValue("b", JsonValueKind.Number, "2")));

        Assert.Equal("{\n  a: 1,\n  b: 2\n}", text);
    }

    [Fact]
    public void A_json5_key_and_value_copy_the_single_quotes_of_a_single_quoted_document()
    {
        string text = Edit("{\n  'a': 'x'\n}", Json5, new InsertNode(At(Item(0)), 1, new NewJsonValue("b", JsonValueKind.String, "y")));

        Assert.Equal("{\n  'a': 'x',\n  'b': 'y'\n}", text);
    }

    [Fact]
    public void A_null_item_is_inserted_into_an_array()
    {
        string text = Edit("[]", Strict, new InsertNode(At(Item(0)), 0, new NewJsonValue(null, JsonValueKind.Null, null)));

        Assert.Equal("[null]", text);
    }

    [Fact]
    public void An_empty_object_is_inserted_after_an_item_with_its_comma()
    {
        string text = Edit("[1]", Strict, new InsertNode(At(Item(0)), 1, new NewJsonValue(null, JsonValueKind.Object, null)));

        Assert.Equal("[1, {}]", text);
    }

    [Fact]
    public void A_line_comment_is_inserted_on_its_own_line_before_the_entry_at_the_index()
    {
        string text = Edit("{\n  \"a\": 1\n}", Jsonc, new InsertNode(At(Item(0)), 0, new NewJsonComment(" note", false)));

        Assert.Equal("{\n  // note\n  \"a\": 1\n}", text);
    }

    [Fact]
    public void A_line_comment_is_inserted_after_the_last_entry_on_its_own_line_without_a_comma()
    {
        string text = Edit("{\n  \"a\": 1\n}", Jsonc, new InsertNode(At(Item(0)), 1, new NewJsonComment("x", false)));

        Assert.Equal("{\n  \"a\": 1\n  //x\n}", text);
    }

    [Fact]
    public void A_block_comment_is_inserted_between_items_of_a_single_line_array()
    {
        string text = Edit("[1, 2]", Jsonc, new InsertNode(At(Item(0)), 1, new NewJsonComment(" c ", true)));

        Assert.Equal("[1, /* c */ 2]", text);
    }

    [Fact]
    public void A_comment_is_inserted_at_the_document_level_before_the_root_value()
    {
        string text = Edit("[1]", Jsonc, new InsertNode(NodePath.Empty, 0, new NewJsonComment(" hi", false)));

        Assert.Equal("// hi\n[1]", text);
    }

    [Fact]
    public void A_comment_is_inserted_at_the_document_level_after_the_root_value()
    {
        string text = Edit("[1]", Jsonc, new InsertNode(NodePath.Empty, 1, new NewJsonComment(" hi", false)));

        Assert.Equal("[1]\n// hi", text);
    }

    [Fact]
    public void Inserting_moves_focus_to_the_new_node()
    {
        (string text, NodePath? focus) = EditWithFocus("{\"a\": 1}", Strict, new InsertNode(At(Item(0)), 1, new NewJsonValue("b", JsonValueKind.Number, "2")));

        Assert.Equal("{\"a\": 1, \"b\": 2}", text);
        Assert.Equal(At(Item(0), Member("b")), focus);
    }

    [Fact]
    public void Inserting_at_the_start_of_an_array_gives_the_new_item_index_zero()
    {
        string text = Edit("[1]", Strict, new InsertNode(At(Item(0)), 0, new NewJsonValue(null, JsonValueKind.Number, "0")));

        Assert.Equal("[0, 1]", text);
    }
}
