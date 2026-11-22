using System.Globalization;
using System.Text;
using Cfgedit.Core.Model;

namespace Cfgedit.Tui;

/// <summary>
/// The detail line's path and kind of a focused part. JSON paths are <c>$.a[2]</c>, with <c>["key"]</c> for keys that are not
/// identifiers. XML paths are XPath-like: <c>/configuration/add[2]/@value</c>.
/// </summary>
public static class PathText
{
    private const string Root = "$";

    /// <summary>The path of a node, or of one of its parts. For an attribute part, the path ends with the attribute.</summary>
    /// <param name="document">The document the node belongs to.</param>
    /// <param name="node">The node.</param>
    /// <param name="part">The focused part, or null for the node itself.</param>
    public static string Format(ConfigDocument document, DocNode node, DocPart? part)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);

        if (document.Format is not XmlFormat)
        {
            return JsonPath(node);
        }

        string path = XmlPath(node);
        if (part is { Kind: PartKind.AttributeName or PartKind.AttributeValue } attribute)
        {
            string name = AttributeName(node, attribute.AttributeIndex);
            return (path == "/" ? string.Empty : path) + "/@" + name;
        }

        return path;
    }

    /// <summary>
    /// The path of a member, attribute or child element that a row does not have yet, for a missing table cell: the row's path, then the
    /// name in the notation of the path (<c>.name</c> or <c>["name"]</c> in JSON, <c>/@name</c> for an attribute and <c>/name</c> for a
    /// child element in XML).
    /// </summary>
    /// <param name="document">The document the row belongs to.</param>
    /// <param name="row">The row's node.</param>
    /// <param name="name">The name of the member, attribute or child element.</param>
    /// <param name="attribute">True for an XML attribute.</param>
    public static string WithName(ConfigDocument document, DocNode row, string name, bool attribute)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(name);
        if (document.Format is not XmlFormat)
        {
            return JsonPath(row) + (IsIdentifier(name) ? "." + name : "[" + JsonQuote(name) + "]");
        }

        return XmlPath(row) + (attribute ? "/@" : "/") + name;
    }

    /// <summary>
    /// The kind of a focused part, for the detail line: <c>string</c>, <c>number</c>, <c>bool</c>, <c>null</c>, <c>object</c>,
    /// <c>array</c>, <c>element</c>, <c>attribute</c>, <c>text</c>, <c>comment</c>, <c>CDATA</c> or <c>PI</c>. A key or an index
    /// is <c>key</c> or <c>index</c>, the JSON root marker is <c>root</c>, and a declaration or doctype is <c>declaration</c> or <c>doctype</c>.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="part">The focused part, or null for the node itself.</param>
    public static string KindName(DocNode node, DocPart? part)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (part is null)
        {
            return NodeKindName(node);
        }

        return part.Kind switch
        {
            PartKind.Root => "root",
            PartKind.Key => "key",
            PartKind.Index => "index",
            PartKind.Value => ScalarName(node.JsonKind),
            PartKind.Summary => node.Kind switch
            {
                NodeKind.JsonArray => "array",
                NodeKind.XmlElement => "element",
                _ => "object",
            },
            PartKind.ElementName or PartKind.EndName => "element",
            PartKind.AttributeName or PartKind.AttributeValue => "attribute",
            PartKind.Text => node.Kind == NodeKind.XmlCData ? "CDATA" : "text",
            PartKind.Comment => "comment",
            PartKind.PITarget or PartKind.PIData => "PI",
            PartKind.ReadOnly => NodeKindName(node),
            _ => NodeKindName(node),
        };
    }

    private static string NodeKindName(DocNode node)
    {
        return node.Kind switch
        {
            NodeKind.Document => "document",
            NodeKind.JsonObject => "object",
            NodeKind.JsonArray => "array",
            NodeKind.JsonScalar => ScalarName(node.JsonKind),
            NodeKind.JsonComment or NodeKind.XmlComment => "comment",
            NodeKind.XmlElement => "element",
            NodeKind.XmlText => "text",
            NodeKind.XmlCData => "CDATA",
            NodeKind.XmlProcessingInstruction => "PI",
            NodeKind.XmlDeclaration => "declaration",
            NodeKind.XmlDoctype => "doctype",
            _ => throw new ArgumentOutOfRangeException(nameof(node), node.Kind, "Unknown node kind."),
        };
    }

    private static string ScalarName(JsonValueKind? kind)
    {
        return kind switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "bool",
            JsonValueKind.Null => "null",
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            _ => "value",
        };
    }

    // ----- JSON -----

    private static string JsonPath(DocNode node)
    {
        var builder = new StringBuilder(Root);
        List<DocNode> chain = Chain(node);
        for (int i = 0; i < chain.Count; i++)
        {
            PathStep step = chain[i].Path.Steps[^1];
            switch (step)
            {
                case ItemStep when i == 0:
                    // The root value: the path starts at "$".
                    break;
                case MemberStep member:
                    builder.Append(IsIdentifier(member.Key) ? "." + member.Key : "[" + JsonQuote(member.Key) + "]");

                    // A later member with the same key (a duplicate key, which JSON allows) is told apart by its 1-based
                    // occurrence; the first one stays plain, so files without duplicates never show the suffix.
                    if (member.Ordinal > 0)
                    {
                        builder.Append('#').Append((member.Ordinal + 1).ToString(CultureInfo.InvariantCulture));
                    }

                    break;
                case ItemStep item:
                    builder.Append('[').Append(item.Index.ToString(CultureInfo.InvariantCulture)).Append(']');
                    break;
                case CommentStep comment:
                    builder.Append("/#comment[").Append((comment.Index + 1).ToString(CultureInfo.InvariantCulture)).Append(']');
                    break;
                default:
                    throw new InvalidOperationException("Unexpected JSON path step " + step + ".");
            }
        }

        return builder.ToString();
    }

    private static bool IsIdentifier(string key)
    {
        if (key.Length == 0 || char.IsAsciiDigit(key[0]))
        {
            return false;
        }

        foreach (char c in key)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '$'))
            {
                return false;
            }
        }

        return true;
    }

    private static string JsonQuote(string text)
    {
        var builder = new StringBuilder(text.Length + 2);
        builder.Append('"');
        foreach (char c in text)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    // ----- XML -----

    private static string XmlPath(DocNode node)
    {
        List<DocNode> chain = Chain(node);
        if (chain.Count == 0)
        {
            return "/";
        }

        var builder = new StringBuilder();
        for (int i = 0; i < chain.Count; i++)
        {
            DocNode current = chain[i];
            DocNode parent = current.Parent ?? throw new InvalidOperationException("A node in the chain has no parent.");
            switch (current.Path.Steps[^1])
            {
                case ElementStep element:
                    builder.Append('/').Append(element.Name);
                    if (SameNameCount(parent, element.Name) > 1)
                    {
                        builder.Append('[').Append((element.Ordinal + 1).ToString(CultureInfo.InvariantCulture)).Append(']');
                    }

                    break;
                case DeclarationStep:
                    builder.Append("/?xml");
                    break;
                case DoctypeStep:
                    builder.Append("/!DOCTYPE");
                    break;
                case TextStep text:
                    builder.Append("/text()[").Append((text.Index + 1).ToString(CultureInfo.InvariantCulture)).Append(']');
                    break;
                case CommentStep comment:
                    builder.Append("/comment()[").Append((comment.Index + 1).ToString(CultureInfo.InvariantCulture)).Append(']');
                    break;
                case PIStep:
                    builder.Append("/processing-instruction(").Append(PITarget(current)).Append(')');
                    break;
                default:
                    throw new InvalidOperationException("Unexpected XML path step " + current.Path.Steps[^1] + ".");
            }
        }

        return builder.ToString();
    }

    /// <summary>The number of element children of a parent that have the given name.</summary>
    private static int SameNameCount(DocNode parent, string name)
    {
        int count = 0;
        foreach (DocNode child in parent.Children)
        {
            if (child.Path.Steps[^1] is ElementStep element && element.Name == name)
            {
                count++;
            }
        }

        return count;
    }

    private static string PITarget(DocNode node)
    {
        foreach (DocPart part in node.Parts)
        {
            if (part.Kind == PartKind.PITarget)
            {
                return part.Value;
            }
        }

        throw new InvalidOperationException("A processing instruction has no target part.");
    }

    private static string AttributeName(DocNode element, int index)
    {
        foreach (DocPart part in element.Parts)
        {
            if (part.Kind == PartKind.AttributeName && part.AttributeIndex == index)
            {
                return part.Value;
            }
        }

        throw new InvalidOperationException("The element has no attribute " + index.ToString(CultureInfo.InvariantCulture) + ".");
    }

    // ----- Shared -----

    /// <summary>The nodes from the top level down to the node, without the document node. Empty for the document node.</summary>
    private static List<DocNode> Chain(DocNode node)
    {
        var chain = new List<DocNode>();
        for (DocNode? current = node; current is not null && current.Parent is not null; current = current.Parent)
        {
            chain.Add(current);
        }

        chain.Reverse();
        return chain;
    }
}
