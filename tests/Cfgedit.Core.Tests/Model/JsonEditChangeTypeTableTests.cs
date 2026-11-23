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
