using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>ChangeJsonType for every pair of value kinds: each source value becomes the target, whatever it was.</summary>
public sealed class JsonEditChangeTypeTableTests
{
    /// <summary>One source document per kind: a string, a number, true, false, null, an object with contents, an array with contents.</summary>
    private static readonly string[] Sources =
    [
        "{\"a\": \"s\"}",
        "{\"a\": 1}",
        "{\"a\": true}",
        "{\"a\": false}",
        "{\"a\": null}",
        "{\"a\": {\"x\": 1}}",
        "{\"a\": [1, 2]}",
    ];

    /// <summary>One target per kind, with the value to give it and the text it must read as in the member.</summary>
    private static readonly (JsonValueKind Kind, string? Value, string Text)[] Targets =
    [
        (JsonValueKind.String, "v", "\"v\""),
        (JsonValueKind.Number, "7", "7"),
        (JsonValueKind.True, null, "true"),
        (JsonValueKind.False, null, "false"),
        (JsonValueKind.Null, null, "null"),
        (JsonValueKind.Object, null, "{}"),
        (JsonValueKind.Array, null, "[]"),
    ];

    public static IEnumerable<object[]> Pairs()
    {
        for (int source = 0; source < Sources.Length; source++)
        {
            for (int target = 0; target < Targets.Length; target++)
            {
                yield return [source, target];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Every_source_kind_becomes_every_target_kind(int source, int target)
    {
        (JsonValueKind kind, string? value, string text) = Targets[target];

        string result = Edit(Sources[source], Strict, new ChangeJsonType(At(Item(0), Member("a")), kind, value));

        Assert.Equal(Expected(source, target, text), result);
    }

    /// <summary>The member text after changing to an array: a value that is not an array is wrapped as the only item, an array stays.</summary>
    private static readonly string[] WrappedArrays =
    [
        "{\"a\": [\"s\"]}",
        "{\"a\": [1]}",
        "{\"a\": [true]}",
        "{\"a\": [false]}",
        "{\"a\": []}",
        "{\"a\": [{\"x\": 1}]}",
        "{\"a\": [1, 2]}",
    ];

    private static string Expected(int source, int target, string text)
    {
        if (Targets[target].Kind == JsonValueKind.Array)
        {
            return WrappedArrays[source];
        }

        return "{\"a\": " + text + "}";
    }

    private static readonly NodePath MemberA = At(Item(0), Member("a"));

    [Fact]
    public void A_scalar_member_is_wrapped_in_an_array()
    {
        Assert.Equal("{\"a\": [1]}", Edit("{\"a\": 1}", Strict, new ChangeJsonType(MemberA, JsonValueKind.Array, null)));
    }

    [Fact]
    public void A_scalar_array_item_is_wrapped_in_an_array()
    {
        Assert.Equal("[[1], 2]", Edit("[1, 2]", Strict, new ChangeJsonType(At(Item(0), Item(0)), JsonValueKind.Array, null)));
    }

    [Fact]
    public void A_null_becomes_an_empty_array_because_it_has_nothing_to_keep()
    {
        Assert.Equal("{\"a\": []}", Edit("{\"a\": null}", Strict, new ChangeJsonType(MemberA, JsonValueKind.Array, null)));
        Assert.Equal("[[], 2]", Edit("[null, 2]", Strict, new ChangeJsonType(At(Item(0), Item(0)), JsonValueKind.Array, null)));
    }

    [Fact]
    public void A_root_scalar_is_wrapped_in_an_array()
    {
        Assert.Equal("[1]", Edit("1", Strict, new ChangeJsonType(RootValue, JsonValueKind.Array, null)));
    }

    [Fact]
    public void A_single_line_object_is_wrapped_in_an_array_on_one_line()
    {
        Assert.Equal("{\"k\": [{\"a\": 1}]}", Edit("{\"k\": {\"a\": 1}}", Strict, new ChangeJsonType(At(Item(0), Member("k")), JsonValueKind.Array, null)));
    }

    [Fact]
    public void A_multi_line_object_becomes_a_multi_line_array_with_exact_indentation()
    {
        string text = "{\n  \"k\": {\n    \"a\": 1,\n    \"b\": 2\n  }\n}";
        string expected = "{\n  \"k\": [\n    {\n      \"a\": 1,\n      \"b\": 2\n    }\n  ]\n}";

        Assert.Equal(expected, Edit(text, Strict, new ChangeJsonType(At(Item(0), Member("k")), JsonValueKind.Array, null)));
    }

    [Fact]
    public void A_multi_line_array_item_is_wrapped_at_the_item_indentation()
    {
        string text = "[\n  {\n    \"a\": 1\n  }\n]";
        string expected = "[\n  [\n    {\n      \"a\": 1\n    }\n  ]\n]";

        Assert.Equal(expected, Edit(text, Strict, new ChangeJsonType(At(Item(0), Item(0)), JsonValueKind.Array, null)));
    }

    // ----- Unwrapping: a one-item array changes to its item's own type -----

    [Fact]
    public void A_one_item_array_unwraps_to_its_item_for_each_kind()
    {
        Assert.Equal("{\"a\": \"s\"}", Edit("{\"a\": [\"s\"]}", Strict, new ChangeJsonType(MemberA, JsonValueKind.String, null)));
        Assert.Equal("{\"a\": 1}", Edit("{\"a\": [1]}", Strict, new ChangeJsonType(MemberA, JsonValueKind.Number, null)));
        Assert.Equal("{\"a\": true}", Edit("{\"a\": [true]}", Strict, new ChangeJsonType(MemberA, JsonValueKind.True, null)));
        Assert.Equal("{\"a\": false}", Edit("{\"a\": [false]}", Strict, new ChangeJsonType(MemberA, JsonValueKind.False, null)));
        Assert.Equal("{\"a\": null}", Edit("{\"a\": [null]}", Strict, new ChangeJsonType(MemberA, JsonValueKind.Null, null)));
        Assert.Equal("{\"a\": {\"x\": 1}}", Edit("{\"a\": [{\"x\": 1}]}", Strict, new ChangeJsonType(MemberA, JsonValueKind.Object, null)));
    }

    [Fact]
    public void A_root_one_item_array_unwraps()
    {
        Assert.Equal("1", Edit("[1]", Strict, new ChangeJsonType(RootValue, JsonValueKind.Number, null)));
    }

    [Fact]
    public void An_element_of_another_kind_keeps_the_old_result()
    {
        Assert.Equal("{\"a\": \"\"}", Edit("{\"a\": [1]}", Strict, new ChangeJsonType(MemberA, JsonValueKind.String, null)));
        Assert.Equal("{\"a\": {}}", Edit("{\"a\": [true]}", Strict, new ChangeJsonType(MemberA, JsonValueKind.Object, null)));
    }

    [Fact]
    public void A_true_element_does_not_unwrap_for_false()
    {
        Assert.Equal("{\"a\": false}", Edit("{\"a\": [true]}", Strict, new ChangeJsonType(MemberA, JsonValueKind.False, null)));
    }

    [Fact]
    public void A_number_without_a_value_for_a_number_array_of_two_keeps_its_requirement()
    {
        CfgeditException error = Fails("{\"a\": [1, 2]}", Strict, new ChangeJsonType(MemberA, JsonValueKind.Number, null));

        Assert.Equal("A number needs a value", error.Message);
    }

    [Fact]
    public void A_two_item_array_keeps_the_old_result_with_a_value()
    {
        Assert.Equal("{\"a\": \"x\"}", Edit("{\"a\": [1, 2]}", Strict, new ChangeJsonType(MemberA, JsonValueKind.String, "x")));
    }

    [Fact]
    public void A_one_item_array_with_a_value_keeps_the_old_result()
    {
        Assert.Equal("{\"a\": 7}", Edit("{\"a\": [1]}", Strict, new ChangeJsonType(MemberA, JsonValueKind.Number, "7")));
    }

    [Fact]
    public void A_one_item_array_with_a_comment_is_refused()
    {
        CfgeditException error = Fails("{\"a\": [1 /* c */]}", Jsonc, new ChangeJsonType(MemberA, JsonValueKind.Number, null));

        Assert.Equal("the array has comments, which would be lost: delete them first", error.Message);
    }

    [Fact]
    public void A_multi_line_object_item_is_de_indented_to_the_member_indentation()
    {
        string text = "{\n  \"a\": [\n    {\n      \"x\": 1\n    }\n  ]\n}";

        Assert.Equal("{\n  \"a\": {\n    \"x\": 1\n  }\n}", Edit(text, Strict, new ChangeJsonType(MemberA, JsonValueKind.Object, null)));
    }

    [Fact]
    public void Lines_inside_a_multi_line_block_comment_of_an_unwrapped_item_keep_their_text()
    {
        string text = "{\n  \"a\": [\n    {\n      /* first\n    second */\n      \"x\": 1\n    }\n  ]\n}";
        string expected = "{\n  \"a\": {\n    /* first\n    second */\n    \"x\": 1\n  }\n}";

        Assert.Equal(expected, Edit(text, Jsonc, new ChangeJsonType(MemberA, JsonValueKind.Object, null)));
    }

    [Fact]
    public void Wrapping_then_unwrapping_gives_back_the_original_text()
    {
        string text = "{\n  \"k\": {\n    \"a\": 1,\n    \"b\": 2\n  }\n}";
        NodePath member = At(Item(0), Member("k"));

        string wrapped = Edit(text, Strict, new ChangeJsonType(member, JsonValueKind.Array, null));

        Assert.Equal(text, Edit(wrapped, Strict, new ChangeJsonType(member, JsonValueKind.Object, null)));
    }

    [Fact]
    public void Unwraps_is_true_only_for_a_one_item_array_whose_item_has_the_kind()
    {
        ConfigDocument one = ConfigDocument.Load("{\"a\": [1]}"u8, null, Strict);
        ConfigDocument two = ConfigDocument.Load("{\"a\": [1, 2]}"u8, null, Strict);

        Assert.True(one.Unwraps(new ChangeJsonType(MemberA, JsonValueKind.Number, null)));
        Assert.False(one.Unwraps(new ChangeJsonType(MemberA, JsonValueKind.Number, "7")));
        Assert.False(one.Unwraps(new ChangeJsonType(MemberA, JsonValueKind.String, null)));
        Assert.False(two.Unwraps(new ChangeJsonType(MemberA, JsonValueKind.Number, null)));
    }

    [Fact]
    public void A_multi_line_root_object_is_wrapped_in_a_multi_line_array()
    {
        Assert.Equal("[\n  {\n    \"a\": 1\n  }\n]", Edit("{\n  \"a\": 1\n}", Strict, new ChangeJsonType(RootValue, JsonValueKind.Array, null)));
    }

    [Fact]
    public void Lines_inside_a_multi_line_block_comment_keep_their_text()
    {
        string text = "{\n  \"k\": {\n    /* first\n    second */\n    \"a\": 1\n  }\n}";
        string expected = "{\n  \"k\": [\n    {\n      /* first\n    second */\n      \"a\": 1\n    }\n  ]\n}";

        Assert.Equal(expected, Edit(text, Jsonc, new ChangeJsonType(At(Item(0), Member("k")), JsonValueKind.Array, null)));
    }

    [Fact]
    public void A_trailing_comment_stays_after_the_value()
    {
        Assert.Equal("{\"a\": [1], // note\n\"b\": 2}", Edit("{\"a\": 1, // note\n\"b\": 2}", Jsonc, new ChangeJsonType(MemberA, JsonValueKind.Array, null)));
    }

    [Fact]
    public void An_array_changed_to_an_array_is_left_as_it_is()
    {
        Assert.Equal("{\"a\": [1,\n  2]}", Edit("{\"a\": [1,\n  2]}", Strict, new ChangeJsonType(MemberA, JsonValueKind.Array, null)));
    }
}
