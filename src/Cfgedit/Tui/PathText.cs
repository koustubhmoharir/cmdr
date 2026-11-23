using System.Globalization;
using System.Text;
using Cfgedit.Core;
using Cfgedit.Core.Model;
using Cfgedit.Core.Yaml;

namespace Cfgedit.Tui;

/// <summary>
/// The detail line's path and kind of a focused part. JSON paths are <c>$.a[2]</c>, with <c>["key"]</c> for keys that are not
/// identifiers. XML paths are XPath-like: <c>/configuration/add[2]/@value</c>. .env paths are the full key, with <c>__</c> between
/// its segments (<c>Logging__LogLevel__Default</c>), and <c>$</c> for the root. TOML paths are <c>servers[1].port</c> and
/// <c>tool."my.key"</c>, with <c>$</c> for the root table and no prefix before a top-level key. YAML paths are as TOML's, with a
/// document as <c>---[1]</c> before its keys (<c>---[1].spec.replicas</c>) and keys quoted when they are not plain.
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

        if (document.Format is EnvFormat)
        {
            return EnvPath(node);
        }

        if (document.Format is TomlFormat)
        {
            return TomlPath(node);
        }

        if (document.Format is YamlFormat)
        {
            return YamlPath(node);
        }

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
        switch (document.Format)
        {
            case XmlFormat:
                return XmlPath(row) + (attribute ? "/@" : "/") + name;
            case TomlFormat:
                return TomlPath(row) + "." + (IsBareKey(name) ? name : JsonQuote(name));
            case YamlFormat:
                return YamlPath(row) + "." + YamlKey(name);
            default:
                return JsonPath(row) + (IsIdentifier(name) ? "." + name : "[" + JsonQuote(name) + "]");
        }
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
            PartKind.Index => node.Kind == NodeKind.YamlDocument ? "document" : "index",
            PartKind.Value => node.Kind switch
            {
                NodeKind.EnvEntry => "string",
                NodeKind.TomlScalar => TomlKindName(node.TomlKind),
                NodeKind.YamlScalar => YamlKindName(node.YamlKind),
                _ => ScalarName(node.JsonKind),
            },
            PartKind.Summary => node.Kind switch
            {
                NodeKind.JsonArray or NodeKind.TomlArray => "array",
                NodeKind.YamlSequence => "sequence",
                NodeKind.YamlMapping => "mapping",
                NodeKind.TomlTable => "table",
                NodeKind.XmlElement => "element",
                NodeKind.EnvGroup => EnvKindName(node),
                _ => "object",
            },
            PartKind.ElementName or PartKind.EndName => "element",
            PartKind.AttributeName or PartKind.AttributeValue => "attribute",
            PartKind.Text => node.Kind == NodeKind.XmlCData ? "CDATA" : "text",
            PartKind.Comment => "comment",
            PartKind.PITarget or PartKind.PIData => "PI",
            PartKind.Anchor => "anchor",
            PartKind.Tag => "tag",
            PartKind.Alias => "alias",
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
            NodeKind.EnvGroup or NodeKind.EnvEntry or NodeKind.EnvComment => EnvKindName(node),
            NodeKind.TomlTable => "table",
            NodeKind.TomlArray => "array",
            NodeKind.TomlScalar => TomlKindName(node.TomlKind),
            NodeKind.TomlComment => "comment",
            NodeKind.YamlDocument => "document",
            NodeKind.YamlMapping => "mapping",
            NodeKind.YamlSequence => "sequence",
            NodeKind.YamlScalar => YamlKindName(node.YamlKind),
            NodeKind.YamlAlias => "alias",
            NodeKind.YamlComment => "comment",
            _ => throw new ArgumentOutOfRangeException(nameof(node), node.Kind, "Unknown node kind."),
        };
    }

    /// <summary>
    /// The YAML kind of a scalar, by the core schema's names: <c>null</c>, <c>boolean</c>, <c>integer</c>, <c>float</c>, and <c>string</c> for
    /// a string, a quoted scalar or a block scalar.
    /// </summary>
    private static string YamlKindName(YamlValueKind? kind)
    {
        return kind switch
        {
            YamlValueKind.Null => "null",
            YamlValueKind.Boolean => "boolean",
            YamlValueKind.Integer => "integer",
            YamlValueKind.Float => "float",
            YamlValueKind.String => "string",
            _ => "value",
        };
    }

    /// <summary>
    /// The TOML kind of a scalar, by its name in the TOML spec: <c>string</c>, <c>integer</c>, <c>float</c>, <c>boolean</c>, and the four
    /// date-time forms. An array of tables is shown as <c>array</c>: the tree does not tell it from a static array.
    /// </summary>
    private static string TomlKindName(TomlValueKind? kind)
    {
        return kind switch
        {
            TomlValueKind.String => "string",
            TomlValueKind.Integer => "integer",
            TomlValueKind.Float => "float",
            TomlValueKind.Boolean => "boolean",
            TomlValueKind.OffsetDateTime => "offset date-time",
            TomlValueKind.LocalDateTime => "local date-time",
            TomlValueKind.LocalDate => "local date",
            TomlValueKind.LocalTime => "local time",
            _ => "value",
        };
    }

    /// <summary>The kind of a .env node: <c>root</c> for the file's group, else <c>group</c>, <c>entry</c> or <c>comment</c>.</summary>
    private static string EnvKindName(DocNode node)
    {
        return node.Kind switch
        {
            NodeKind.EnvGroup => node.Parent?.Kind == NodeKind.Document ? "root" : "group",
            NodeKind.EnvEntry => "entry",
            _ => "comment",
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

    // ----- .env -----

    /// <summary>
    /// The full key of an entry or group, its segments joined with <c>__</c>, or <c>$</c> for the root. A comment is its parent's path and
    /// <c>/#comment[n]</c>, as a JSON comment is. A later node with the same key (a duplicate entry) has its 1-based occurrence after the
    /// segment, as a JSON duplicate key does.
    /// </summary>
    private static string EnvPath(DocNode node)
    {
        if (node.Path.Steps[^1] is CommentStep comment)
        {
            DocNode parent = node.Parent ?? throw new InvalidOperationException("A comment has no parent.");
            return EnvPath(parent) + "/#comment[" + (comment.Index + 1).ToString(CultureInfo.InvariantCulture) + "]";
        }

        var segments = new List<string>();
        foreach (DocNode current in Chain(node))
        {
            if (current.Path.Steps[^1] is MemberStep member)
            {
                segments.Add(member.Ordinal > 0 ? member.Key + "#" + (member.Ordinal + 1).ToString(CultureInfo.InvariantCulture) : member.Key);
            }
        }

        return segments.Count == 0 ? Root : string.Join("__", segments);
    }

    // ----- TOML -----

    /// <summary>
    /// The path of a TOML node: <c>$</c> for the root table, then each key as <c>.key</c> (a top-level key has no dot, and a key that is
    /// not bare is quoted), and each array item as <c>[n]</c>. A comment is its parent's path and <c>/#comment[n]</c>, as in JSON. Quoted
    /// keys use JSON's escapes, which are TOML basic-string escapes too.
    /// </summary>
    private static string TomlPath(DocNode node)
    {
        if (node.Path.Steps[^1] is CommentStep comment)
        {
            DocNode parent = node.Parent ?? throw new InvalidOperationException("A comment has no parent.");
            return TomlPath(parent) + "/#comment[" + (comment.Index + 1).ToString(CultureInfo.InvariantCulture) + "]";
        }

        var builder = new StringBuilder();
        foreach (DocNode current in Chain(node))
        {
            switch (current.Path.Steps[^1])
            {
                case ItemStep when current.Parent?.Kind == NodeKind.Document:
                    // The root table: the path starts at "$".
                    break;
                case MemberStep member:
                    if (builder.Length > 0)
                    {
                        builder.Append('.');
                    }

                    builder.Append(IsBareKey(member.Key) ? member.Key : JsonQuote(member.Key));
                    break;
                case ItemStep item:
                    builder.Append('[').Append(item.Index.ToString(CultureInfo.InvariantCulture)).Append(']');
                    break;
                default:
                    throw new InvalidOperationException("Unexpected TOML path step " + current.Path.Steps[^1] + ".");
            }
        }

        return builder.Length == 0 ? Root : builder.ToString();
    }

    /// <summary>A TOML bare key: one or more ASCII letters, digits, <c>_</c> or <c>-</c>.</summary>
    private static bool IsBareKey(string key)
    {
        if (key.Length == 0)
        {
            return false;
        }

        foreach (char c in key)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    // ----- YAML -----

    /// <summary>
    /// The path of a YAML node, built as a TOML path is, from the root value down: a mapping member as <c>.key</c> (a first key has no dot,
    /// and a key that is not plain is quoted as the YAML file writes it), a sequence item as <c>[n]</c>. A document is <c>---[n]</c>, and its
    /// root adds nothing to it. The root of a single document is <c>$</c>. A comment is its parent's path and <c>/#comment[n]</c>.
    /// </summary>
    private static string YamlPath(DocNode node)
    {
        if (node.Path.Steps[^1] is CommentStep comment)
        {
            DocNode parent = node.Parent ?? throw new InvalidOperationException("A comment has no parent.");
            return YamlPath(parent) + "/#comment[" + (comment.Index + 1).ToString(CultureInfo.InvariantCulture) + "]";
        }

        var builder = new StringBuilder();
        foreach (DocNode current in Chain(node))
        {
            switch (current.Path.Steps[^1])
            {
                case ItemStep document when current.Kind == NodeKind.YamlDocument:
                    builder.Append("---[").Append(document.Index.ToString(CultureInfo.InvariantCulture)).Append(']');
                    break;
                case ItemStep when current.Parent?.Kind is NodeKind.Document or NodeKind.YamlDocument:
                    // The root value of the stream or of a document: the path starts at "$" (or at the document's marker).
                    break;
                case MemberStep member:
                    if (builder.Length > 0)
                    {
                        builder.Append('.');
                    }

                    builder.Append(YamlKey(member.Key));
                    break;
                case ItemStep item:
                    builder.Append('[').Append(item.Index.ToString(CultureInfo.InvariantCulture)).Append(']');
                    break;
                default:
                    throw new InvalidOperationException("Unexpected YAML path step " + current.Path.Steps[^1] + ".");
            }
        }

        return builder.Length == 0 ? Root : builder.ToString();
    }

    /// <summary>A key as the YAML file writes it: plain when it is plain-safe, else double-quoted. A key too long to write plain is quoted too.</summary>
    private static string YamlKey(string key)
    {
        try
        {
            return YamlEncoder.EncodeKey(key);
        }
        catch (CfgeditException)
        {
            return JsonQuote(key);
        }
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
