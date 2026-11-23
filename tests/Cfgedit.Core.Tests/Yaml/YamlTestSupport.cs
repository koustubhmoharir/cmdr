using System.Text;
using Cfgedit.Core.Text;
using Cfgedit.Core.Yaml;

namespace Cfgedit.Core.Tests.Yaml;

/// <summary>
/// Shared helpers for the YAML tests. The tiling walk is written against the public tree only, so that it checks the parser's output
/// rather than repeating its code.
/// </summary>
internal static class YamlTestSupport
{
    public static SourceText Utf8(string text)
    {
        return SourceText.Create(text, Encoding.UTF8, false);
    }

    public static YamlDocument Parse(string text)
    {
        return YamlDocument.Parse(Utf8(text));
    }

    /// <summary>Joins lines with LF, so that a multi-line expectation reads as it is laid out.</summary>
    public static string Lines(params string[] lines)
    {
        return string.Join("\n", lines);
    }

    public static string FixturePath(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "yaml", name);
    }

    /// <summary>Loads a fixture from the output folder and parses it through the byte path, as the editor does.</summary>
    public static YamlDocument Fixture(string name)
    {
        return YamlDocument.Parse(SourceText.Decode(File.ReadAllBytes(FixturePath(name)), name));
    }

    /// <summary>The leaves of the tree in source order: each leaf is a token, a comment, or a composite node with no parts.</summary>
    public static List<YamlNode> Leaves(YamlDocument doc)
    {
        var leaves = new List<YamlNode>();
        foreach (YamlNode item in doc.Items)
        {
            AddLeaves(item, leaves);
        }

        return leaves;
    }

    private static void AddLeaves(YamlNode node, List<YamlNode> leaves)
    {
        if (node.Parts.Count == 0)
        {
            leaves.Add(node);
            return;
        }

        foreach (YamlNode part in node.Parts)
        {
            AddLeaves(part, leaves);
        }
    }

    /// <summary>The text of the leaves, printed in order.</summary>
    public static string Reassemble(YamlDocument doc)
    {
        var sb = new StringBuilder();
        foreach (YamlNode leaf in Leaves(doc))
        {
            sb.Append(doc.Source.Text, leaf.Start, leaf.End - leaf.Start);
        }

        return sb.ToString();
    }

    /// <summary>Asserts that the leaves tile the text exactly: no gaps and no overlaps, from the start to the end.</summary>
    public static void AssertTiles(YamlDocument doc)
    {
        int position = 0;
        foreach (YamlNode leaf in Leaves(doc))
        {
            if (leaf.Start != position)
            {
                Assert.Fail($"the leaves do not tile at {position}: the next leaf starts at {leaf.Start}");
            }

            position = leaf.End;
        }

        Assert.Equal(doc.Source.Text.Length, position);
    }

    /// <summary>Asserts that a text is refused with the message, and that the error points at the line and column.</summary>
    public static void AssertError(string text, string message, int line, int column)
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => Parse(text));
        Assert.Equal(message, ex.Message);
        Assert.Equal(line, ex.Line);
        Assert.Equal(column, ex.Column);
    }

    /// <summary>Asserts that a text is refused with the message, wherever it points.</summary>
    public static void AssertRefused(string text, string message)
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => Parse(text));
        Assert.Equal(message, ex.Message);
    }

    /// <summary>The single root mapping of a one-document text.</summary>
    public static YamlBlockMapping RootMapping(YamlDocument doc)
    {
        return Assert.IsType<YamlBlockMapping>(Assert.Single(doc.Documents).Root);
    }

    /// <summary>The decoded value of the entry with the key, for a mapping whose values are scalars.</summary>
    public static string ScalarValue(YamlBlockMapping mapping, string key)
    {
        YamlMappingEntry entry = mapping.Entries.Single(e => e.Key.Value == key);
        return Assert.IsType<YamlScalar>(entry.Value).Value;
    }

    /// <summary>The decoded value of the scalar that the single entry of a one-entry text holds, for value tests.</summary>
    public static string DecodeValue(string valueText)
    {
        YamlBlockMapping mapping = RootMapping(Parse("k: " + valueText + "\n"));
        return ScalarValue(mapping, "k");
    }
}
