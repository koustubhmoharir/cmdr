namespace Cfgedit.Core.Yaml;

/// <summary>
/// An entry of a block mapping: its key, the spaces, the <c>:</c>, and its value. <see cref="Value"/> is null for an entry
/// with no value (<c>key:</c> with nothing after it). The line break and any comment after the entry belong to the mapping.
/// </summary>
public sealed class YamlMappingEntry : YamlCompositeNode
{
    internal YamlMappingEntry(IReadOnlyList<YamlNode> parts, YamlScalar key, YamlToken colon, YamlValueNode? value)
        : base(parts)
    {
        Key = key;
        Colon = colon;
        Value = value;
    }

    /// <summary>The key: a plain, single-quoted or double-quoted scalar on one line.</summary>
    public YamlScalar Key { get; }

    /// <summary>The <c>:</c> that follows the key.</summary>
    public YamlToken Colon { get; }

    /// <summary>The value, or null when the entry has none.</summary>
    public YamlValueNode? Value { get; }
}

/// <summary>An item of a block sequence: its <c>-</c> and its value, which is null for an empty item.</summary>
public sealed class YamlSequenceItem : YamlCompositeNode
{
    internal YamlSequenceItem(IReadOnlyList<YamlNode> parts, YamlToken dash, YamlValueNode? value)
        : base(parts)
    {
        Dash = dash;
        Value = value;
    }

    /// <summary>The <c>-</c>.</summary>
    public YamlToken Dash { get; }

    /// <summary>The value, or null when the item is empty.</summary>
    public YamlValueNode? Value { get; }
}

/// <summary>
/// An entry of a flow mapping. <see cref="Colon"/> and <see cref="Value"/> are null for a key with no value (<c>{ a, b }</c>).
/// </summary>
public sealed class YamlFlowMappingEntry : YamlCompositeNode
{
    internal YamlFlowMappingEntry(IReadOnlyList<YamlNode> parts, YamlScalar key, YamlToken? colon, YamlValueNode? value)
        : base(parts)
    {
        Key = key;
        Colon = colon;
        Value = value;
    }

    /// <summary>The key: a plain, single-quoted or double-quoted scalar on one line.</summary>
    public YamlScalar Key { get; }

    /// <summary>The <c>:</c> after the key, or null when the entry has no value.</summary>
    public YamlToken? Colon { get; }

    /// <summary>The value, or null when the entry has none.</summary>
    public YamlValueNode? Value { get; }
}

/// <summary>A directive, such as <c>%YAML 1.2</c> or <c>%TAG !e! tag:example.com,2000:</c>.</summary>
public sealed class YamlDirective : YamlCompositeNode
{
    internal YamlDirective(IReadOnlyList<YamlNode> parts, string name, string arguments)
        : base(parts)
    {
        Name = name;
        Arguments = arguments;
    }

    /// <summary>The name without the <c>%</c>: <c>YAML</c> or <c>TAG</c>.</summary>
    public string Name { get; }

    /// <summary>The arguments, as written, without spaces around them. Empty when there are none.</summary>
    public string Arguments { get; }
}

/// <summary>
/// One document of a stream: its directives, its <c>---</c> marker (when written), its root value, and its <c>...</c> marker (when
/// written). The comments and blank lines around them are parts too.
/// </summary>
public sealed class YamlDocumentNode : YamlCompositeNode
{
    internal YamlDocumentNode(
        IReadOnlyList<YamlNode> parts,
        IReadOnlyList<YamlDirective> directives,
        YamlToken? startMarker,
        YamlValueNode? root,
        YamlToken? endMarker)
        : base(parts)
    {
        Directives = directives;
        StartMarker = startMarker;
        Root = root;
        EndMarker = endMarker;
    }

    /// <summary>The directives, in source order.</summary>
    public IReadOnlyList<YamlDirective> Directives { get; }

    /// <summary>The <c>---</c> marker, or null for an implicit document.</summary>
    public YamlToken? StartMarker { get; }

    /// <summary>The root value, or null for a document with no content.</summary>
    public YamlValueNode? Root { get; }

    /// <summary>The <c>...</c> marker, or null when the document ends implicitly.</summary>
    public YamlToken? EndMarker { get; }
}
