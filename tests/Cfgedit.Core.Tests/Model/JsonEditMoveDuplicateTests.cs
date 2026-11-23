using Cfgedit.Core;
using Cfgedit.Core.Json;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>MoveNode and DuplicateNode on JSON: order, commas, attached comments, key copies and focus.</summary>
public sealed class JsonEditMoveDuplicateTests
{
    [Fact]
    public void A_member_moves_up_in_a_multi_line_object_and_the_comma_moves_with_it()
    {
        string text = Edit("{\n  \"a\": 1,\n  \"b\": 2,\n  \"c\": 3\n}", Strict, new MoveNode(At(Item(0), Member("c")), 0));

        Assert.Equal("{\n  \"c\": 3,\n  \"a\": 1,\n  \"b\": 2\n}", text);
    }

    [Fact]
    public void A_member_moves_down_to_the_end_of_a_multi_line_object()
    {
        string text = Edit("{\n  \"a\": 1,\n  \"b\": 2,\n  \"c\": 3\n}", Strict, new MoveNode(At(Item(0), Member("a")), 2));

        Assert.Equal("{\n  \"b\": 2,\n  \"c\": 3,\n  \"a\": 1\n}", text);
    }

    [Fact]
    public void A_member_moves_to_the_start_of_a_single_line_object()
    {
        string text = Edit("{\"a\": 1, \"b\": 2, \"c\": 3}", Strict, new MoveNode(At(Item(0), Member("c")), 0));

        Assert.Equal("{\"c\": 3, \"a\": 1, \"b\": 2}", text);
    }

    [Fact]
    public void A_member_moves_to_the_end_of_a_single_line_object()
    {
        string text = Edit("{\"a\": 1, \"b\": 2, \"c\": 3}", Strict, new MoveNode(At(Item(0), Member("a")), 2));

        Assert.Equal("{\"b\": 2, \"c\": 3, \"a\": 1}", text);
    }

    [Fact]
    public void Attached_comments_travel_with_the_member_they_belong_to()
    {
        string text = Edit("{\n  // about a\n  \"a\": 1,\n  \"b\": 2\n}", Jsonc, new MoveNode(At(Item(0), Member("a")), 1));

        Assert.Equal("{\n  \"b\": 2,\n  // about a\n  \"a\": 1\n}", text);
    }

    [Fact]
    public void A_trailing_comma_object_keeps_its_trailing_comma_when_a_member_moves_to_the_end()
    {
        string text = Edit("{\n  \"a\": 1,\n  \"b\": 2,\n}", Jsonc, new MoveNode(At(Item(0), Member("a")), 1));

        Assert.Equal("{\n  \"b\": 2,\n  \"a\": 1,\n}", text);
    }

    [Fact]
    public void An_item_moves_to_the_end_of_a_multi_line_array()
    {
        string text = Edit("[\n  1,\n  2,\n  3\n]", Strict, new MoveNode(At(Item(0), Item(0)), 2));

        Assert.Equal("[\n  2,\n  3,\n  1\n]", text);
    }

    [Fact]
    public void A_move_to_the_same_position_returns_the_same_document()
    {
        ConfigDocument document = ModelTestSupport.Parse("[1, 2]", Strict);

        EditResult result = document.Apply(new MoveNode(At(Item(0), Item(0)), 0));

        Assert.Same(document, result.Document);
    }

    [Fact]
    public void A_new_index_past_the_end_is_clamped_to_the_end()
    {
        string text = Edit("[1, 2]", Strict, new MoveNode(At(Item(0), Item(0)), 99));

        Assert.Equal("[2, 1]", text);
    }

    [Fact]
    public void A_document_level_comment_can_move_after_the_root_value()
    {
        string text = Edit("// c\n[1]", Jsonc, new MoveNode(At(Comment(0)), 1));

        Assert.Equal("[1]\n// c", text);
    }

