using System.Text;

namespace Cfgedit.Core.Model;

/// <summary>The kinds of logical node.</summary>
public enum LogicalKind
{
    /// <summary>The document node.</summary>
    Document,

    /// <summary>A JSON object.</summary>
    Object,

    /// <summary>A JSON array.</summary>
    Array,

    /// <summary>A JSON scalar. Its <see cref="LogicalNode.ScalarKind"/> says which.</summary>
    Scalar,

    /// <summary>A comment, in JSON or XML.</summary>
    Comment,

    /// <summary>An XML element.</summary>
    Element,

    /// <summary>XML character data: a text node, or the text of a text-only element.</summary>
    Text,

    /// <summary>An XML CDATA section.</summary>
    CData,

    /// <summary>An XML processing instruction. Its name is the target and its value is the data.</summary>
    ProcessingInstruction,

    /// <summary>The XML declaration. Its attributes are the version, encoding and standalone pseudo-attributes.</summary>
    Declaration,

    /// <summary>The XML document type declaration. Its name is the root name and its value is the internal subset.</summary>
    Doctype,

    /// <summary>A YAML alias. Its value is the anchor name it refers to; it is not expanded.</summary>
    Alias,
}

/// <summary>An attribute of a logical node: a name and its decoded value.</summary>
/// <param name="Name">The attribute name.</param>
/// <param name="Value">The decoded value.</param>
/// <param name="AnyValue">True in an expectation whose value is not known in advance: <see cref="LogicalNode.Matches"/> accepts any value.</param>
public sealed record LogicalAttribute(string Name, string Value, bool AnyValue = false);

