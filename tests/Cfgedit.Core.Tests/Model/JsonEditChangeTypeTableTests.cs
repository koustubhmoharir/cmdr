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

        Assert.Equal("{\"a\": " + text + "}", result);
    }
}
