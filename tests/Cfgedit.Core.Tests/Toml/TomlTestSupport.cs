using System.Text;
using Cfgedit.Core.Toml;
using Shared.Core;

namespace Cfgedit.Core.Tests.Toml;

/// <summary>
/// Shared helpers for the TOML tests. The tiling walk here is written against the public tree only, so that it checks the
/// parser's output rather than repeating its code.
/// </summary>
internal static class TomlTestSupport
{
    public static SourceText Utf8(string text)
    {
        return SourceText.Create(text, Encoding.UTF8, false);
    }

    public static TomlDocument Parse(string text)
    {
        return TomlDocument.Parse(Utf8(text));
    }

    /// <summary>Joins lines with LF, so that a multi-line expectation reads as it is laid out.</summary>
    public static string Lines(params string[] lines)
    {
        return string.Join("\n", lines);
    }

    public static string FixturePath(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "toml", name);
    }

    /// <summary>Loads a fixture from the output folder and parses it through the byte path, as the editor does.</summary>
    public static TomlDocument Fixture(string name)
    {
        return TomlDocument.Parse(SourceText.Decode(File.ReadAllBytes(FixturePath(name)), name));
    }

    /// <summary>
    /// Every leaf span of the tree, in source order: each item's indent, its key and value (or header or comment), its trailing
    /// space, its comment and its line break, and within a value the brackets, whitespace, comments, items and commas. A correct
    /// tree yields spans that tile the text.
    /// </summary>
    public static List<TomlSpan> Leaves(TomlDocument doc)
    {
        var leaves = new List<TomlSpan>();
        foreach (TomlItem item in doc.Items)
        {
            AddItemLeaves(item, leaves);
        }

        return leaves;
    }

    /// <summary>The text of the leaf spans, printed in order.</summary>
    public static string Reassemble(TomlDocument doc)
    {
        var sb = new StringBuilder();
        foreach (TomlSpan leaf in Leaves(doc))
        {
            sb.Append(doc.Source.Text, leaf.Start, leaf.Length);
        }

        return sb.ToString();
    }

    /// <summary>Asserts that the leaves tile the text exactly: no gaps and no overlaps, from the start to the end.</summary>
    public static void AssertTiles(TomlDocument doc)
    {
        string text = doc.Source.Text;
        int position = 0;
        foreach (TomlSpan leaf in Leaves(doc))
        {
            if (leaf.Start != position)
            {
                Assert.Fail($"the spans do not tile: text from {Math.Min(position, leaf.Start)} to {Math.Max(position, leaf.Start)} "
                    + $"is {Quote(text, Math.Min(position, leaf.Start), Math.Max(position, leaf.Start))} and is not in a span");
            }

            Assert.True(leaf.End >= leaf.Start, "a span ends before it starts");
            position = leaf.End;
        }

        if (position != text.Length)
        {
            Assert.Fail($"the spans end at {position}, but the text is {text.Length} long; the rest is {Quote(text, position, text.Length)}");
        }
    }

    private static string Quote(string text, int start, int end)
    {
        return "\"" + text.Substring(start, Math.Min(end, text.Length) - start).Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    }

    /// <summary>Asserts that an offset is at the given 1-based line and column.</summary>
    public static void AssertAt(TomlDocument doc, int offset, int line, int column)
    {
        Assert.Equal(new LinePosition(line, column), doc.Source.GetPosition(offset));
    }

    /// <summary>Asserts that the text fails to parse with the message, and at the line and column.</summary>
    public static void AssertParseError(string text, string message, int line, int column)
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => Parse(text));
        Assert.Equal(message, ex.Message);
        Assert.Equal(line, ex.Line);
        Assert.Equal(column, ex.Column);
    }

    /// <summary>Asserts that the text fails to parse, with the message, and returns the exception for further checks.</summary>
    public static CfgeditException ParseError(string text, string message)
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => Parse(text));
        Assert.Equal(message, ex.Message);
        return ex;
    }

    /// <summary>The single item of a text that holds one line, parsed.</summary>
    public static TomlItem SingleItem(string text, out TomlDocument doc)
    {
        doc = Parse(text);
        return Assert.Single(doc.Items);
    }

    /// <summary>The single key/value item of a one-line text.</summary>
    public static TomlKeyValueItem SingleKeyValue(string text, out TomlDocument doc)
    {
        return Assert.IsType<TomlKeyValueItem>(SingleItem(text, out doc));
    }

    /// <summary>The value of a one-line <c>k = value</c> text.</summary>
    public static TomlValue ValueOf(string valueText)
    {
        return SingleKeyValue("k = " + valueText + "\n", out _).Value;
    }

    private static void AddItemLeaves(TomlItem item, List<TomlSpan> leaves)
    {
        leaves.Add(item.Indent);
        switch (item)
        {
            case TomlKeyValueItem kv:
                AddKeyLeaves(kv.Key, leaves);
                leaves.Add(kv.Assignment);
                AddValueLeaves(kv.Value, leaves);
                break;
            case TomlHeaderItem header:
                leaves.Add(header.OpenSpan);
                leaves.Add(header.SpaceAfterOpen);
                AddKeyLeaves(header.Key, leaves);
                leaves.Add(header.SpaceBeforeClose);
                leaves.Add(header.CloseSpan);
                break;
        }

        leaves.Add(item.TrailingSpace);
        if (item.Comment is { } comment)
        {
            leaves.Add(Span(comment));
        }

        leaves.Add(item.LineBreak);
    }

    private static void AddKeyLeaves(TomlKey key, List<TomlSpan> leaves)
    {
        for (int i = 0; i < key.Parts.Count; i++)
        {
            leaves.Add(Span(key.Parts[i]));
            if (i < key.Separators.Count)
            {
                leaves.Add(key.Separators[i]);
            }
        }
    }

    private static void AddValueLeaves(TomlValue value, List<TomlSpan> leaves)
    {
        switch (value)
        {
            case TomlArray array:
                leaves.Add(new TomlSpan(array.Start, array.Start + 1));
                foreach (TomlNode entry in array.Entries)
                {
                    switch (entry)
                    {
                        case TomlWhitespace or TomlComment:
                            leaves.Add(Span(entry));
                            break;
                        case TomlArrayItem item:
                            AddValueLeaves(item.Value, leaves);
                            foreach (TomlNode between in item.BeforeComma)
                            {
                                leaves.Add(Span(between));
                            }

                            if (item.Comma is { } comma)
                            {
                                leaves.Add(comma);
                            }

                            break;
                    }
                }

                leaves.Add(new TomlSpan(array.End - 1, array.End));
                break;

            case TomlInlineTable table:
                leaves.Add(new TomlSpan(table.Start, table.Start + 1));
                foreach (TomlNode entry in table.Entries)
                {
                    switch (entry)
                    {
                        case TomlWhitespace:
                            leaves.Add(Span(entry));
                            break;
                        case TomlInlineMember member:
                            AddKeyLeaves(member.Key, leaves);
                            leaves.Add(member.Assignment);
                            AddValueLeaves(member.Value, leaves);
                            leaves.Add(member.SpaceBeforeComma);
                            if (member.Comma is { } comma)
                            {
                                leaves.Add(comma);
                            }

                            break;
                    }
                }

                leaves.Add(new TomlSpan(table.End - 1, table.End));
                break;

            default:
                leaves.Add(Span(value));
                break;
        }
    }

    private static TomlSpan Span(TomlNode node)
    {
        return new TomlSpan(node.Start, node.End);
    }
}
