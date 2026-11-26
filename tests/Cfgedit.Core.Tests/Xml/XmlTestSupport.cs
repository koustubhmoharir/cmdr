using System.Text;
using Cfgedit.Core.Xml;
using Shared.Core;

namespace Cfgedit.Core.Tests.Xml;

/// <summary>
/// Shared helpers for the XML tree tests. The helpers load documents and slice text; every expectation is written in the
/// test as a literal.
/// </summary>
internal static class XmlTestSupport
{
    /// <summary>Parses a string as UTF-8 text, through <see cref="XmlDocumentSyntax.Parse(SourceText)"/>.</summary>
    public static XmlDocumentSyntax Parse(string text)
    {
        return XmlDocumentSyntax.Parse(SourceText.Create(text, Encoding.UTF8, false));
    }

    public static string FixturePath(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "xml", name);
    }

    /// <summary>Loads a fixture from the output folder and parses its bytes, as the editor does.</summary>
    public static XmlDocumentSyntax Fixture(string name)
    {
        return XmlDocumentSyntax.Parse(File.ReadAllBytes(FixturePath(name)), name);
    }

    public static string Slice(XmlDocumentSyntax doc, int start, int end)
    {
        return doc.Source.Text[start..end];
    }

    public static string Slice(XmlDocumentSyntax doc, XmlNodeSyntax node)
    {
        return Slice(doc, node.Start, node.End);
    }

    /// <summary>The nodes that are not whitespace text.</summary>
    public static List<XmlNodeSyntax> Significant(IEnumerable<XmlNodeSyntax> nodes)
    {
        return nodes.Where(n => n is not XmlTextSyntax { IsWhitespace: true }).ToList();
    }

    public static string[] ElementNames(IEnumerable<XmlNodeSyntax> nodes)
    {
        return nodes.OfType<XmlElementSyntax>().Select(e => e.Name).ToArray();
    }

    public static string[] AttributeNames(XmlElementSyntax element)
    {
        return element.Attributes.Select(a => a.Name).ToArray();
    }

    public static string AttributeValue(XmlElementSyntax element, string name)
    {
        return element.Attributes.Single(a => a.Name == name).Value;
    }

    public static XmlElementSyntax Child(XmlElementSyntax parent, string name)
    {
        return parent.Children.OfType<XmlElementSyntax>().Single(e => e.Name == name);
    }

    /// <summary>Asserts that an offset is at the given 1-based line and column.</summary>
    public static void AssertAt(XmlDocumentSyntax doc, int offset, int line, int column)
    {
        Assert.Equal(new LinePosition(line, column), doc.Source.GetPosition(offset));
    }

    /// <summary>Joins lines with LF, so that a multi-line expectation reads as it is laid out.</summary>
    public static string Lines(params string[] lines)
    {
        return string.Join("\n", lines);
    }
}