/// <summary>
/// The content of a document with its formatting removed: kinds, names, decoded values, attributes in order, and children
/// (comments included). Two documents with equal logical trees say the same thing, whatever their layout. The tree is
/// immutable. Equality is by value, and <see cref="ToString"/> is an indented dump for failure messages.
/// </summary>
public sealed class LogicalNode : IEquatable<LogicalNode>
{
    /// <summary>Creates a logical node.</summary>
    /// <param name="kind">The kind of node.</param>
    /// <param name="name">The JSON key, the XML element name, or the processing instruction target. Null when there is none.</param>
    /// <param name="scalarKind">For a JSON scalar, its kind. Null otherwise.</param>
    /// <param name="value">The decoded value, or the raw number or literal text for JSON scalars. Null when there is none.</param>
    /// <param name="attributes">The attributes in document order.</param>
    /// <param name="children">The children in document order.</param>
    /// <param name="anyValue">True when the value is a wildcard in an expectation. See <see cref="AnyValue"/>.</param>
    /// <param name="tomlKind">For a TOML scalar, its kind. Null otherwise.</param>
    /// <param name="yamlKind">For a YAML scalar, its kind. Null otherwise.</param>
    /// <param name="yamlPlain">For a YAML scalar, true when it is written plain (without quotes or a block indicator).</param>
    /// <param name="yamlAnchor">For a YAML node with an anchor, the anchor name (without the <c>&amp;</c>). Null otherwise.</param>
    /// <param name="yamlTag">For a YAML node with a tag, the tag resolved to its full name, such as <c>tag:yaml.org,2002:str</c>. Null otherwise.</param>
    public LogicalNode(
        LogicalKind kind,
        string? name,
        JsonValueKind? scalarKind,
        string? value,
        IReadOnlyList<LogicalAttribute> attributes,
        IReadOnlyList<LogicalNode> children,
        bool anyValue = false,
        TomlValueKind? tomlKind = null,
        YamlValueKind? yamlKind = null,
        bool yamlPlain = false,
        string? yamlAnchor = null,
        string? yamlTag = null)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(children);
        Kind = kind;
        Name = name;
        ScalarKind = scalarKind;
        Value = value;
        Attributes = attributes;
        Children = children;
        AnyValue = anyValue;
        TomlKind = tomlKind;
        YamlKind = yamlKind;
        YamlPlain = yamlPlain;
        YamlAnchor = yamlAnchor;
        YamlTag = yamlTag;
    }

    /// <summary>For a TOML scalar, its kind. Null otherwise.</summary>
    public TomlValueKind? TomlKind { get; }

    /// <summary>For a YAML scalar, its kind. Null otherwise.</summary>
    public YamlValueKind? YamlKind { get; }

    /// <summary>For a YAML scalar, true when it is written plain. False otherwise.</summary>
    public bool YamlPlain { get; }

    /// <summary>For a YAML node with an anchor, the anchor name. Null otherwise.</summary>
    public string? YamlAnchor { get; }

    /// <summary>For a YAML node with a tag, the resolved tag name. Null otherwise.</summary>
    public string? YamlTag { get; }

    /// <summary>
    /// True when the value is a wildcard: an expectation for a value that is not known in advance, such as raw source text
    /// written by the user. <see cref="Matches"/> accepts any value for such a node. <see cref="Equals(LogicalNode?)"/> ignores
    /// the flag, and it is never set on a tree read from a document.
    /// </summary>
    public bool AnyValue { get; }

    /// <summary>
    /// True when <paramref name="actual"/> has the content <paramref name="expected"/> describes. The two must have the same
    /// kinds, names, scalar kinds, attribute names and child counts. A value must be equal, unless the expected node or
    /// attribute is a wildcard. <see cref="Equals(LogicalNode?)"/> stays exact.
    /// </summary>
    public static bool Matches(LogicalNode expected, LogicalNode actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        if (expected.Kind != actual.Kind
            || expected.Name != actual.Name
            || expected.ScalarKind != actual.ScalarKind
            || expected.TomlKind != actual.TomlKind
            || expected.YamlKind != actual.YamlKind
            || expected.YamlPlain != actual.YamlPlain
            || expected.YamlAnchor != actual.YamlAnchor
            || expected.YamlTag != actual.YamlTag
            || (!expected.AnyValue && expected.Value != actual.Value)
            || expected.Attributes.Count != actual.Attributes.Count
            || expected.Children.Count != actual.Children.Count)
        {
            return false;
        }

        for (int k = 0; k < expected.Attributes.Count; k++)
        {
            LogicalAttribute want = expected.Attributes[k];
            LogicalAttribute got = actual.Attributes[k];
            if (want.Name != got.Name || (!want.AnyValue && want.Value != got.Value))
            {
                return false;
            }
        }

        for (int k = 0; k < expected.Children.Count; k++)
        {
            if (!Matches(expected.Children[k], actual.Children[k]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The kind of node.</summary>
    public LogicalKind Kind { get; }

    /// <summary>The key or name, or null.</summary>
    public string? Name { get; }

    /// <summary>For a JSON scalar, its kind. Null otherwise.</summary>
    public JsonValueKind? ScalarKind { get; }

    /// <summary>The value, or null when the node has none. An empty string is a value.</summary>
    public string? Value { get; }

    /// <summary>The attributes in document order.</summary>
    public IReadOnlyList<LogicalAttribute> Attributes { get; }

    /// <summary>The children in document order, comments included.</summary>
    public IReadOnlyList<LogicalNode> Children { get; }

    /// <inheritdoc/>
    public bool Equals(LogicalNode? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Kind == other.Kind
            && Name == other.Name
            && ScalarKind == other.ScalarKind
            && TomlKind == other.TomlKind
            && YamlKind == other.YamlKind
            && YamlPlain == other.YamlPlain
            && YamlAnchor == other.YamlAnchor
            && YamlTag == other.YamlTag
            && Value == other.Value
            && Attributes.SequenceEqual(other.Attributes)
            && Children.SequenceEqual(other.Children);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is LogicalNode other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Name);
        hash.Add(ScalarKind);
        hash.Add(TomlKind);
        hash.Add(YamlKind);
        hash.Add(YamlPlain);
        hash.Add(YamlAnchor);
        hash.Add(YamlTag);
        hash.Add(Value);
        foreach (LogicalAttribute attribute in Attributes)
        {
            hash.Add(attribute);
        }

        foreach (LogicalNode child in Children)
        {
            hash.Add(child);
        }

        return hash.ToHashCode();
    }

    /// <summary>An indented dump of the tree, one node per line, for failure messages.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        Append(builder, 0);
        return builder.ToString().TrimEnd('\n');
    }

    private void Append(StringBuilder builder, int depth)
    {
        builder.Append(' ', depth * 2).Append(Kind);
        if (Name is not null)
        {
            builder.Append(" name=").Append(DisplayText.Quote(Name));
        }

        if (ScalarKind is { } scalarKind)
        {
            builder.Append(" scalar=").Append(scalarKind);
        }

        if (TomlKind is { } tomlKind)
        {
            builder.Append(" toml=").Append(tomlKind);
        }

        if (YamlKind is { } yamlKind)
        {
            builder.Append(" yaml=").Append(yamlKind);
        }

        if (YamlPlain)
        {
            builder.Append(" plain");
        }

        if (YamlAnchor is not null)
        {
            builder.Append(" &").Append(YamlAnchor);
        }

        if (YamlTag is not null)
        {
            builder.Append(" tag=").Append(DisplayText.Quote(YamlTag));
        }

        if (AnyValue)
        {
            builder.Append(" value=*");
        }
        else if (Value is not null)
        {
            builder.Append(" value=").Append(DisplayText.Quote(Value));
        }

        foreach (LogicalAttribute attribute in Attributes)
        {
            builder.Append(" @").Append(attribute.Name).Append('=')
                .Append(attribute.AnyValue ? "*" : DisplayText.Quote(attribute.Value));
        }

        builder.Append('\n');
        foreach (LogicalNode child in Children)
        {
            child.Append(builder, depth + 1);
        }
    }
}