    [Fact]
    public void A_move_reports_focus_at_the_new_index()
    {
        (string text, NodePath? focus) = EditWithFocus("[1, 2, 3]", Strict, new MoveNode(At(Item(0), Item(2)), 0));

        Assert.Equal("[3, 1, 2]", text);
        Assert.Equal(At(Item(0), Item(0)), focus);
    }

    [Fact]
    public void Duplicating_a_member_gives_the_copy_the_key_suffix_after_it()
    {
        string text = Edit("{\n  \"a\": 1,\n  \"b\": 2\n}", Strict, new DuplicateNode(At(Item(0), Member("a"))));

        Assert.Equal("{\n  \"a\": 1,\n  \"a2\": 1,\n  \"b\": 2\n}", text);
    }

    [Fact]
    public void The_copy_key_skips_suffixes_that_are_taken()
    {
        string text = Edit("{\"a\": 1, \"a2\": 2}", Strict, new DuplicateNode(At(Item(0), Member("a"))));

        Assert.Equal("{\"a\": 1, \"a3\": 1, \"a2\": 2}", text);
    }

    [Fact]
    public void Duplicating_the_last_member_adds_a_comma_to_the_original()
    {
        string text = Edit("{\n  \"a\": 1,\n  \"b\": 2\n}", Strict, new DuplicateNode(At(Item(0), Member("b"))));

        Assert.Equal("{\n  \"a\": 1,\n  \"b\": 2,\n  \"b2\": 2\n}", text);
    }

    [Fact]
    public void Duplicating_an_item_copies_it_as_it_is()
    {
        string text = Edit("[1, 2]", Strict, new DuplicateNode(At(Item(0), Item(0))));

        Assert.Equal("[1, 1, 2]", text);
    }

    [Fact]
    public void Duplicating_a_nested_container_copies_its_formatting()
    {
        string text = Edit("[[1], 2]", Strict, new DuplicateNode(At(Item(0), Item(0))));

        Assert.Equal("[[1], [1], 2]", text);
    }

    [Fact]
    public void Duplicating_a_multi_line_member_copies_its_nested_lines()
    {
        string text = Edit(
            "{\n  \"o\": {\n    \"x\": 1\n  }\n}",
            Strict,
            new DuplicateNode(At(Item(0), Member("o"))));

        Assert.Equal("{\n  \"o\": {\n    \"x\": 1\n  },\n  \"o2\": {\n    \"x\": 1\n  }\n}", text);
    }

    [Fact]
    public void Duplicating_a_member_copies_its_attached_comment()
    {
        string text = Edit("{\n  // about a\n  \"a\": 1\n}", Jsonc, new DuplicateNode(At(Item(0), Member("a"))));

        Assert.Equal("{\n  // about a\n  \"a\": 1,\n  // about a\n  \"a2\": 1\n}", text);
    }

    [Fact]
    public void The_root_value_cannot_be_duplicated()
    {
        CfgeditException ex = Fails("[1]", Strict, new DuplicateNode(RootValue));

        Assert.Equal("The root value cannot be duplicated", ex.Message);
    }

    [Fact]
    public void The_root_value_cannot_be_moved()
    {
        CfgeditException ex = Fails("[1]", Strict, new MoveNode(RootValue, 0));

        Assert.Equal("The root value cannot be moved", ex.Message);
    }

    [Fact]
    public void Duplicating_moves_focus_to_the_copy()
    {
        (string text, NodePath? focus) = EditWithFocus("[1, 2]", Strict, new DuplicateNode(At(Item(0), Item(0))));

        Assert.Equal("[1, 1, 2]", text);
        Assert.Equal(At(Item(0), Item(1)), focus);
    }
}

/// <summary>ChangeJsonType: every transition between kinds of value.</summary>
public sealed class JsonEditChangeTypeTests
{
    private static readonly NodePath A = At(Item(0), Member("a"));

    [Fact]
    public void A_string_becomes_a_number()
    {
        Assert.Equal("{\"a\": 5}", Edit("{\"a\": \"x\"}", Strict, new ChangeJsonType(A, JsonValueKind.Number, "5")));
    }

