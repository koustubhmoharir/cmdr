using Cfgedit.Core.Json;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// Over every JSON fixture: every editable part takes a changed value and only its own span changes; every node can be
/// deleted. Apply's verification is the oracle for the edits, and the text outside the span is compared directly.
/// </summary>
public sealed class JsonEditPropertyTests
{
    public static IEnumerable<object[]> Fixtures()
    {
        yield return ["strict.json", "json", JsonDialect.Json];
        yield return ["lines.json", "json", JsonDialect.Json];
        yield return ["appsettings.jsonc", "jsonc", JsonDialect.Jsonc];
        yield return ["tsconfig.jsonc", "jsonc", JsonDialect.Jsonc];
        yield return ["example.json5", "json5", JsonDialect.Json5];
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Every_editable_part_takes_a_changed_value_and_only_its_span_changes(string file, string formatName, JsonDialect dialect)
    {
        ConfigDocument document = Load(file, formatName);
        int changed = 0;
        foreach (DocNode node in ModelTestSupport.AllNodes(document.Root))
        {
            foreach (DocPart part in node.Parts)
            {
                if (!part.Editable || NewValue(node, part, dialect) is not { } value)
                {
                    continue;
                }

                EditResult result = document.Apply(new SetPart(node.Path, part.Kind, part.AttributeIndex, value));

                AssertOnlySpanChanged(document.Source.Text, result.Document.Source.Text, part.Start, part.End);
                changed++;
            }
        }

        Assert.True(changed > 0, "No part was edited.");
    }

    public static IEnumerable<object[]> DeleteFixtures()
    {
        foreach (object[] fixture in Fixtures())
        {
            yield return [fixture[0], fixture[1]];
        }
    }

    [Theory]
    [MemberData(nameof(DeleteFixtures))]
    public void Every_node_can_be_deleted_and_the_result_verifies(string file, string formatName)
    {
        ConfigDocument document = Load(file, formatName);
        int deleted = 0;
        foreach (DocNode node in ModelTestSupport.AllNodes(document.Root))
        {
            if (node.Parent is null || (node.Parent.Kind == NodeKind.Document && node.Kind != NodeKind.JsonComment))
            {
                continue;
            }

            EditResult result = document.Apply(new DeleteNode(node.Path));

            Assert.False(ReferenceEquals(result.Document, document), $"Deleting {node.Path} changed nothing.");
            deleted++;
        }

        Assert.True(deleted > 0, "No node was deleted.");
    }

    /// <summary>
    /// The changed value for a part: a string gets "x" appended, a number "1" (or "1" in place of a number that cannot take it,
    /// such as Infinity), a key "_k" (skipped when that makes a duplicate), and a comment " c". Literals and containers have
    /// nothing to change.
    /// </summary>
    private static string? NewValue(DocNode node, DocPart part, JsonDialect dialect)
    {
        switch (part.Kind)
        {
            case PartKind.Key:
            {
                string key = part.Value + "_k";
                bool duplicate = node.Parent!.Children.Any(child =>
                    !ReferenceEquals(child, node) && child.Path.Count > 0 && child.Path.Steps[^1] is MemberStep member && member.Key == key);
                return duplicate ? null : key;
            }
            case PartKind.Value when node.JsonKind == JsonValueKind.String:
                return part.Value + "x";
            case PartKind.Value when node.JsonKind == JsonValueKind.Number:
            {
                string appended = part.Value + "1";
                return JsonSyntax.IsValidNumber(appended, dialect) ? appended : "1";
            }
            case PartKind.Comment:
                return part.Value + " c";
            default:
                return null;
        }
    }

    /// <summary>
    /// Asserts that the text changed only inside [start, end): the common prefix ends at or after start, and the common suffix
    /// starts at or before end.
    /// </summary>
    private static void AssertOnlySpanChanged(string before, string after, int start, int end)
    {
        int prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix
            && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
        {
            suffix++;
        }

        int changedEnd = before.Length - suffix;
        Assert.True(
            prefix >= start && changedEnd <= end,
            $"The text changed in [{prefix}, {changedEnd}), outside the part span [{start}, {end}).");
    }

    private static ConfigDocument Load(string file, string formatName)
    {
        string path = ModelTestSupport.FixturePath("json", file);
        return ConfigDocument.Load(File.ReadAllBytes(path), path, Formats.FromName(formatName)!);
    }
}
