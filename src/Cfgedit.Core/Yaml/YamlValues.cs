namespace Cfgedit.Core.Yaml;

/// <summary>A value node: a scalar, an alias, or a block or flow collection. It may carry an anchor and a tag.</summary>
public abstract class YamlValueNode : YamlCompositeNode
{
    internal YamlValueNode(IReadOnlyList<YamlNode> parts, YamlToken? anchor, YamlToken? tag)
        : base(parts)
    {
        Anchor = anchor;
        Tag = tag;
    }

    /// <summary>The anchor (<c>&amp;name</c>), or null.</summary>
    public YamlToken? Anchor { get; }

    /// <summary>The tag as written (for example <c>!!str</c>), or null. Tags are not resolved.</summary>
    public YamlToken? Tag { get; }
}

/// <summary>The five scalar styles.</summary>
public enum YamlScalarStyle
{
    /// <summary>A plain scalar, without quotes or indicators.</summary>
    Plain,

    /// <summary>A scalar in single quotes.</summary>
    SingleQuoted,

    /// <summary>A scalar in double quotes, with escapes.</summary>
    DoubleQuoted,

    /// <summary>A literal block scalar, <c>|</c>.</summary>
    Literal,

    /// <summary>A folded block scalar, <c>&gt;</c>.</summary>
    Folded,
}

/// <summary>The kinds of scalar in the YAML 1.2 core schema.</summary>
public enum YamlCoreKind
{
    /// <summary>null: <c>~</c>, <c>null</c>, <c>Null</c>, <c>NULL</c>, or an empty plain scalar.</summary>
    Null,

    /// <summary>A boolean: <c>true</c> or <c>false</c> in one of their three spellings.</summary>
    Boolean,

    /// <summary>An integer: decimal, <c>0o</c> octal or <c>0x</c> hexadecimal.</summary>
    Integer,

    /// <summary>A floating-point number, or <c>.inf</c> or <c>.nan</c>.</summary>
    Float,

    /// <summary>Any other text, which is a string.</summary>
    String,
}

/// <summary>
/// A scalar. <see cref="Value"/> is the decoded text: escapes are decoded, quotes are removed, and line folding and block
/// chomping are applied. A plain scalar with no text is <see cref="IsEmpty"/>; YAML reads it as null unless it is tagged.
/// </summary>
public sealed class YamlScalar : YamlValueNode
{
    internal YamlScalar(
        IReadOnlyList<YamlNode> parts, YamlToken? anchor, YamlToken? tag, YamlScalarStyle style, string value, bool isEmpty)
        : base(parts, anchor, tag)
    {
        Style = style;
        Value = value;
        IsEmpty = isEmpty;
    }

    /// <summary>The style the scalar is written in.</summary>
    public YamlScalarStyle Style { get; }

    /// <summary>The decoded text.</summary>
    public string Value { get; }

    /// <summary>True for a plain scalar with no text at all, such as the value of <c>key:</c> that carries only an anchor or a tag.</summary>
    public bool IsEmpty { get; }

    /// <summary>
    /// The core-schema kind of the value, for an untagged plain scalar. A quoted or block scalar is a string, and so is a plain
    /// scalar tagged <c>!</c> or <c>!!str</c>. Null for a plain scalar with another tag, whose type this layer does not resolve.
    /// </summary>
    public YamlCoreKind? Kind
    {
        get
        {
            if (Style != YamlScalarStyle.Plain)
            {
                return YamlCoreKind.String;
            }

            if (Tag is null)
            {
                return YamlEncoder.CoreSchemaKind(Value);
            }

            return Tag.Text is "!" or "!!str" ? YamlCoreKind.String : null;
        }
    }
}

/// <summary>An alias, <c>*name</c>. It is a reference, not expanded; <see cref="Name"/> is the anchor it refers to.</summary>
public sealed class YamlAlias : YamlValueNode
{
    internal YamlAlias(IReadOnlyList<YamlNode> parts, string name)
        : base(parts, null, null)
    {
        Name = name;
    }

    /// <summary>The anchor name, without the <c>*</c>.</summary>
    public string Name { get; }
}

/// <summary>
/// A block mapping: its entries and the lines between them, in source order. Its entries share one indentation; a line that
/// starts more to the left ends the mapping.
/// </summary>
public sealed class YamlBlockMapping : YamlValueNode
{
    internal YamlBlockMapping(IReadOnlyList<YamlNode> parts, YamlToken? anchor, YamlToken? tag, IReadOnlyList<YamlMappingEntry> entries)
        : base(parts, anchor, tag)
    {
        Entries = entries;
    }

    /// <summary>The entries, in source order.</summary>
    public IReadOnlyList<YamlMappingEntry> Entries { get; }
}

/// <summary>A block sequence: its items and the lines between them, in source order.</summary>
public sealed class YamlBlockSequence : YamlValueNode
{
    internal YamlBlockSequence(IReadOnlyList<YamlNode> parts, YamlToken? anchor, YamlToken? tag, IReadOnlyList<YamlSequenceItem> items)
        : base(parts, anchor, tag)
    {
        Items = items;
    }

    /// <summary>The items, in source order.</summary>
    public IReadOnlyList<YamlSequenceItem> Items { get; }
}

/// <summary>A flow mapping, <c>{ ... }</c>. Its entries and the commas, spaces and line breaks around them are parts, in source order.</summary>
public sealed class YamlFlowMapping : YamlValueNode
{
    internal YamlFlowMapping(IReadOnlyList<YamlNode> parts, YamlToken? anchor, YamlToken? tag, IReadOnlyList<YamlFlowMappingEntry> entries)
        : base(parts, anchor, tag)
    {
        Entries = entries;
    }

    /// <summary>The entries, in source order.</summary>
    public IReadOnlyList<YamlFlowMappingEntry> Entries { get; }
}

/// <summary>A flow sequence, <c>[ ... ]</c>. Its items and the commas, spaces and line breaks around them are parts, in source order.</summary>
public sealed class YamlFlowSequence : YamlValueNode
{
    internal YamlFlowSequence(IReadOnlyList<YamlNode> parts, YamlToken? anchor, YamlToken? tag, IReadOnlyList<YamlValueNode> items)
        : base(parts, anchor, tag)
    {
        Items = items;
    }

    /// <summary>The items, in source order.</summary>
    public IReadOnlyList<YamlValueNode> Items { get; }
}