    [Fact]
    public void A_number_becomes_a_string_with_double_quotes()
    {
        Assert.Equal("{\"a\": \"hi\"}", Edit("{\"a\": 1}", Strict, new ChangeJsonType(A, JsonValueKind.String, "hi")));
    }

    [Fact]
    public void A_string_becomes_true()
    {
        Assert.Equal("{\"a\": true}", Edit("{\"a\": \"x\"}", Strict, new ChangeJsonType(A, JsonValueKind.True, "ignored")));
    }

    [Fact]
    public void A_number_becomes_false()
    {
        Assert.Equal("{\"a\": false}", Edit("{\"a\": 1}", Strict, new ChangeJsonType(A, JsonValueKind.False, null)));
    }

    [Fact]
    public void A_true_becomes_null()
    {
        Assert.Equal("{\"a\": null}", Edit("{\"a\": true}", Strict, new ChangeJsonType(A, JsonValueKind.Null, null)));
    }

    [Fact]
    public void A_number_becomes_an_empty_object()
    {
        Assert.Equal("{\"a\": {}}", Edit("{\"a\": 1}", Strict, new ChangeJsonType(A, JsonValueKind.Object, null)));
    }

    [Fact]
    public void An_object_with_contents_is_wrapped_in_an_array_and_its_contents_are_kept()
    {
        Assert.Equal("{\"a\": [{\"x\": 1}]}", Edit("{\"a\": {\"x\": 1}}", Strict, new ChangeJsonType(A, JsonValueKind.Array, null)));
    }

    [Fact]
    public void An_array_becomes_a_number_and_its_items_are_dropped()
    {
        Assert.Equal("{\"a\": 3}", Edit("{\"a\": [1, 2]}", Strict, new ChangeJsonType(A, JsonValueKind.Number, "3")));
    }

    [Fact]
    public void An_object_stays_an_object_but_its_contents_are_dropped()
    {
        Assert.Equal("{\"a\": {}}", Edit("{\"a\": {\"x\": 1}}", Strict, new ChangeJsonType(A, JsonValueKind.Object, null)));
    }

    [Fact]
    public void A_json5_string_keeps_its_single_quotes_when_its_type_stays_string()
    {
        Assert.Equal("{a: 'new'}", Edit("{a: 'x'}", Json5, new ChangeJsonType(A, JsonValueKind.String, "new")));
    }

    [Fact]
    public void A_string_without_a_value_becomes_the_empty_string()
    {
        Assert.Equal("{\"a\": \"\"}", Edit("{\"a\": 1}", Strict, new ChangeJsonType(A, JsonValueKind.String, null)));
    }

    [Fact]
    public void A_number_needs_a_value()
    {
        CfgeditException ex = Fails("{\"a\": 1}", Strict, new ChangeJsonType(A, JsonValueKind.Number, null));

        Assert.Equal("A number needs a value", ex.Message);
    }

    [Fact]
    public void A_changed_number_is_validated()
    {
        CfgeditException ex = Fails("{\"a\": 1}", Strict, new ChangeJsonType(A, JsonValueKind.Number, "1."));

        Assert.Equal("'1.' is not a valid number", ex.Message);
    }

    [Fact]
    public void The_root_value_can_change_its_type()
    {
        Assert.Equal("{}", Edit("[1]", Strict, new ChangeJsonType(RootValue, JsonValueKind.Object, null)));
    }

    [Fact]
    public void Changing_a_value_to_the_same_text_returns_the_same_document()
    {
        ConfigDocument document = ModelTestSupport.Parse("{\"a\": 1}", Strict);

        EditResult result = document.Apply(new ChangeJsonType(A, JsonValueKind.Number, "1"));

        Assert.Same(document, result.Document);
    }

    [Fact]
    public void A_comment_has_no_type()
    {
        CfgeditException ex = Fails("// c\n[1]", Jsonc, new ChangeJsonType(At(Comment(0)), JsonValueKind.Null, null));

        Assert.Equal("only a value has a type", ex.Message);
    }
}
